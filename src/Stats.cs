// Play statistics and achievements (stored locally in stats.ini; nothing is ever sent anywhere).
using System;
using System.Collections.Generic;
using System.IO;

namespace DigDug
{
    public static class Achievements
    {
        public struct Def { public string Name, Desc; public Def(string n, string d) { Name = n; Desc = d; } }
        public const int FirstDig = 0, Tunneler = 1, Excavator = 2, DrillSergeant = 3, MasterDriller = 4, Legend = 5, Round3 = 6, Round5 = 7,
            Round10 = 8, Round15 = 9, Marathon = 10, Regular = 11, BigSpender = 12, TimeTraveler = 13, Director = 14, Photographer = 15,
            Cheater = 16, SaveScummer = 17, Stylish = 18;
        public static readonly Def[] All =
        {
            new Def("FIRST DIG", "START YOUR FIRST GAME"),
            new Def("TUNNELER", "SCORE 1000 POINTS"),
            new Def("EXCAVATOR", "SCORE 5000 POINTS"),
            new Def("DRILL SERGEANT", "SCORE 10000 POINTS"),
            new Def("MASTER DRILLER", "SCORE 30000 POINTS"),
            new Def("LEGEND", "SCORE 100000 POINTS"),
            new Def("GETTING DEEP", "REACH ROUND 3"),
            new Def("HALFWAY DOWN", "REACH ROUND 5"),
            new Def("MOLE PERSON", "REACH ROUND 10"),
            new Def("CORE SAMPLE", "REACH ROUND 15"),
            new Def("MARATHON", "PLAY FOR 30 MINUTES"),
            new Def("REGULAR", "START 10 GAMES"),
            new Def("BIG SPENDER", "USE 25 CREDITS"),
            new Def("TIME TRAVELER", "REWIND THE GAME"),
            new Def("DIRECTOR", "RECORD A REPLAY"),
            new Def("PHOTOGRAPHER", "TAKE A SCREENSHOT"),
            new Def("CHEATER", "TURN ON A CHEAT"),
            new Def("SAVE SCUMMER", "USE A SAVE STATE"),
            new Def("STYLISH", "CHANGE THE COLOUR THEME"),
        };
    }

    public sealed class Stats
    {
        public long PlayFrames;       // frames spent in a real game (not attract mode)
        public int Games, Coins, Deaths, BestScore, MaxRound;
        public uint Unlocked;         // bitmask of achievements

        public double PlaySeconds { get { return PlayFrames / 60.6061; } }
        public bool Has(int id) { return (Unlocked & (1u << id)) != 0; }
        public int UnlockedCount { get { int n = 0; for (int i = 0; i < Achievements.All.Length; i++) if (Has(i)) n++; return n; } }

        static string FilePath { get { return Path.Combine(Settings.ConfigDir, "stats.ini"); } }

        public static Stats Load()
        {
            var s = new Stats();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('='); if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(); long v; long.TryParse(line.Substring(eq + 1).Trim(), out v);
                    switch (k)
                    {
                        case "play_frames": s.PlayFrames = v; break;
                        case "games": s.Games = (int)v; break;
                        case "coins": s.Coins = (int)v; break;
                        case "deaths": s.Deaths = (int)v; break;
                        case "best_score": s.BestScore = (int)v; break;
                        case "max_round": s.MaxRound = (int)v; break;
                        case "unlocked": s.Unlocked = (uint)v; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            if (Settings.NoSave) return;
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "play_frames=" + PlayFrames, "games=" + Games, "coins=" + Coins, "deaths=" + Deaths,
                    "best_score=" + BestScore, "max_round=" + MaxRound, "unlocked=" + Unlocked
                });
            }
            catch { }
        }

        public void Reset() { PlayFrames = 0; Games = Coins = Deaths = BestScore = MaxRound = 0; Unlocked = 0; Save(); }
    }
}
