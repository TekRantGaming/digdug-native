// User settings, stored as simple key=value lines in the per-user config folder.
using System;
using System.Collections.Generic;
using System.IO;

namespace DigDug
{
    public sealed class Settings
    {
        public bool Fullscreen;
        public int WindowScale;          // 0 = automatic, otherwise 1..8
        public bool IntegerScale = true; // true = whole-number scaling (sharp pixels), false = fit window
        public bool Smooth;              // bilinear filtering
        public int Volume = 80;          // 0..100
        public int Lives = 3;            // 1, 2, 3, 5
        public int Bonus = 4;            // value of DIP byte 0 bits 3-5 (see BonusNames)
        public int Rank;                 // 0..3 = A..D
        public string RomPath = "";
        public string AudioDevice = "";  // empty = system default
        public bool Widescreen = true;   // fill the side areas of wide windows/fullscreen with an extension of the dirt
        public bool AutoPump = true;     // holding fire re-presses automatically so the pump keeps inflating
        public bool AutoCoin = true;     // pressing Start with no credit inserts one automatically

        public static readonly int[] BonusValues = { 4, 2, 6, 1, 0 };
        public static readonly string[] BonusNames = { "10000 40000", "10000 50000", "20000 60000", "20000 70000", "NONE" };
        public static readonly int[] LivesValues = { 1, 2, 3, 5 };

        public static string ConfigDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DigDugNative");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }
        static string FilePath { get { return Path.Combine(ConfigDir, "settings.ini"); } }
        public static string NvPath { get { return Path.Combine(ConfigDir, "digdug.nv"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int n; int.TryParse(v, out n);
                    switch (k)
                    {
                        case "fullscreen": s.Fullscreen = n != 0; break;
                        case "window_scale": s.WindowScale = Math.Max(0, Math.Min(8, n)); break;
                        case "integer_scale": s.IntegerScale = n != 0; break;
                        case "smooth": s.Smooth = n != 0; break;
                        case "volume": s.Volume = Math.Max(0, Math.Min(100, n)); break;
                        case "lives": if (Array.IndexOf(LivesValues, n) >= 0) s.Lives = n; break;
                        case "bonus": if (Array.IndexOf(BonusValues, n) >= 0) s.Bonus = n; break;
                        case "rank": s.Rank = Math.Max(0, Math.Min(3, n)); break;
                        case "rom_path": s.RomPath = v; break;
                        case "audio_device": s.AudioDevice = v; break;
                        case "auto_coin": s.AutoCoin = n != 0; break;
                        case "widescreen": s.Widescreen = n != 0; break;
                        case "auto_pump": s.AutoPump = n != 0; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                var l = new List<string>
                {
                    "fullscreen=" + (Fullscreen ? 1 : 0), "window_scale=" + WindowScale, "integer_scale=" + (IntegerScale ? 1 : 0),
                    "smooth=" + (Smooth ? 1 : 0), "volume=" + Volume, "lives=" + Lives, "bonus=" + Bonus, "rank=" + Rank, "rom_path=" + RomPath, "audio_device=" + AudioDevice, "auto_coin=" + (AutoCoin ? 1 : 0), "widescreen=" + (Widescreen ? 1 : 0), "auto_pump=" + (AutoPump ? 1 : 0)
                };
                File.WriteAllLines(FilePath, l.ToArray());
            }
            catch { }
        }

        // DIP bytes reported by the 53xx: coin B 1c/1cr, coin A 1c/1cr, upright, plus the user's choices.
        public void ApplyTo(Machine m)
        {
            int lv = Lives == 1 ? 0 : Lives == 2 ? 1 : Lives == 5 ? 3 : 2;
            m.Dip0 = (lv << 6) | ((Bonus & 7) << 3) | 1;
            int rankBits = Rank == 1 ? 0x02 : Rank == 2 ? 0x01 : Rank == 3 ? 0x03 : 0;
            m.Dip1 = 0x3c | rankBits;
        }
    }
}
