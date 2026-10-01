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
        readonly List<IntPtr> pads = new List<IntPtr>();
        readonly Stopwatch sw = Stopwatch.StartNew();
        bool running = true, paused, menuOpen, menuPausesGame;

        // input state
        readonly bool[] prevDir = new bool[4];
        int lastDir = -1;

        // menu
        enum Screen { Main, Options, Controls }
        Screen screen = Screen.Main;
        int sel;
        readonly int[] frameBuf = new int[W * H];
        short[] abuf = new short[1600];
        double sampleAcc;

        public App(Settings s) { cfg = s; try { File.WriteAllText(Path.Combine(Settings.ConfigDir, "digdug.log"), "Dig Dug log " + DateTime.Now + "\n"); } catch { } }

        static void Log(string s) { try { File.AppendAllText(Path.Combine(Settings.ConfigDir, "digdug.log"), s + "\n"); } catch { } }
        double lastLog;

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

            OpenAudio();
            for (int i = 0; i < Sdl.SDL_NumJoysticks(); i++) OpenPad(i);

            if (roms != null) StartMachine(roms, opts);
            else
            {
                string msg = "Dig Dug needs its original ROM files, which are not included with this program.\n\n" +
                    "Drag your digdug.zip (or the folder containing the ROM files) onto the game window,\n" +
                    "or place it in:\n  " + Path.Combine(Settings.ConfigDir, "roms") + "\n\n" +
                    "You can also start the program as:  DigDug <path-to-digdug.zip>";
                Sdl.SDL_ShowSimpleMessageBox(0x20, "Dig Dug - ROMs required", msg, win);
                Sdl.SDL_SetWindowTitle(win, "Dig Dug - drop your digdug.zip onto this window");
            }

            MainLoop();

            if (machine != null) machine.SaveEarom(Settings.NvPath);
            cfg.Save();
            foreach (var p in pads) Sdl.SDL_GameControllerClose(p);
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
            if (opts != null) Program.ApplyDipOptions(machine, opts);
            menuOpen = true; menuPausesGame = false; screen = Screen.Main; sel = 0;
            Sdl.SDL_SetWindowTitle(win, "Dig Dug");
        }

        void MakeTexture()
        {
            if (tex != IntPtr.Zero) Sdl.SDL_DestroyTexture(tex);
            Sdl.SDL_SetHint("SDL_RENDER_SCALE_QUALITY", cfg.Smooth ? "linear" : "nearest");
            tex = Sdl.SDL_CreateTexture(ren, Sdl.PixelFormatArgb8888, Sdl.TextureAccessStreaming, W, H);
        }

        void OpenAudio()
        {
            var want = new SdlAudioSpec { Freq = Sound.SampleRate, Format = Sdl.AudioS16, Channels = 1, Samples = 1024 };
            SdlAudioSpec got;
            audio = Sdl.SDL_OpenAudioDevice(IntPtr.Zero, 0, ref want, out got, 0);
            if (audio == 0) { Log("Audio unavailable: " + Sdl.Error()); return; }
            Log("Audio opened: " + got.Freq + " Hz, format " + got.Format.ToString("x") + ", " + got.Channels + " ch");
            Sdl.SDL_PauseAudioDevice(audio, 0);
        }

        void OpenPad(int index)
        {
            if (Sdl.SDL_IsGameController(index) == 0) return;
            IntPtr p = Sdl.SDL_GameControllerOpen(index);
            if (p == IntPtr.Zero) return;
            int id = Sdl.InstanceId(p);
            foreach (var q in pads) if (Sdl.InstanceId(q) == id) { return; }
            pads.Add(p);
            Log("Controller connected: " + Sdl.ControllerName(p));
        }

        // ================================================================== main loop
        void MainLoop()
        {
            double next = 0;
            while (running)
            {
                PumpEvents();
                if (machine == null) { RenderBlank(); Sdl.SDL_Delay(30); continue; }

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
                    PollInput();
                    machine.RunFrame();
                    PushAudio();
                }
                else if (audio != 0) Sdl.SDL_ClearQueuedAudio(audio);
                machine.Video.Render();
                Present();
            }
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
            int vol = cfg.Volume;
            if (vol < 100) for (int i = 0; i < n; i++) abuf[i] = (short)(abuf[i] * vol / 100);
            fixed (short* p = abuf) Sdl.SDL_QueueAudio(audio, (IntPtr)p, (uint)(n * 2));
            double tn = sw.Elapsed.TotalSeconds;
            if (tn - lastLog > 5) { lastLog = tn; Log("t=" + tn.ToString("0") + "s audio queued=" + (Sdl.SDL_GetQueuedAudioSize(audio) / 2) + " samples"); }
        }

        // ================================================================== rendering
        void RenderBlank()
        {
            Sdl.SDL_SetRenderDrawColor(ren, 0, 0, 0, 255);
            Sdl.SDL_RenderClear(ren);
            Sdl.SDL_RenderPresent(ren);
        }

        void Present()
        {
            int[] src = machine.Video.Pixels;
            if (menuOpen)
            {
                for (int i = 0; i < src.Length; i++) { int c = src[i]; frameBuf[i] = (int)(0xff000000 | (uint)((c >> 2) & 0x3f3f3f)); }
                DrawMenu();
                src = frameBuf;
            }
            fixed (int* p = src) Sdl.SDL_UpdateTexture(tex, IntPtr.Zero, (IntPtr)p, W * 4);

            int ow, oh; Sdl.SDL_GetRendererOutputSize(ren, out ow, out oh);
            SdlRect dst;
            if (cfg.IntegerScale && ow >= W && oh >= H)
            {
                int s = Math.Max(1, Math.Min(ow / W, oh / H));
                dst = new SdlRect { W = W * s, H = H * s };
            }
            else
            {
                double s = Math.Min((double)ow / W, (double)oh / H);
                dst = new SdlRect { W = (int)(W * s), H = (int)(H * s) };
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
                if (Btn(p, Sdl.BtnBack)) coin = true;
                if (Btn(p, Sdl.BtnStart)) start1 = true;
                if (Btn(p, Sdl.BtnLShoulder)) start2 = true;
                if (Btn(p, Sdl.BtnA) || Btn(p, Sdl.BtnB) || Btn(p, Sdl.BtnX) || Btn(p, Sdl.BtnY) || Btn(p, Sdl.BtnRShoulder)
                    || Sdl.SDL_GameControllerGetAxis(p, Sdl.AxisTriggerRight) > 8000 || Sdl.SDL_GameControllerGetAxis(p, Sdl.AxisTriggerLeft) > 8000) fire = true;
                if (Btn(p, Sdl.BtnUp)) d[0] = true; if (Btn(p, Sdl.BtnRight)) d[1] = true;
                if (Btn(p, Sdl.BtnDown)) d[2] = true; if (Btn(p, Sdl.BtnLeft)) d[3] = true;
                int ax = Sdl.SDL_GameControllerGetAxis(p, Sdl.AxisLX), ay = Sdl.SDL_GameControllerGetAxis(p, Sdl.AxisLY);
                if (Math.Max(Math.Abs(ax), Math.Abs(ay)) > 14000)
                {
                    if (Math.Abs(ax) >= Math.Abs(ay)) { if (ax > 0) d[1] = true; else d[3] = true; }
                    else { if (ay < 0) d[0] = true; else d[2] = true; }
                }
            }

            // 4-way stick: the most recently pressed direction wins
            for (int k = 0; k < 4; k++) if (d[k] && !prevDir[k]) lastDir = k;
            if (lastDir >= 0 && !d[lastDir]) { lastDir = -1; for (int k = 0; k < 4; k++) if (d[k]) { lastDir = k; break; } }
            for (int k = 0; k < 4; k++) prevDir[k] = d[k];

            i.Coin1 = coin; i.Coin2 = KeyDown(Sc6); i.Start1 = start1; i.Start2 = start2; i.Fire = fire;
            i.Service = KeyDown(ScF2);
            i.Dir = (menuOpen ? -1 : lastDir) < 0 ? -1 : lastDir * 2;
            if (menuOpen) { i.Fire = false; i.Start1 = false; i.Start2 = false; i.Coin1 = false; }
        }

        static bool Btn(IntPtr p, int b) { return Sdl.SDL_GameControllerGetButton(p, b) != 0; }

        void PumpEvents()
        {
            while (Sdl.SDL_PollEvent(ev) != 0)
            {
                uint type = (uint)Marshal.ReadInt32(ev, 0);
                switch (type)
                {
                    case Sdl.EvQuit: running = false; break;
                    case Sdl.EvKeyDown:
                        if (Marshal.ReadByte(ev, 13) == 0) OnKey(Marshal.ReadInt32(ev, 16));
                        break;
                    case Sdl.EvControllerButtonDown: OnPadButton(Marshal.ReadByte(ev, 12)); break;
                    case Sdl.EvControllerDeviceAdded: OpenPad(Marshal.ReadInt32(ev, 8)); break;
                    case Sdl.EvControllerDeviceRemoved:
                        {
                            int id = Marshal.ReadInt32(ev, 8);
                            for (int i = pads.Count - 1; i >= 0; i--)
                                if (Sdl.InstanceId(pads[i]) == id) { Sdl.SDL_GameControllerClose(pads[i]); pads.RemoveAt(i); }
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

        void OnDrop(string path)
        {
            if (machine != null || string.IsNullOrEmpty(path)) return;
            try
            {
                var roms = RomSet.Load(path);
                cfg.RomPath = path; cfg.Save();
                StartMachine(roms, null);
            }
            catch (Exception ex)
            {
                Sdl.SDL_ShowSimpleMessageBox(0x10, "Dig Dug", "That doesn't look like a complete Dig Dug ROM set:\n\n" + ex.Message, win);
            }
        }

        void OnKey(int sc)
        {
            Log("key " + sc);
            if (machine == null) return;
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
            if (machine == null) return;
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

        int ItemCount { get { return screen == Screen.Main ? 5 : screen == Screen.Options ? 10 : 1; } }
        void MenuMove(int d)
        {
            int first;
            var lines = ScreenLines(out first);
            for (int tries = 0; tries < 12; tries++)
            {
                sel = (sel + d + ItemCount) % ItemCount;
                if (screen != Screen.Options || lines[sel].Length > 0) break;   // skip the blank spacer row
            }
        }

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
            else if (sel == 9) { screen = Screen.Main; sel = 1; cfg.Save(); }
            else MenuAdjust(1);
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
                case 5: { int i = (Array.IndexOf(Settings.LivesValues, cfg.Lives) + d + 4) % 4; cfg.Lives = Settings.LivesValues[i]; cfg.ApplyTo(machine); break; }
                case 6: { int i = (Array.IndexOf(Settings.BonusValues, cfg.Bonus) + d + 5) % 5; cfg.Bonus = Settings.BonusValues[i]; cfg.ApplyTo(machine); break; }
                case 7: cfg.Rank = (cfg.Rank + d + 4) % 4; cfg.ApplyTo(machine); break;
                case 8: break;
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
                    "VOLUME " + cfg.Volume, "LIVES " + cfg.Lives,
                    "BONUS " + Settings.BonusNames[Array.IndexOf(Settings.BonusValues, cfg.Bonus)],
                    "RANK " + (char)('A' + cfg.Rank), "", "BACK"
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
                int x = (W - lines[i].Length * 8) / 2;
                if (screen == Screen.Options && i < 9) x = 16;
                Text(lines[i], x, 72 + i * 18, col);
                if (on) Marker(screen == Screen.Options && i < 9 ? 4 : x - 12, 72 + i * 18, col);
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
