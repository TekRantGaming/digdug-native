// User settings, stored as simple key=value lines in the per-user config folder.
using System;
using System.Collections.Generic;
using System.IO;

namespace DigDug
{
    public enum ScaleMode { Sharp, Fit, Stretch }

    public sealed class Settings
    {
        // ---- display
        public bool Fullscreen;
        public int WindowScale;                 // 0 = automatic, otherwise 1..8
        public ScaleMode Scaling = ScaleMode.Sharp;
        public bool Smooth;                     // bilinear filtering
        public bool Widescreen = true;          // fill the side areas of wide windows/fullscreen
        public int SidePanels;                  // 0 dirt, 1 black, 2 glow, 3 sound scope, 4 info
        public int Scanlines;                   // 0 off, 1 light, 2 medium, 3 heavy
        public bool AuthenticScan;              // scanlines run along the arcade monitor's real scan direction
        public bool CrtMask;                    // RGB phosphor mask
        public bool Vignette;                   // darkened screen corners
        public int Theme;                       // colour theme (see Themes)
        public int Rotation;                    // 0, 1 = 90, 2 = 180, 3 = 270 degrees
        public bool VSync = true;
        public bool ShowFps;
        public bool AlwaysOnTop;
        public bool Borderless;

        // ---- audio
        public int Volume = 80;                 // 0..100
        public string AudioDevice = "";         // empty = system default
        public int MuteMask;                    // bit n = voice n silenced
        public bool SoundSmooth;                // soft low-pass filter
        public bool MuteInBackground = true;

        // ---- game
        public int Lives = 3;                   // 1, 2, 3, 5
        public int Bonus = 4;                   // value of DIP byte 0 bits 3-5
        public int Rank;                        // 0..3 = A..D
        public bool AutoCoin = true;
        public bool AutoPump = true;
        public int GameSpeed = 100;             // percent
        public bool PauseOnFocusLoss = true;

        // ---- cheats
        public bool CheatLives, CheatInvincible;
        public int CheatRound = 1;

        // ---- controls
        public enum Act { Up, Right, Down, Left, Fire, Coin, Start1, Start2 }
        public static readonly string[] ActNames = { "UP", "RIGHT", "DOWN", "LEFT", "FIRE", "COIN", "START 1", "START 2" };
        static readonly string[] ActKeys = { "up", "right", "down", "left", "fire", "coin", "start1", "start2" };
        public readonly int[][] Keys = new int[8][];
        public int PadFire;                     // 0 any face button, 1 A, 2 B, 3 X, 4 Y, 5 triggers/shoulders only
        public int Deadzone = 45;               // stick deadzone, percent
        public int Rumble = 2;                  // 0 off, 1 low, 2 medium, 3 strong

        public string RomPath = "";

        public static readonly int[] BonusValues = { 4, 2, 6, 1, 0 };
        public static readonly string[] BonusNames = { "10000 40000", "10000 50000", "20000 60000", "20000 70000", "NONE" };
        public static readonly int[] LivesValues = { 1, 2, 3, 5 };

        public Settings() { ResetKeys(); }

        public void ResetKeys()
        {
            Keys[(int)Act.Up] = new[] { 82, 26 };       // Up arrow, W
            Keys[(int)Act.Right] = new[] { 79, 7 };     // Right arrow, D
            Keys[(int)Act.Down] = new[] { 81, 22 };     // Down arrow, S
            Keys[(int)Act.Left] = new[] { 80, 4 };      // Left arrow, A
            Keys[(int)Act.Fire] = new[] { 44, 29 };     // Space, Z
            Keys[(int)Act.Coin] = new[] { 34, 6 };      // 5, C
            Keys[(int)Act.Start1] = new[] { 30, 40 };   // 1, Enter
            Keys[(int)Act.Start2] = new[] { 31, 0 };    // 2
        }

        public static string ConfigDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DigDugNative");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }
        public static string FilePath { get { return Path.Combine(ConfigDir, "settings.ini"); } }
        public static string NvPath { get { return Path.Combine(ConfigDir, "digdug.nv"); } }
        public static string SubDir(string name) { string d = Path.Combine(ConfigDir, name); try { Directory.CreateDirectory(d); } catch { } return d; }

