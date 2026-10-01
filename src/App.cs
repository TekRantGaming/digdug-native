// SDL2 front end - core: startup, main loop, audio device, window options.
// The rest of the front end lives in App.Input.cs, App.Video.cs, App.Menu.cs, App.Features.cs and App.Rom.cs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DigDug
{
    public sealed unsafe partial class App
    {
        const long BootFrames = 1900;          // power-on self-test frames that are fast-forwarded
        const double FrameTime = 1.0 / 60.6061;
        const int W = Video.OutW, H = Video.OutH;

        readonly Settings cfg;
        readonly Stats stats = Stats.Load();
        Machine machine;
        IntPtr win, ren, tex, ev;
        uint audio;
        readonly Stopwatch sw = Stopwatch.StartNew();
        bool running = true, paused, menuOpen, menuPausesGame, focusLost, advanceOne;

        public readonly List<string[]> Script = new List<string[]>();
        public readonly List<string> Overrides = new List<string>();

        short[] abuf = new short[1600];
        double sampleAcc;

        public App(Settings s)
        {
            cfg = s;
            try { File.WriteAllText(Path.Combine(Settings.ConfigDir, "digdug.log"), "Dig Dug log " + DateTime.Now + "\n"); } catch { }
        }

        static void Log(string s) { try { File.AppendAllText(Path.Combine(Settings.ConfigDir, "digdug.log"), s + "\n"); } catch { } }

        // ================================================================== startup
        public int Run(RomSet roms, Dictionary<string, string> opts)
        {
            foreach (var o in Overrides) cfg.Override(o);
            Sdl.SDL_SetHint("SDL_WINDOWS_DPI_AWARENESS", "permonitorv2");
            Sdl.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (Sdl.SDL_Init(Sdl.InitVideo | Sdl.InitAudio | Sdl.InitGameController | Sdl.InitEvents) != 0)
            {
                Console.Error.WriteLine("SDL_Init failed: " + Sdl.Error());
                return 1;
            }
            ev = Marshal.AllocHGlobal(64);
            foreach (var line in Program.StartupLog) Log(line);
            ConfigureShot(opts);

            int sw0 = W * 3, sh0 = H * 3;
            SdlRect ub;
            if (Sdl.SDL_GetDisplayUsableBounds(0, out ub) == 0)
            {
                int scale = cfg.WindowScale > 0 ? cfg.WindowScale : Math.Max(2, (int)(ub.H * 0.86) / H);
                sw0 = W * scale; sh0 = H * scale;
            }
            string v;
            if (opts != null && opts.TryGetValue("size", out v)) { var wh = v.ToLowerInvariant().Split('x'); int a, b; if (wh.Length == 2 && int.TryParse(wh[0], out a) && int.TryParse(wh[1], out b)) { sw0 = a; sh0 = b; } }
            win = Sdl.SDL_CreateWindow("Dig Dug", Sdl.WindowPosCentered, Sdl.WindowPosCentered, sw0, sh0, Sdl.WindowResizable | (cfg.Fullscreen ? Sdl.WindowFullscreenDesktop : 0) | (shotFile != null ? 8u : 0u));   // 8 = hidden: test shots never pop up a window
            if (win == IntPtr.Zero) { Console.Error.WriteLine("Window failed: " + Sdl.Error()); return 1; }
            ren = Sdl.SDL_CreateRenderer(win, -1, Sdl.RendererAccelerated | (cfg.VSync ? Sdl.RendererPresentVSync : 0));
            if (ren == IntPtr.Zero) ren = Sdl.SDL_CreateRenderer(win, -1, 0);
            if (ren == IntPtr.Zero) { Console.Error.WriteLine("Renderer failed: " + Sdl.Error()); return 1; }
            MakeTexture();
            ApplyWindowFlags();
            Sdl.SDL_EventState(Sdl.EvDropFile, 1);

            if (shotFile == null) OpenAudio();      // test shots run silently
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

            if (machine != null && !Settings.NoSave) machine.SaveEarom(Settings.NvPath);
            cfg.Save(); stats.Save();
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
            if (opts != null) Program.ApplyDipOptions(machine, opts);
            menuOpen = true; menuPausesGame = false;
            OpenMainMenu();
            if (shotMenu == "off") menuOpen = false;
            else if (shotMenu != null && shotMenu != "main") OpenMenuPage(shotMenu);
            Sdl.SDL_SetWindowTitle(win, "Dig Dug");
        }

        public void ApplyWindowFlags()
        {
            Sdl.SDL_SetWindowBordered(win, cfg.Borderless && !cfg.Fullscreen ? 0 : 1);
            Sdl.SDL_SetWindowAlwaysOnTop(win, cfg.AlwaysOnTop ? 1 : 0);
            Sdl.SDL_ShowCursor(cfg.Fullscreen ? 0 : 1);
            Sdl.SDL_RenderSetVSync(ren, cfg.VSync ? 1 : 0);
        }

        void ToggleFullscreen()
        {
            cfg.Fullscreen = !cfg.Fullscreen;
            Sdl.SDL_SetWindowFullscreen(win, cfg.Fullscreen ? Sdl.WindowFullscreenDesktop : 0);
            ApplyWindowFlags();
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
            bool rot = (cfg.Rotation & 1) != 0;
            Sdl.SDL_SetWindowSize(win, (rot ? H : W) * scale, (rot ? W : H) * scale);
            Sdl.SDL_SetWindowPosition(win, Sdl.WindowPosCentered, Sdl.WindowPosCentered);
        }

        void MakeTexture()
        {
            if (tex != IntPtr.Zero) Sdl.SDL_DestroyTexture(tex);
            Sdl.SDL_SetHint("SDL_RENDER_SCALE_QUALITY", cfg.Smooth ? "linear" : "nearest");
            tex = Sdl.SDL_CreateTexture(ren, Sdl.PixelFormatArgb8888, Sdl.TextureAccessStreaming, texW, H);
            overlayKey = null;
        }

        // ================================================================== audio
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
            Log("Audio devices: " + string.Join(" | ", AudioDevices().ToArray()));
            Log("Audio driver " + Sdl.AudioDriver() + "; opened: " + got.Freq + " Hz, format " + got.Format.ToString("x") + ", " + got.Channels + " ch");
            Sdl.SDL_PauseAudioDevice(audio, 0);
        }

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
            string s = ((i >= 0 ? (i + 1) + " " : "") + ArcadeText.Clean(cfg.AudioDevice)).Trim();
            return s.Length > 11 ? s.Substring(0, 11) : s;
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

        void PushAudio(double frameTime)
        {
            if (audio == 0) return;
            uint queued = Sdl.SDL_GetQueuedAudioSize(audio) / 2;
            if (queued > 6 * 800) return;                       // running ahead: drop this frame's audio
            sampleAcc += Sound.SampleRate * frameTime;
            int n = (int)sampleAcc; sampleAcc -= n;
            if (queued < 400) n += 400;                         // refill after an underrun
            if (n > abuf.Length) n = abuf.Length;
            machine.Sound.Mix(abuf, 0, n);
            int vol = (focusLost && cfg.MuteInBackground) ? 0 : cfg.Volume;
            if (vol < 100) for (int i = 0; i < n; i++) abuf[i] = (short)(abuf[i] * vol / 100);
            fixed (short* p = abuf) Sdl.SDL_QueueAudio(audio, (IntPtr)p, (uint)(n * 2));
        }

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
                bool fastKey = KeyDown(ScTab);
                bool frozen = paused || (menuOpen && menuPausesGame) || (focusLost && cfg.PauseOnFocusLoss);
                bool rewinding = KeyDown(ScR) && !menuOpen && !frozen && CanRewind();
                double now = sw.Elapsed.TotalSeconds;

                if ((booting || fastKey) && !frozen && !rewinding)
                {
                    double until = now + 0.012;
                    while (sw.Elapsed.TotalSeconds < until && (machine.FrameCount < BootFrames || KeyDown(ScTab)))
                        StepCore();
                    if (booting) RenderBlank(); else { machine.Video.Render(); Present(); }
                    if (audio != 0) Sdl.SDL_ClearQueuedAudio(audio);
                    next = sw.Elapsed.TotalSeconds;
                    continue;
                }

                double ft = FrameTime * 100.0 / Math.Max(25, cfg.GameSpeed);
                if (now < next) { Sdl.SDL_Delay(1); continue; }
                next += ft;
                if (now - next > 0.1) next = now;

                if (rewinding) { RewindStep(); if (audio != 0) Sdl.SDL_ClearQueuedAudio(audio); }
                else if (!frozen || advanceOne)
                {
                    advanceOne = false;
                    StepCore();
                    if (cfg.GameSpeed == 100) PushAudio(FrameTime); else if (audio != 0) Sdl.SDL_ClearQueuedAudio(audio);
                }
                else if (audio != 0) Sdl.SDL_ClearQueuedAudio(audio);

                machine.Video.Render();
                Present();
                if (shotFrame > 0 && machine.FrameCount >= shotFrame)
                {
                    TakeScreenshot(shotFile, shotW, shotH);
                    Log("saved frame " + machine.FrameCount + " to " + shotFile);
                    shotFrame = 0; if (shotQuit) running = false;
                }
            }
        }

        // One emulated frame: input, CPU/video/sound, and everything that watches the game (rewind buffer, statistics, rumble).
        void StepCore()
        {
            InjectPadEvents();
            if (replayPlaying) ApplyReplayFrame(); else PollInput();
            if (recording) RecordFrame();
            machine.RunFrame();
            OnFrameDone();
        }

        // ---- automated test hooks (--shot-at, --shot-file, --shot-size, --shot-menu, --shot-quit)
        long shotFrame; string shotFile; bool shotQuit; int shotW, shotH; string shotMenu;
        void ConfigureShot(Dictionary<string, string> o)
        {
            string v;
            if (o == null) return;
            if (o.TryGetValue("shot-at", out v)) long.TryParse(v, out shotFrame);
            if (o.TryGetValue("shot-file", out v)) shotFile = v;
            shotQuit = o.ContainsKey("shot-quit");
            if (o.TryGetValue("shot-size", out v)) { var wh = v.ToLowerInvariant().Split('x'); if (wh.Length == 2) { int.TryParse(wh[0], out shotW); int.TryParse(wh[1], out shotH); } }
            if (o.TryGetValue("shot-menu", out v)) shotMenu = v;
            if (shotFile == null) shotFrame = 0;
        }
    }
}
