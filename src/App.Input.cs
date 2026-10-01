// Front end - input: keyboard (rebindable), game controllers (event-tracked), hotkeys, rumble.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DigDug
{
    public sealed unsafe partial class App
    {
        // SDL scancodes used by fixed (non-rebindable) keys
        const int ScA = 4, ScD = 7, ScF = 9, ScP = 19, ScR = 21, ScS = 22, ScW = 26, ScZ = 29,
            ScReturn = 40, ScEscape = 41, ScBackspace = 42, ScTab = 43, ScSpace = 44, ScLBracket = 47, ScRBracket = 48, ScBackslash = 49, ScPeriod = 55,
            ScF1 = 58, ScF2 = 59, ScF3 = 60, ScF5 = 62, ScF6 = 63, ScF7 = 64, ScF8 = 65, ScF9 = 66, ScF10 = 67, ScF11 = 68, ScF12 = 69,
            ScRight = 79, ScLeft = 80, ScDown = 81, ScUp = 82, ScKpEnter = 88;

        byte* keys;
        // Controller state is tracked from SDL's button/axis *events* (the same path the menu uses), not by polling.
        sealed class Pad { public IntPtr Handle; public int Id; public readonly bool[] Btn = new bool[16]; public readonly short[] Axis = new short[6]; }
        readonly List<Pad> pads = new List<Pad>();

        readonly bool[] prevDir = new bool[4];
        int fireHeldFrames;
        int lastDir = -1;
        bool padRewind;

        // key-rebinding capture
        int capAct = -1, capSlot;

        bool KeyDown(int sc)
        {
            if (keys == null) { int n; keys = (byte*)Sdl.SDL_GetKeyboardState(out n); }
            return sc > 0 && keys[sc] != 0;
        }

        bool Held(Settings.Act a)
        {
            var k = cfg.Keys[(int)a];
            return KeyDown(k[0]) || KeyDown(k[1]);
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
            Toast("CONTROLLER CONNECTED");
        }

        Pad FindPad(int id) { foreach (var p in pads) if (p.Id == id) return p; return null; }

        void Rumble(int power, uint ms)
        {
            if (cfg.Rumble == 0 || pads.Count == 0) return;
            double scale = cfg.Rumble == 1 ? 0.35 : cfg.Rumble == 2 ? 0.6 : 1.0;
            ushort v = (ushort)Math.Min(65535, 655.35 * power * scale);
            foreach (var p in pads) Sdl.SDL_GameControllerRumble(p.Handle, v, v, ms);
        }

        bool PadFireDown(Pad p)
        {
            bool trig = p.Axis[Sdl.AxisTriggerRight] > 8000 || p.Axis[Sdl.AxisTriggerLeft] > 8000 || p.Btn[Sdl.BtnRShoulder];
            switch (cfg.PadFire)
            {
                case 1: return p.Btn[Sdl.BtnA];
                case 2: return p.Btn[Sdl.BtnB];
                case 3: return p.Btn[Sdl.BtnX];
                case 4: return p.Btn[Sdl.BtnY];
                case 5: return trig;
                default: return p.Btn[Sdl.BtnA] || p.Btn[Sdl.BtnB] || p.Btn[Sdl.BtnX] || p.Btn[Sdl.BtnY] || trig;
            }
        }

        void PollInput()
        {
            var i = machine.Input;
            bool coin = Held(Settings.Act.Coin), start1 = Held(Settings.Act.Start1), start2 = Held(Settings.Act.Start2);
            bool fire = Held(Settings.Act.Fire);
            var d = new bool[4];
            d[0] = Held(Settings.Act.Up); d[1] = Held(Settings.Act.Right); d[2] = Held(Settings.Act.Down); d[3] = Held(Settings.Act.Left);
            padRewind = false;
            int dz = 32767 * cfg.Deadzone / 100;

            foreach (var p in pads)
            {
                if (p.Btn[Sdl.BtnBack]) coin = true;
                if (p.Btn[Sdl.BtnStart]) start1 = true;
                if (p.Btn[Sdl.BtnLShoulder]) start2 = true;
                if (PadFireDown(p)) fire = true;
                if (p.Btn[Sdl.BtnLStick]) padRewind = true;
                if (p.Btn[Sdl.BtnUp]) d[0] = true;
                if (p.Btn[Sdl.BtnRight]) d[1] = true;
                if (p.Btn[Sdl.BtnDown]) d[2] = true;
                if (p.Btn[Sdl.BtnLeft]) d[3] = true;
                int ax = p.Axis[Sdl.AxisLX], ay = p.Axis[Sdl.AxisLY];
                if (Math.Max(Math.Abs(ax), Math.Abs(ay)) > dz)
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

            if (Script.Count > 0 && Script.Exists(s => !s[1].StartsWith("pad") && !s[1].StartsWith("key")))   // automated test input: --at frame:key:duration
            {
                long f = machine.FrameCount + 1;
                coin = start1 = fire = false; lastDir = -1;
                foreach (var e in Script)
                {
                    int start = int.Parse(e[0]), dur = e.Length > 2 ? int.Parse(e[2]) : 5;
                    if (f < start || f >= start + dur) continue;
                    switch (e[1])
                    {
                        case "coin1": coin = true; break; case "start1": start1 = true; break; case "fire": fire = true; break;
                        case "up": lastDir = 0; break; case "right": lastDir = 1; break; case "down": lastDir = 2; break; case "left": lastDir = 3; break;
                    }
                }
                i.Coin1 = coin; i.Start1 = start1; i.Fire = fire; i.Dir = lastDir < 0 ? -1 : lastDir * 2; i.Coin2 = i.Start2 = false;
                return;
            }

            i.Coin1 = coin; i.Coin2 = false; i.Start1 = start1; i.Start2 = start2; i.Fire = fire;
            i.Service = KeyDown(ScF2);
            i.Dir = (menuOpen ? -1 : lastDir) < 0 ? -1 : lastDir * 2;
            if (menuOpen) { i.Fire = false; i.Start1 = false; i.Start2 = false; i.Coin1 = false; }
        }

        bool CanRewindKey() { return KeyDown(ScR) || padRewind; }

        // ================================================================== events
        void PumpEvents()
        {
            while (Sdl.SDL_PollEvent(ev) != 0)
            {
                uint type = (uint)Marshal.ReadInt32(ev, 0);
                switch (type)
                {
                    case Sdl.EvQuit: running = false; break;
                    case Sdl.EvMouseButtonDown: if (machine == null) BrowseForRom(); break;
                    case Sdl.EvWindow:
                        {
                            int id = Marshal.ReadByte(ev, 12);
                            if (id == Sdl.EvWindowFocusLost) focusLost = true;
                            else if (id == Sdl.EvWindowFocusGained) focusLost = false;
                            break;
                        }
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
                                if (pads[i].Id == id) { Sdl.SDL_GameControllerClose(pads[i].Handle); pads.RemoveAt(i); Log("Controller removed (id " + id + ")"); Toast("CONTROLLER REMOVED"); }
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

        void OnKey(int sc)
        {
            if (machine == null) { if (sc == ScReturn || sc == ScKpEnter) BrowseForRom(); return; }

            if (capAct >= 0)    // waiting for a key to bind
            {
                if (sc != ScEscape) cfg.Keys[capAct][capSlot] = sc == ScBackspace ? 0 : sc;
                capAct = -1; cfg.Save(); RefreshMenu();
                return;
            }
            if (sc == ScF11 || sc == ScF12) { if (sc == ScF11) ToggleFullscreen(); else TakeScreenshotToFile(); return; }

            if (menuOpen)
            {
                switch (sc)
                {
                    case ScUp: case ScW: MenuMove(-1); break;
                    case ScDown: case ScS: MenuMove(1); break;
                    case ScLeft: case ScA: MenuAdjust(-1); break;
                    case ScRight: case ScD: MenuAdjust(1); break;
                    case ScReturn: case ScKpEnter: case ScSpace: MenuActivate(); break;
                    case ScEscape: case ScF1: MenuBack(); break;
                }
                return;
            }
            switch (sc)
            {
                case ScEscape: case ScF1: OpenMenu(); break;
                case ScP: paused = !paused; Toast(paused ? "PAUSED" : "RESUMED"); break;
                case ScPeriod: if (paused) advanceOne = true; break;
                case ScF3: machine.Reset(); cfg.ApplyTo(machine); ClearRewind(); break;
                case ScF5: SaveSlot(stateSlot); break;
                case ScF7: LoadSlot(stateSlot); break;
                case ScF6: stateSlot = stateSlot % StateSlots + 1; Toast("STATE SLOT " + stateSlot); break;
                case ScF8: cfg.Scanlines = (cfg.Scanlines + 1) % 4; Toast("SCANLINES " + new[] { "OFF", "LIGHT", "MEDIUM", "HEAVY" }[cfg.Scanlines]); break;
                case ScF9: cfg.ShowFps = !cfg.ShowFps; break;
                case ScF10: cfg.Theme = (cfg.Theme + 1) % Themes.Count; machine.Video.SetTheme(cfg.Theme); Unlock(Achievements.Stylish); Toast("THEME " + Themes.Names[cfg.Theme]); break;
                case ScLBracket: SetSpeed(cfg.GameSpeed - 25); break;
                case ScRBracket: SetSpeed(cfg.GameSpeed + 25); break;
                case ScBackslash: SetSpeed(100); break;
            }
        }

        void SetSpeed(int pct) { cfg.GameSpeed = Math.Max(25, Math.Min(400, pct)); Toast("SPEED " + cfg.GameSpeed + "%"); }

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

        // Test hook: script entries named "padN" (N = SDL controller button number) are injected as real SDL controller
        // events, so the event -> pad state -> game input path can be tested without touching hardware.
        void InjectPadEvents()
        {
            if (Script.Count == 0) return;
            long f = machine.FrameCount;
            foreach (var e in Script)
            {
                // "key<scancode>" presses a hotkey (test hook), e.g. --at 3000:key62:1 = F5
                if (e[1].StartsWith("key")) { if (f == int.Parse(e[0])) OnKey(int.Parse(e[1].Substring(3))); continue; }
                if (pads.Count == 0 || !e[1].StartsWith("pad") || e[1].StartsWith("padaxis")) continue;
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
    }
}