        static int Clamp(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath)) s.ParseLine(line);
            }
            catch { }
            return s;
        }

        public void Override(string kv) { ParseLine(kv); }

        void ParseLine(string line)
        {
            var s = this;
            try
            {
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) return;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int n; int.TryParse(v, out n);
                    bool b = n != 0;
                    switch (k)
                    {
                        case "fullscreen": s.Fullscreen = b; break;
                        case "window_scale": s.WindowScale = Clamp(n, 0, 8); break;
                        case "integer_scale": s.Scaling = b ? ScaleMode.Sharp : ScaleMode.Fit; break;   // older versions
                        case "scaling": s.Scaling = (ScaleMode)Clamp(n, 0, 2); break;
                        case "smooth": s.Smooth = b; break;
                        case "widescreen": s.Widescreen = b; break;
                        case "side_panels": s.SidePanels = Clamp(n, 0, 4); break;
                        case "scanlines": s.Scanlines = Clamp(n, 0, 3); break;
                        case "authentic_scan": s.AuthenticScan = b; break;
                        case "crt_mask": s.CrtMask = b; break;
                        case "vignette": s.Vignette = b; break;
                        case "theme": s.Theme = Clamp(n, 0, Themes.Count - 1); break;
                        case "rotation": s.Rotation = Clamp(n, 0, 3); break;
                        case "vsync": s.VSync = b; break;
                        case "show_fps": s.ShowFps = b; break;
                        case "always_on_top": s.AlwaysOnTop = b; break;
                        case "borderless": s.Borderless = b; break;
                        case "volume": s.Volume = Clamp(n, 0, 100); break;
                        case "audio_device": s.AudioDevice = v; break;
                        case "mute_mask": s.MuteMask = Clamp(n, 0, 7); break;
                        case "sound_smooth": s.SoundSmooth = b; break;
                        case "mute_in_background": s.MuteInBackground = b; break;
                        case "lives": if (Array.IndexOf(LivesValues, n) >= 0) s.Lives = n; break;
                        case "bonus": if (Array.IndexOf(BonusValues, n) >= 0) s.Bonus = n; break;
                        case "rank": s.Rank = Clamp(n, 0, 3); break;
                        case "auto_coin": s.AutoCoin = b; break;
                        case "auto_pump": s.AutoPump = b; break;
                        case "game_speed": s.GameSpeed = Clamp(n, 25, 400); break;
                        case "pause_on_focus_loss": s.PauseOnFocusLoss = b; break;
                        case "cheat_lives": s.CheatLives = b; break;
                        case "cheat_invincible": s.CheatInvincible = b; break;
                        case "cheat_round": s.CheatRound = Clamp(n, 1, 30); break;
                        case "pad_fire": s.PadFire = Clamp(n, 0, 5); break;
                        case "deadzone": s.Deadzone = Clamp(n, 10, 80); break;
                        case "rumble": s.Rumble = Clamp(n, 0, 3); break;
                        case "rom_path": s.RomPath = v; break;
                        default:
                            if (k.StartsWith("key_"))
                            {
                                int a = Array.IndexOf(ActKeys, k.Substring(4));
                                var parts = v.Split(',');
                                int p0, p1 = 0; int.TryParse(parts[0], out p0); if (parts.Length > 1) int.TryParse(parts[1], out p1);
                                if (a >= 0) s.Keys[a] = new[] { p0, p1 };
                            }
                            break;
                    }
                }
            }
            catch { }

        }

        public static bool NoSave;

        public void Save()
        {
            if (NoSave) return;
            try
            {
                Func<bool, int> i = x => x ? 1 : 0;
                var l = new List<string>
                {
                    "fullscreen=" + i(Fullscreen), "window_scale=" + WindowScale, "scaling=" + (int)Scaling, "smooth=" + i(Smooth),
                    "widescreen=" + i(Widescreen), "side_panels=" + SidePanels, "scanlines=" + Scanlines, "authentic_scan=" + i(AuthenticScan),
                    "crt_mask=" + i(CrtMask), "vignette=" + i(Vignette), "theme=" + Theme, "rotation=" + Rotation, "vsync=" + i(VSync),
                    "show_fps=" + i(ShowFps), "always_on_top=" + i(AlwaysOnTop), "borderless=" + i(Borderless),
                    "volume=" + Volume, "audio_device=" + AudioDevice, "mute_mask=" + MuteMask, "sound_smooth=" + i(SoundSmooth),
                    "mute_in_background=" + i(MuteInBackground),
                    "lives=" + Lives, "bonus=" + Bonus, "rank=" + Rank, "auto_coin=" + i(AutoCoin), "auto_pump=" + i(AutoPump),
                    "game_speed=" + GameSpeed, "pause_on_focus_loss=" + i(PauseOnFocusLoss),
                    "cheat_lives=" + i(CheatLives), "cheat_invincible=" + i(CheatInvincible), "cheat_round=" + CheatRound,
                    "pad_fire=" + PadFire, "deadzone=" + Deadzone, "rumble=" + Rumble, "rom_path=" + RomPath
                };
                for (int a = 0; a < 8; a++) l.Add("key_" + ActKeys[a] + "=" + Keys[a][0] + "," + Keys[a][1]);
                File.WriteAllLines(FilePath, l.ToArray());
            }
            catch { }
        }

        // DIP bytes reported by the 53xx (coin B 1c/1cr, coin A 1c/1cr, upright, plus the user's choices) and the machine-side options.
        public void ApplyTo(Machine m)
        {
            int lv = Lives == 1 ? 0 : Lives == 2 ? 1 : Lives == 5 ? 3 : 2;
            m.Dip0 = (lv << 6) | ((Bonus & 7) << 3) | 1;
            int rankBits = Rank == 1 ? 0x02 : Rank == 2 ? 0x01 : Rank == 3 ? 0x03 : 0;
            m.Dip1 = 0x3c | rankBits;
            m.Chip51.AutoCoin = AutoCoin;
            m.Cheats.InfiniteLives = CheatLives; m.Cheats.Invincible = CheatInvincible; m.Cheats.StartRound = CheatRound;
            m.Sound.MuteMask = MuteMask; m.Sound.Smooth = SoundSmooth;
            m.Video.WantPfOnly = Widescreen && (SidePanels == 0 || SidePanels == 2);
            m.Video.SetTheme(Theme);
        }
    }
}
