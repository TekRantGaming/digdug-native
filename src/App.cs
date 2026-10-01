// SDL2 front end: window, rendering, keyboard + game controller input, audio, and the in-game menu.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DigDug
{
    public sealed unsafe class App
    {
        const long BootFrames = 1900;          // power-on self-test frames that are fast-forwarded
        const double FrameTime = 1.0 / 60.6061;
        const int W = Video.OutW, H = Video.OutH;

        // SDL scancodes
        const int ScA = 4, ScC = 6, ScD = 7, ScF = 9, ScP = 19, ScS = 22, ScW = 26, ScX = 27, ScZ = 29, Sc1 = 30, Sc2 = 31, Sc5 = 34, Sc6 = 35,
            ScReturn = 40, ScEscape = 41, ScTab = 43, ScSpace = 44, ScF1 = 58, ScF2 = 59, ScF3 = 60, ScF11 = 68,
            ScRight = 79, ScLeft = 80, ScDown = 81, ScUp = 82, ScKpEnter = 88, ScLCtrl = 224, ScRCtrl = 228;

        readonly Settings cfg;
        Machine machine;
        IntPtr win, ren, tex, ev;
        uint audio;
        byte* keys;
        // Controller state is tracked from SDL's button/axis *events* (the same path the menu uses), not by polling.
        sealed class Pad { public IntPtr Handle; public int Id; public readonly bool[] Btn = new bool[16]; public readonly short[] Axis = new short[6]; }
        readonly List<Pad> pads = new List<Pad>();
        readonly Stopwatch sw = Stopwatch.StartNew();
        bool running = true, paused, menuOpen, menuPausesGame;

        // input state
        readonly bool[] prevDir = new bool[4];
        int fireHeldFrames;
        int lastDir = -1;

        // menu
        enum Screen { Main, Options, Controls }
        Screen screen = Screen.Main;
        int sel;
        readonly int[] frameBuf = new int[W * H];
        short[] abuf = new short[1600];
        double sampleAcc;

        public readonly List<string[]> Script = new List<string[]>();
        public App(Settings s) { cfg = s; try { File.WriteAllText(Path.Combine(Settings.ConfigDir, "digdug.log"), "Dig Dug log " + DateTime.Now + "\n"); } catch { } }

        static void Log(string s) { try { File.AppendAllText(Path.Combine(Settings.ConfigDir, "digdug.log"), s + "\n"); } catch { } }
        double lastLog; int peak;

        // ================================================================== startup
        public int Run(RomSet roms, Dictionary<string, string> opts)
        {
            Sdl.SDL_SetHint("SDL_WINDOWS_DPI_AWARENESS", "permonitorv2");
            Sdl.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (Sdl.SDL_Init(Sdl.InitVideo | Sdl.InitAudio | Sdl.InitGameController | Sdl.InitEvents) != 0)
            {
                Console.Error.WriteLine("SDL_Init failed: " + Sdl.Error());
                return 1;
            }
            ev = Marshal.AllocHGlobal(64);
            ConfigureShot(opts);

            int sw0 = W * 3, sh0 = H * 3;
            SdlRect ub;
            if (Sdl.SDL_GetDisplayUsableBounds(0, out ub) == 0)
            {
                int scale = cfg.WindowScale > 0 ? cfg.WindowScale : Math.Max(2, (int)(ub.H * 0.86) / H);
                sw0 = W * scale; sh0 = H * scale;
            }
            win = Sdl.SDL_CreateWindow("Dig Dug", Sdl.WindowPosCentered, Sdl.WindowPosCentered, sw0, sh0, Sdl.WindowResizable | (cfg.Fullscreen ? Sdl.WindowFullscreenDesktop : 0));
            if (win == IntPtr.Zero) { Console.Error.WriteLine("Window failed: " + Sdl.Error()); return 1; }
            ren = Sdl.SDL_CreateRenderer(win, -1, Sdl.RendererAccelerated | Sdl.RendererPresentVSync);
            if (ren == IntPtr.Zero) ren = Sdl.SDL_CreateRenderer(win, -1, 0);
            if (ren == IntPtr.Zero) { Console.Error.WriteLine("Renderer failed: " + Sdl.Error()); return 1; }
            MakeTexture();
            Sdl.SDL_ShowCursor(cfg.Fullscreen ? 0 : 1);
            Sdl.SDL_EventState(Sdl.EvDropFile, 1);

            OpenAudio();
            for (int i = 0; i < Sdl.SDL_NumJoysticks(); i++) OpenPad(i);

            if (roms != null) StartMachine(roms, opts);
            else
            {
                // No modal dialogs here: a blocked window cannot receive a drag-and-drop. The "ROM required" page is
                // drawn in the window itself and the file picker (Enter / click / A) runs on a background thread.
                Sdl.SDL_SetWindowTitle(win, "Dig Dug - drop your digdug.zip onto this window");
                Log("No ROM set found; waiting for a drop or file pick");
            }

            MainLoop();

            if (machine != null) machine.SaveEarom(Settings.NvPath);
            cfg.Save();
            foreach (var p in pads) Sdl.SDL_GameControllerClose(p.Handle);
            if (audio != 0) Sdl.SDL_CloseAudioDevice(audio);
            if (tex != IntPtr.Zero) Sdl.SDL_DestroyTexture(tex);
            Sdl.SDL_DestroyRenderer(ren);
            Sdl.SDL_DestroyWindow(win);
            Sdl.SDL_Quit();
            return 0;
        }

        void StartMachine(RomSet roms, Dictionary<string, string> opts)
        {
            machine = new Machine(roms);
            machine.LoadEarom(Settings.NvPath);
            cfg.ApplyTo(machine);
            machine.Chip51.AutoCoin = cfg.AutoCoin;
            machine.Video.WantPfOnly = cfg.Widescreen;
            if (opts != null) Program.ApplyDipOptions(machine, opts);
            menuOpen = true; menuPausesGame = false; screen = Screen.Main; sel = 0;
            if (shotMenu == "options") { screen = Screen.Options; sel = 1; }
            else if (shotMenu == "controls") screen = Screen.Controls;
            else if (shotMenu == "off") menuOpen = false;
            Sdl.SDL_SetWindowTitle(win, "Dig Dug");
        }

        void MakeTexture()
        {
            if (tex != IntPtr.Zero) Sdl.SDL_DestroyTexture(tex);
            Sdl.SDL_SetHint("SDL_RENDER_SCALE_QUALITY", cfg.Smooth ? "linear" : "nearest");
            tex = Sdl.SDL_CreateTexture(ren, Sdl.PixelFormatArgb8888, Sdl.TextureAccessStreaming, texW, H);
        }


        // Builds the widescreen frame: the 224x288 game in the middle, dimmed mirrored dirt on either side, bright frame line at the playfield edge.
        int[] ComposeWide(int[] src, int tw)
        {
            if (wideBuf.Length != tw * H) wideBuf = new int[tw * H];
            int[] pf = machine.Video.PfOnly;
            int x0 = (tw - W) / 2;
            for (int y = 0; y < H; y++)
            {
                int row = y * tw;
                for (int x = 0; x < tw; x++)
                {
                    int cx = x - x0;
                    if (cx >= 0 && cx < W) { wideBuf[row + x] = src[y * W + cx]; continue; }
                    int dd = cx < 0 ? -cx - 1 : cx - W;
                    int mm = dd % (2 * W); int off = mm < W ? mm : 2 * W - 1 - mm;
                    int col = cx < 0 ? off : W - 1 - off;
                    int c = pf[y * W + col];
                    int v = (int)(0xff000000 | (uint)((c >> 1) & 0x7f7f7f));
                    if (y >= 264) v = unchecked((int)0xff000000);               // keep the status strip (lives icons) out of the extension
                    if (dd == 0) v = unchecked((int)0xffd0d0d0);
                    else if (dd == 1) v = unchecked((int)0xff000000);
                    wideBuf[row + x] = v;
                }
            }
            return wideBuf;
        }
        int texW = W;
        int[] wideBuf = new int[0];

        void OpenAudio()
        {
            var want = new SdlAudioSpec { Freq = Sound.SampleRate, Format = Sdl.AudioS16, Channels = 1, Samples = 1024 };
            SdlAudioSpec got;
            IntPtr name = string.IsNullOrEmpty(cfg.AudioDevice) ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(cfg.AudioDevice);
            audio = Sdl.SDL_OpenAudioDevice(name, 0, ref want, out got, 0);
            if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name);
            if (audio == 0 && !string.IsNullOrEmpty(cfg.AudioDevice))
            {
                Log("Could not open '" + cfg.AudioDevice + "': " + Sdl.Error() + " - falling back to default");
                cfg.AudioDevice = "";
                audio = Sdl.SDL_OpenAudioDevice(IntPtr.Zero, 0, ref want, out got, 0);
            }
            if (audio == 0) { Log("Audio unavailable: " + Sdl.Error()); return; }
            { var l = AudioDevices(); Log("Audio devices: " + string.Join(" | ", l.ToArray())); }
            Log("Audio driver " + Sdl.AudioDriver() + "; opened: " + got.Freq + " Hz, format " + got.Format.ToString("x") + ", " + got.Channels + " ch");
            Sdl.SDL_PauseAudioDevice(audio, 0);
        }

        void OpenPad(int index)
        {
            if (Sdl.SDL_IsGameController(index) == 0) return;
            IntPtr p = Sdl.SDL_GameControllerOpen(index);
            if (p == IntPtr.Zero) return;
            int id = Sdl.InstanceId(p);
            foreach (var q in pads) if (q.Id == id) return;
            var pad = new Pad { Handle = p, Id = id };
            for (int b = 0; b < 15; b++) pad.Btn[b] = Sdl.SDL_GameControllerGetButton(p, b) != 0;
            for (int a = 0; a < 6; a++) pad.Axis[a] = Sdl.SDL_GameControllerGetAxis(p, a);
            pads.Add(pad);
            Log("Controller connected: " + Sdl.ControllerName(p) + " (id " + id + ")");
        }

        Pad FindPad(int id) { foreach (var p in pads) if (p.Id == id) return p; return null; }

        // ================================================================== main loop
        void MainLoop()
        {
            double next = 0;
            while (running)
            {
                PumpEvents();
                if (machine == null)
                {
                    string pp = pendingPath;
                    if (pp != null) { pendingPath = null; OnDrop(pp); if (machine != null) continue; }
                    RenderRomRequired();
                    if (shotFile != null && ++noRomFrames == 20) { Png.Write(shotFile, frameBuf, W, H, 2); if (shotQuit) running = false; }
                    Sdl.SDL_Delay(30);
                    continue;
                }

                bool booting = machine.FrameCount < BootFrames;
                bool fast = booting || KeyDown(ScTab);
                double now = sw.Elapsed.TotalSeconds;

                if (fast && !(paused || (menuOpen && menuPausesGame)))
                {
                    double until = now + 0.012;
                    while (sw.Elapsed.TotalSeconds < until && (machine.FrameCount < BootFrames || KeyDown(ScTab)))
                    {
                        PollInput(); machine.RunFrame();
                    }
                    if (booting) RenderBlank(); else { machine.Video.Render(); Present(); }
                    next = sw.Elapsed.TotalSeconds;
                    continue;
                }

                if (now < next) { Sdl.SDL_Delay(1); continue; }
                next += FrameTime;
                if (now - next > 0.1) next = now;

                bool frozen = paused || (menuOpen && menuPausesGame);
                if (!frozen)
                {
                    InjectPadEvents();
                    PollInput();
                    machine.RunFrame();
                    PushAudio();
                }
                else if (audio != 0) Sdl.SDL_ClearQueuedAudio(audio);
                machine.Video.Render();
                if (shotFrame > 0 && machine.FrameCount >= shotFrame)
                {
                    if (shotWide > W) { machine.Video.WantPfOnly = true; machine.Video.Render(); Png.Write(shotFile, ComposeWide(BuildSource(), shotWide), shotWide, H, 2); }
                    else Png.Write(shotFile, BuildSource(), W, H, 2);
                    Log("saved frame " + machine.FrameCount + " to " + shotFile);
                    shotFrame = 0; if (shotQuit) running = false;
                }
                Present();
            }
        }

        // Test hook: script entries named "padN" (N = SDL controller button number) are injected as real SDL controller
        // events, so the event -> pad state -> game input path can be tested without touching hardware.
        void InjectPadEvents()
        {
            if (Script.Count == 0 || pads.Count == 0) return;
            long f = machine.FrameCount;
            foreach (var e in Script)
            {
                if (!e[1].StartsWith("pad") || e[1].StartsWith("padaxis")) continue;
                int start = int.Parse(e[0]), dur = e.Length > 2 ? int.Parse(e[2]) : 5;
                int btn = int.Parse(e[1].Substring(3));
                if (f == start) PushButton(pads[0].Id, btn, true);
                if (f == start + dur) PushButton(pads[0].Id, btn, false);
            }
        }

        void PushButton(int id, int btn, bool down)
        {
            IntPtr e = Marshal.AllocHGlobal(64);
            for (int i = 0; i < 64; i += 4) Marshal.WriteInt32(e, i, 0);
            Marshal.WriteInt32(e, 0, (int)(down ? Sdl.EvControllerButtonDown : Sdl.EvControllerButtonUp));
            Marshal.WriteInt32(e, 8, id);
            Marshal.WriteByte(e, 12, (byte)btn);
            Marshal.WriteByte(e, 13, (byte)(down ? 1 : 0));
            Sdl.SDL_PushEvent(e);
            Marshal.FreeHGlobal(e);
        }

        // automated tests: --shot-at <frame> --shot-file <png> [--shot-quit]
        long shotFrame; string shotFile; bool shotQuit; int shotWide; string shotMenu;
        void ConfigureShot(Dictionary<string, string> o)
        {
            string v;
            if (o != null && o.TryGetValue("shot-at", out v)) long.TryParse(v, out shotFrame);
            if (o != null && o.TryGetValue("shot-file", out v)) shotFile = v;
            shotQuit = o != null && o.ContainsKey("shot-quit");
            if (o != null && o.TryGetValue("shot-wide", out v)) int.TryParse(v, out shotWide);
            if (o != null && o.TryGetValue("shot-menu", out v)) shotMenu = v;
            if (shotFile == null) shotFrame = 0;
        }

        void PushAudio()
        {
            if (audio == 0) return;
            uint queued = Sdl.SDL_GetQueuedAudioSize(audio) / 2;
            if (queued > 6 * 800) return;                       // running ahead: drop this frame's audio
            sampleAcc += Sound.SampleRate * FrameTime;
            int n = (int)sampleAcc; sampleAcc -= n;
            if (queued < 400) n += 400;                         // refill after an underrun
            if (n > abuf.Length) n = abuf.Length;
            machine.Sound.Mix(abuf, 0, n);
            for (int k = 0; k < n; k++) { int a = Math.Abs((int)abuf[k]); if (a > peak) peak = a; }
            int vol = cfg.Volume;
            if (vol < 100) for (int i = 0; i < n; i++) abuf[i] = (short)(abuf[i] * vol / 100);
            fixed (short* p = abuf) Sdl.SDL_QueueAudio(audio, (IntPtr)p, (uint)(n * 2));
            double tn = sw.Elapsed.TotalSeconds;
            if (tn - lastLog > 5)
            {
                int big = 0; for (int s = 0; s < 64; s++) if (machine.Ram[0xb80 + s * 2] >= 0x80) big++;
                Log("frame " + machine.FrameCount + " big-sprite slots in RAM: " + big + " credits-menu:" + menuOpen);
                lastLog = tn; Log("t=" + tn.ToString("0") + "s audio queued=" + (Sdl.SDL_GetQueuedAudioSize(audio) / 2) + " samples, peak since last log " + peak); peak = 0; }
        }

        // ================================================================== rendering
        void RenderBlank()
        {
            Sdl.SDL_SetRenderDrawColor(ren, 0, 0, 0, 255);
            Sdl.SDL_RenderClear(ren);
            Sdl.SDL_RenderPresent(ren);
        }

        // the 224x288 image to show: the game frame, or the dimmed game with the menu drawn over it
        int[] BuildSource()
        {
            int[] src = machine.Video.Pixels;
            if (menuOpen)
            {
                for (int i = 0; i < src.Length; i++) { int c = src[i]; frameBuf[i] = (int)(0xff000000 | (uint)((c >> 2) & 0x3f3f3f)); }
                DrawMenu();
                src = frameBuf;
            }
            return src;
        }

        // ---- the page shown while no ROM set is loaded (uses the built-in font; no game data needed)
        volatile string pendingPath;
        string romError;
        int noRomFrames, animTick;
        volatile bool browsing;

        void RenderRomRequired()
        {
            animTick++;
            int bg = unchecked((int)0xff101830), gold = unchecked((int)0xffffd800), white = unchecked((int)0xffe8e8ff);
            int green = unchecked((int)0xff80ff80), red = unchecked((int)0xffff6060), dim = unchecked((int)0xff8890b0);
            for (int i = 0; i < frameBuf.Length; i++) frameBuf[i] = bg;
            Action<string, int, int, int, int> center = (s, y, col, scale, dummy) => Font5x7.Draw(frameBuf, W, H, s, (W - Font5x7.Width(s, scale)) / 2, y, col, scale);
            center("DIG DUG", 14, gold, 4, 0);
            center("NATIVE PORT", 50, white, 1, 0);
            center("ROM FILES REQUIRED", 74, red, 1, 0);
            center("THE GAME ROMS ARE NOT", 88, dim, 1, 0);
            center("INCLUDED - BRING YOUR OWN", 98, dim, 1, 0);

            // marching-ants drop box
            int bx0 = 14, by0 = 122, bx1 = W - 15, by1 = 206;
            for (int x = bx0; x <= bx1; x++)
                for (int k = 0; k < 2; k++)
                {
                    int y = k == 0 ? by0 : by1;
                    if (((x + animTick / 3) / 4) % 2 == 0) { frameBuf[y * W + x] = gold; frameBuf[(y + 1) * W + x] = gold; }
                }
            for (int y = by0; y <= by1; y++)
                for (int k = 0; k < 2; k++)
                {
                    int x = k == 0 ? bx0 : bx1;
                    if (((y + animTick / 3) / 4) % 2 == 0) { frameBuf[y * W + x] = gold; frameBuf[y * W + x + 1] = gold; }
                }
            center("DRAG AND DROP", 142, white, 2, 0);
            center("YOUR DIGDUG.ZIP", 164, gold, 2, 0);
            center("ONTO THIS WINDOW", 186, white, 1, 0);

            center(browsing ? "CHOOSE THE FILE IN THE DIALOG..." : "OR PRESS ENTER / CLICK / A", 220, green, 1, 0);
            center("TO BROWSE FOR IT", 232, green, 1, 0);
            if (romError != null) center(romError, 252, red, 1, 0);
            else center("SEE README FOR DETAILS", 264, dim, 1, 0);
            Present(frameBuf);
        }

        void Present(int[] fixedSrc = null)
        {
            int[] src = fixedSrc ?? BuildSource();
            int ow, oh; Sdl.SDL_GetRendererOutputSize(ren, out ow, out oh);

            // widescreen: extend the level sideways (dimmed, mirrored dirt) so wide screens aren't left with black bars;
            // a bright frame marks the real 224x288 playfield so nobody tries to walk into the extension.
            int tw = W;
            double ratio = oh > 0 ? (double)ow / oh : 0;
            if (fixedSrc == null && cfg.Widescreen && ratio > (double)W / H + 0.02) { tw = Math.Min(1280, (int)Math.Ceiling(H * ratio)); tw += tw & 1; if (tw < W) tw = W; }
            if (tw != texW) { texW = tw; MakeTexture(); }
            if (tw > W) src = ComposeWide(src, tw);
            fixed (int* p = src) Sdl.SDL_UpdateTexture(tex, IntPtr.Zero, (IntPtr)p, tw * 4);

            SdlRect dst;
            if (cfg.IntegerScale && ow >= tw && oh >= H)
            {
                int s = Math.Max(1, Math.Min(ow / tw, oh / H));
                dst = new SdlRect { W = tw * s, H = H * s };
            }
            else
            {
                double s = Math.Min((double)ow / tw, (double)oh / H);
                dst = new SdlRect { W = (int)(tw * s), H = (int)(H * s) };
            }
            dst.X = (ow - dst.W) / 2; dst.Y = (oh - dst.H) / 2;
            Sdl.SDL_SetRenderDrawColor(ren, 0, 0, 0, 255);
            Sdl.SDL_RenderClear(ren);
            Sdl.SDL_RenderCopy(ren, tex, IntPtr.Zero, ref dst);
            Sdl.SDL_RenderPresent(ren);
        }

        // ================================================================== input
        bool KeyDown(int sc) { return keys != null && keys[sc] != 0; }

        void PollInput()
        {
            int n; keys = (byte*)Sdl.SDL_GetKeyboardState(out n);
            var i = machine.Input;
            bool coin = KeyDown(Sc5) || KeyDown(ScC), start1 = KeyDown(Sc1) || KeyDown(ScReturn) || KeyDown(ScKpEnter), start2 = KeyDown(Sc2);
            bool fire = KeyDown(ScSpace) || KeyDown(ScLCtrl) || KeyDown(ScRCtrl) || KeyDown(ScZ) || KeyDown(ScX);
            var d = new bool[4];
            d[0] = KeyDown(ScUp) || KeyDown(ScW); d[1] = KeyDown(ScRight) || KeyDown(ScD);
            d[2] = KeyDown(ScDown) || KeyDown(ScS); d[3] = KeyDown(ScLeft) || KeyDown(ScA);

            foreach (var p in pads)
            {
                if (p.Btn[Sdl.BtnBack]) coin = true;
                if (p.Btn[Sdl.BtnStart]) start1 = true;
                if (p.Btn[Sdl.BtnLShoulder]) start2 = true;
                if (p.Btn[Sdl.BtnA] || p.Btn[Sdl.BtnB] || p.Btn[Sdl.BtnX] || p.Btn[Sdl.BtnY] || p.Btn[Sdl.BtnRShoulder]
                    || p.Axis[Sdl.AxisTriggerRight] > 8000 || p.Axis[Sdl.AxisTriggerLeft] > 8000) fire = true;
                if (p.Btn[Sdl.BtnUp]) d[0] = true;
                if (p.Btn[Sdl.BtnRight]) d[1] = true;
                if (p.Btn[Sdl.BtnDown]) d[2] = true;
                if (p.Btn[Sdl.BtnLeft]) d[3] = true;
                int ax = p.Axis[Sdl.AxisLX], ay = p.Axis[Sdl.AxisLY];
                if (Math.Max(Math.Abs(ax), Math.Abs(ay)) > 14000)
                {
                    if (Math.Abs(ax) >= Math.Abs(ay)) { if (ax > 0) d[1] = true; else d[3] = true; }
                    else { if (ay < 0) d[0] = true; else d[2] = true; }
                }
            }

            // Auto pump: the game inflates one step per release-and-re-press, so a steadily held button never inflates.
            // While fire is held, let go for a few frames every half second so holding the button keeps pumping.
            if (cfg.AutoPump && fire)
            {
                fireHeldFrames++;
                if (fireHeldFrames % 32 >= 28) fire = false;
            }
            else if (!fire) fireHeldFrames = 0;

            // 4-way stick: the most recently pressed direction wins
            for (int k = 0; k < 4; k++) if (d[k] && !prevDir[k]) lastDir = k;
            if (lastDir >= 0 && !d[lastDir]) { lastDir = -1; for (int k = 0; k < 4; k++) if (d[k]) { lastDir = k; break; } }
            for (int k = 0; k < 4; k++) prevDir[k] = d[k];

            i.Coin1 = coin; i.Coin2 = KeyDown(Sc6); i.Start1 = start1; i.Start2 = start2; i.Fire = fire;
            i.Service = KeyDown(ScF2);
            if (Script.Count > 0 && Script.Exists(s => !s[1].StartsWith("pad")))   // automated test input: --at frame:key:duration (same as the headless runner)
            {
                long f = machine.FrameCount + 1;
                foreach (var e in Script)
                {
                    int start = int.Parse(e[0]), dur = e.Length > 2 ? int.Parse(e[2]) : 5;
                    if (f < start || f >= start + dur) continue;
                    switch (e[1]) { case "coin1": coin = true; break; case "start1": start1 = true; break; case "fire": fire = true; break; }
                }
                i.Coin1 = coin; i.Start1 = start1; i.Fire = fire; i.Dir = -1;
                return;
            }
            i.Dir = (menuOpen ? -1 : lastDir) < 0 ? -1 : lastDir * 2;
            if (menuOpen) { i.Fire = false; i.Start1 = false; i.Start2 = false; i.Coin1 = false; }
        }

        void PumpEvents()
        {
            while (Sdl.SDL_PollEvent(ev) != 0)
            {
                uint type = (uint)Marshal.ReadInt32(ev, 0);
                switch (type)
                {
                    case Sdl.EvQuit: running = false; break;
                    case Sdl.EvMouseButtonDown: if (machine == null) BrowseForRom(); break;
                    case Sdl.EvKeyDown:
                        if (Marshal.ReadByte(ev, 13) == 0) OnKey(Marshal.ReadInt32(ev, 16));
                        break;
                    case Sdl.EvControllerAxis:
                        {
                            var pa = FindPad(Marshal.ReadInt32(ev, 8));
                            int ax = Marshal.ReadByte(ev, 12);
                            if (pa != null && ax < 6) pa.Axis[ax] = Marshal.ReadInt16(ev, 16);
                            break;
                        }
                    case Sdl.EvControllerButtonDown:
                    case Sdl.EvControllerButtonUp:
                        {
                            var pb = FindPad(Marshal.ReadInt32(ev, 8));
                            int b = Marshal.ReadByte(ev, 12);
                            bool down = type == Sdl.EvControllerButtonDown;
                            if (pb != null && b < 16) pb.Btn[b] = down;
                            if (down) { Log("pad button " + b); OnPadButton(b); }
                            break;
                        }
                    case Sdl.EvControllerDeviceAdded: OpenPad(Marshal.ReadInt32(ev, 8)); break;
                    case Sdl.EvControllerDeviceRemoved:
                        {
                            int id = Marshal.ReadInt32(ev, 8);
                            for (int i = pads.Count - 1; i >= 0; i--)
                                if (pads[i].Id == id) { Sdl.SDL_GameControllerClose(pads[i].Handle); pads.RemoveAt(i); Log("Controller removed (id " + id + ")"); }
                            break;
                        }
                    case Sdl.EvDropFile:
                        {
                            IntPtr sp = Marshal.ReadIntPtr(ev, 8);
                            string path = Marshal.PtrToStringUTF8(sp);
                            Sdl.SDL_free(sp);
                            OnDrop(path);
                            break;
                        }
                }
            }
        }

        // Native file picker (PowerShell/WinForms on Windows, zenity or kdialog on Linux) on a background thread,
        // so the window keeps running - and keeps accepting drag-and-drop - while the dialog is open.
        void BrowseForRom()
        {
            if (browsing) return;
            browsing = true;
            var th = new System.Threading.Thread(() =>
            {
                try { string p = PickFile(); if (!string.IsNullOrEmpty(p)) pendingPath = p; }
                finally { browsing = false; }
            }) { IsBackground = true, Name = "file picker" };
            th.Start();
        }

        string PickFile()
        {
            string[][] attempts;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                attempts = new[] { new[] { "powershell", "-NoProfile", "-STA", "-Command",
                    "Add-Type -AssemblyName System.Windows.Forms; $d = New-Object System.Windows.Forms.OpenFileDialog; $d.Title = 'Select your Dig Dug ROM zip'; $d.Filter = 'ROM set (*.zip)|*.zip|All files (*.*)|*.*'; if ($d.ShowDialog() -eq 'OK') { $d.FileName }" } };
            else
                attempts = new[] { new[] { "zenity", "--file-selection", "--title=Select your Dig Dug ROM zip" }, new[] { "kdialog", "--getopenfilename", ".", "*.zip" } };
            foreach (var a in attempts)
            {
                try
                {
                    var psi = new ProcessStartInfo(a[0]) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                    for (int i = 1; i < a.Length; i++) psi.ArgumentList.Add(a[i]);
                    using (var proc = Process.Start(psi))
                    {
                        string outp = proc.StandardOutput.ReadToEnd().Trim();
                        proc.WaitForExit();
                        return outp.Length > 0 ? outp.Split('\n')[0].Trim() : null;
                    }
                }
                catch (Exception ex) { Log("file dialog '" + a[0] + "' unavailable: " + ex.Message); }
            }
            romError = "NO FILE DIALOG - USE DRAG AND DROP";
            return null;
        }

        void OnDrop(string path)
        {
            Log("drop/open: " + path);
            if (machine != null || string.IsNullOrEmpty(path)) return;
            // a loose ROM file (not a .zip) dropped from an extracted set: use its folder
            if (File.Exists(path) && !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) path = Path.GetDirectoryName(path);
            try
            {
                var roms = RomSet.Load(path);
                cfg.RomPath = path; cfg.Save();
                romError = null;
                StartMachine(roms, null);
            }
            catch (Exception ex)
            {
                Log("ROM load failed: " + ex);
                string m = ex.Message;
                romError = m.StartsWith("Missing ROM files") ? "ROM SET INCOMPLETE - TRY AGAIN" : "NOT A DIG DUG ROM SET";
            }
        }
        void OnKey(int sc)
        {
            if (machine == null) { if (sc == ScReturn || sc == ScKpEnter) BrowseForRom(); return; }
            if (menuOpen)
            {
                switch (sc)
                {
                    case ScUp: case ScW: MenuMove(-1); break;
                    case ScDown: case ScS: MenuMove(1); break;
                    case ScLeft: case ScA: MenuAdjust(-1); break;
                    case ScRight: case ScD: MenuAdjust(1); break;
                    case ScReturn: case ScKpEnter: case ScSpace: case ScZ: MenuActivate(); break;
                    case ScEscape: MenuBack(); break;
                    case ScF11: ToggleFullscreen(); break;
                }
                return;
            }
            switch (sc)
            {
                case ScEscape: case ScF1: OpenMenu(); break;
                case ScP: paused = !paused; break;
                case ScF3: machine.Reset(); cfg.ApplyTo(machine); break;
                case ScF11: case ScF: ToggleFullscreen(); break;
            }
        }

        void OnPadButton(int b)
        {
            if (machine == null) { if (b == Sdl.BtnA || b == Sdl.BtnStart) BrowseForRom(); return; }
            if (menuOpen)
            {
                switch (b)
                {
                    case Sdl.BtnUp: MenuMove(-1); break;
                    case Sdl.BtnDown: MenuMove(1); break;
                    case Sdl.BtnLeft: MenuAdjust(-1); break;
                    case Sdl.BtnRight: MenuAdjust(1); break;
                    case Sdl.BtnA: case Sdl.BtnStart: MenuActivate(); break;
                    case Sdl.BtnB: case Sdl.BtnGuide: MenuBack(); break;
                }
                return;
            }
            if (b == Sdl.BtnGuide || b == Sdl.BtnRStick) OpenMenu();
        }

        // ================================================================== menu
        void OpenMenu() { menuOpen = true; menuPausesGame = true; screen = Screen.Main; sel = 0; }
        void CloseMenu() { menuOpen = false; cfg.Save(); }

        const int OptionCount = 14;
        int ItemCount { get { return screen == Screen.Main ? 5 : screen == Screen.Options ? OptionCount : 1; } }
        void MenuMove(int d) { sel = (sel + d + ItemCount) % ItemCount; }

        void MenuBack()
        {
            if (screen != Screen.Main) { screen = Screen.Main; sel = 0; }
            else if (menuPausesGame || machine.FrameCount > BootFrames) CloseMenu();
        }

        void MenuActivate()
        {
            if (screen == Screen.Main)
            {
                switch (sel)
                {
                    case 0: CloseMenu(); break;
                    case 1: screen = Screen.Options; sel = 0; break;
                    case 2: screen = Screen.Controls; sel = 0; break;
                    case 3: machine.Reset(); cfg.ApplyTo(machine); menuOpen = true; menuPausesGame = false; break;
                    case 4: running = false; break;
                }
            }
            else if (screen == Screen.Controls) { screen = Screen.Main; sel = 2; }
            else if (sel == OptionCount - 1) { screen = Screen.Main; sel = 1; cfg.Save(); }
            else if (sel == 6) PlayTestTone();
            else MenuAdjust(1);
        }

        // ---- audio device selection (some PCs route new apps to a virtual/other output, so let the user pick)
        List<string> AudioDevices()
        {
            var l = new List<string>();
            int n = Sdl.SDL_GetNumAudioDevices(0);
            for (int i = 0; i < n; i++) l.Add(Marshal.PtrToStringUTF8(Sdl.SDL_GetAudioDeviceName(i, 0)) ?? ("Device " + i));
            return l;
        }

        string AudioLabel()
        {
            if (string.IsNullOrEmpty(cfg.AudioDevice)) return "DEFAULT";
            var l = AudioDevices();
            int i = l.IndexOf(cfg.AudioDevice);
            string n = cfg.AudioDevice.ToUpperInvariant();
            var sb = new System.Text.StringBuilder((i >= 0 ? (i + 1) + " " : ""));
            foreach (char c in n) if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ') sb.Append(c);
            string s = sb.ToString().Trim();
            return s.Length > 14 ? s.Substring(0, 14) : s;
        }

        void CycleAudioDevice(int d)
        {
            var l = AudioDevices();
            int idx = string.IsNullOrEmpty(cfg.AudioDevice) ? -1 : l.IndexOf(cfg.AudioDevice);
            idx += d;
            if (idx < -1) idx = l.Count - 1;
            if (idx >= l.Count) idx = -1;
            cfg.AudioDevice = idx < 0 ? "" : l[idx];
            Log("audio device -> " + (idx < 0 ? "default" : l[idx]));
            if (audio != 0) { Sdl.SDL_CloseAudioDevice(audio); audio = 0; }
            OpenAudio();
            PlayTestTone();
        }

        void PlayTestTone()
        {
            if (audio == 0) return;
            Sdl.SDL_ClearQueuedAudio(audio);
            var t = new short[Sound.SampleRate / 2];
            for (int i = 0; i < t.Length; i++)
            {
                double env = Math.Min(1.0, Math.Min(i, t.Length - i) / 2000.0);
                double f = i < t.Length / 2 ? 440 : 660;
                t[i] = (short)(Math.Sin(2 * Math.PI * f * i / Sound.SampleRate) * 9000 * env * cfg.Volume / 100.0);
            }
            fixed (short* p = t) Sdl.SDL_QueueAudio(audio, (IntPtr)p, (uint)(t.Length * 2));
        }

        void MenuAdjust(int d)
        {
            if (screen != Screen.Options) return;
            switch (sel)
            {
                case 0: ToggleFullscreen(); break;
                case 1:
                    cfg.WindowScale = (cfg.WindowScale + d + 9) % 9;
                    if (!cfg.Fullscreen) ApplyWindowSize();
                    break;
                case 2: cfg.IntegerScale = !cfg.IntegerScale; break;
                case 3: cfg.Smooth = !cfg.Smooth; MakeTexture(); break;
                case 4: cfg.Volume = Math.Max(0, Math.Min(100, cfg.Volume + d * 10)); break;
                case 5: CycleAudioDevice(d); break;
                case 6: break;
                case 7: { int i = (Array.IndexOf(Settings.LivesValues, cfg.Lives) + d + 4) % 4; cfg.Lives = Settings.LivesValues[i]; cfg.ApplyTo(machine); break; }
                case 8: { int i = (Array.IndexOf(Settings.BonusValues, cfg.Bonus) + d + 5) % 5; cfg.Bonus = Settings.BonusValues[i]; cfg.ApplyTo(machine); break; }
                case 9: cfg.Rank = (cfg.Rank + d + 4) % 4; cfg.ApplyTo(machine); break;
                case 10: cfg.AutoCoin = !cfg.AutoCoin; machine.Chip51.AutoCoin = cfg.AutoCoin; break;
                case 11: cfg.AutoPump = !cfg.AutoPump; break;
                case 12: cfg.Widescreen = !cfg.Widescreen; machine.Video.WantPfOnly = cfg.Widescreen; break;
            }
        }

        void ToggleFullscreen()
        {
            cfg.Fullscreen = !cfg.Fullscreen;
            int fr = Sdl.SDL_SetWindowFullscreen(win, cfg.Fullscreen ? Sdl.WindowFullscreenDesktop : 0);
            Log("fullscreen -> " + cfg.Fullscreen + " result " + fr + " " + Sdl.Error());
            Sdl.SDL_ShowCursor(cfg.Fullscreen ? 0 : 1);
            if (!cfg.Fullscreen) ApplyWindowSize();
            cfg.Save();
        }

        void ApplyWindowSize()
        {
            int scale = cfg.WindowScale;
            if (scale == 0)
            {
                SdlRect ub;
                scale = Sdl.SDL_GetDisplayUsableBounds(0, out ub) == 0 ? Math.Max(2, (int)(ub.H * 0.86) / H) : 3;
            }
            Sdl.SDL_SetWindowSize(win, W * scale, H * scale);
            Sdl.SDL_SetWindowPosition(win, Sdl.WindowPosCentered, Sdl.WindowPosCentered);
        }

        string[] ScreenLines(out int firstItem)
        {
            firstItem = 0;
            if (screen == Screen.Main)
                return new[] { menuPausesGame ? "RESUME" : "PLAY", "OPTIONS", "CONTROLS", "RESET GAME", "QUIT" };
            if (screen == Screen.Options)
            {
                string scale = cfg.WindowScale == 0 ? "AUTO" : cfg.WindowScale + "X";
                return new[]
                {
                    "FULLSCREEN " + (cfg.Fullscreen ? "ON" : "OFF"), "WINDOW SIZE " + scale,
                    "SCALING " + (cfg.IntegerScale ? "SHARP" : "FIT"), "FILTER " + (cfg.Smooth ? "SMOOTH" : "PIXELS"),
                    "VOLUME " + cfg.Volume, "AUDIO " + AudioLabel(), "TEST SOUND", "LIVES " + cfg.Lives,
                    "BONUS " + Settings.BonusNames[Array.IndexOf(Settings.BonusValues, cfg.Bonus)],
                    "RANK " + (char)('A' + cfg.Rank), "AUTO COIN " + (cfg.AutoCoin ? "ON" : "OFF"),
                    "AUTO PUMP " + (cfg.AutoPump ? "ON" : "OFF"), "WIDESCREEN " + (cfg.Widescreen ? "ON" : "OFF"), "BACK"
                };
            }
            return new string[0];
        }

        void DrawMenu()
        {
            Text("DIG DUG", 84, 24, unchecked((int)0xffffd800));
            if (screen == Screen.Controls)
            {
                string[] c =
                {
                    "KEYBOARD", "COIN 5 OR C", "START 1 OR ENTER", "MOVE ARROWS OR WASD", "PUMP SPACE OR CTRL", "PAUSE P", "FAST FORWARD TAB",
                    "FULLSCREEN F11", "MENU ESC", "", "CONTROLLER", "XBOX PS4 PS5 SWITCH AND", "MOST OTHER PADS WORK", "MOVE DPAD OR STICK", "PUMP A B X Y OR TRIGGER",
                    "COIN SELECT OR SHARE", "START START OR OPTIONS", "MENU PS OR GUIDE OR R3"
                };
                for (int i = 0; i < c.Length; i++) Text(c[i], 8, 56 + i * 12, i == 0 || i == 10 ? unchecked((int)0xffffd800) : unchecked((int)0xffe0e0ff));
                Text("PRESS ENTER TO GO BACK", 8, 272, unchecked((int)0xff80ff80));
                return;
            }
            int first; string[] lines = ScreenLines(out first);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                bool on = i == sel;
                int col = on ? unchecked((int)0xffffff40) : unchecked((int)0xffe0e0ff);
                bool opt = screen == Screen.Options && i < OptionCount - 1;
                int x = opt ? 16 : (W - lines[i].Length * 8) / 2;
                int y = screen == Screen.Options ? 46 + i * 15 : 72 + i * 18;
                Text(lines[i], x, y, col);
                if (on) Marker(opt ? 4 : x - 12, y, col);
            }
            string hint = screen == Screen.Main ? "ARROWS AND ENTER OR PAD" : "LEFT RIGHT TO CHANGE";
            Text(hint, (W - hint.Length * 8) / 2, 268, unchecked((int)0xff80ff80));
        }

        void Marker(int x, int y, int color)
        {
            for (int row = 0; row < 7; row++)
            {
                int w = 4 - Math.Abs(row - 3);
                for (int c = 0; c < w; c++) { int ox = x + c, oy = y + row; if (ox >= 0 && ox < W && oy >= 0 && oy < H) frameBuf[oy * W + ox] = color; }
            }
        }

        // Text is drawn with the game's own character ROM (rotated glyphs: native (gx,gy) -> screen (7-gy, gx)).
        static int GlyphCode(char c)
        {
            if (c >= '0' && c <= '9') return 0x10 + (c - '0');
            if (c >= 'A' && c <= 'Z') return 0x1a + (c - 'A');
            if (c == '.') return 0x34;
            return -1;
        }

        void Text(string s, int x, int y, int color)
        {
            var glyphs = machine.Video.CharPix;
            for (int ci = 0; ci < s.Length; ci++)
            {
                int g = GlyphCode(char.ToUpperInvariant(s[ci]));
                if (g < 0) continue;
                int px = x + ci * 8;
                for (int gy = 0; gy < 8; gy++)
                    for (int gx = 0; gx < 8; gx++)
                        if (glyphs[g * 64 + gy * 8 + gx] != 0)
                        {
                            int ox = px + (7 - gy), oy = y + gx;
                            if (ox >= 0 && ox < W && oy >= 0 && oy < H) frameBuf[oy * W + ox] = color;
                        }
            }
        }
    }
}
