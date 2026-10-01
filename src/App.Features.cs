// Front end - features built around the emulator: save states, rewind, replays, statistics, achievements, notices.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DigDug
{
    public sealed unsafe partial class App
    {
        // ================================================================== notices
        sealed class ToastMsg { public string Text; public bool Gold; public double Until; }
        readonly List<ToastMsg> toasts = new List<ToastMsg>();

        void Toast(string text, bool gold = false)
        {
            text = ArcadeText.Clean(text);
            toasts.RemoveAll(t => t.Text == text);
            toasts.Add(new ToastMsg { Text = text, Gold = gold, Until = sw.Elapsed.TotalSeconds + (gold ? 3.5 : 2.0) });
            while (toasts.Count > 3) toasts.RemoveAt(0);
        }

        void Unlock(int id)
        {
            if (stats.Has(id)) return;
            stats.Unlocked |= 1u << id;
            stats.Save();
            Toast("ACHIEVEMENT " + Achievements.All[id].Name, true);
            Log("achievement: " + Achievements.All[id].Name);
        }

        // ================================================================== per-frame bookkeeping
        bool wasInGame;
        int inGameRun;
        int lastCredits, lastDeaths;

        void OnFrameDone()
        {
            var m = machine;
            // a real game: the flag must hold steadily (it can blip while the board boots)
            inGameRun = m.InGame && m.FrameCount > BootFrames ? inGameRun + 1 : 0;
            bool inGame = inGameRun >= 120;
            if (inGame && !wasInGame)
            {
                stats.Games++;
                Unlock(Achievements.FirstDig);
                if (stats.Games >= 10) Unlock(Achievements.Regular);
            }
            wasInGame = inGame;

            int cr = m.Chip51.Credits;
            if (cr > lastCredits) { stats.Coins += cr - lastCredits; if (stats.Coins >= 25) Unlock(Achievements.BigSpender); }
            lastCredits = cr;

            if (m.Deaths > lastDeaths)
            {
                if (lastDeaths >= 0) { stats.Deaths += m.Deaths - lastDeaths; Rumble(100, 450); }
            }
            lastDeaths = m.Deaths;

            if (inGame)
            {
                stats.PlayFrames++;
                if (stats.PlayFrames >= 30L * 60 * 61) Unlock(Achievements.Marathon);
                if ((stats.PlayFrames & 31) == 0) ScanScreen();
                if ((stats.PlayFrames % 3600) == 0) stats.Save();
                if (cfg.CheatLives || cfg.CheatInvincible || cfg.CheatRound > 1) Unlock(Achievements.Cheater);
            }

            if (!replayPlaying && !recording) CaptureRewind();
        }

        // Score and round are read from the screen text (the game keeps them as characters in video RAM).
        static readonly Regex ScoreRx = new Regex(@"(\d+)\s+10000|HIGH\s+SCORE\s+\S*\s*(\d+)", RegexOptions.Compiled);
        static readonly Regex RoundRx = new Regex(@"ROUND\s+(\d+)", RegexOptions.Compiled);

        void ScanScreen()
        {
            string t = DevTools.ScreenText(machine);
            int score = ParseScore(t);
            if (score > stats.BestScore) stats.BestScore = score;
            if (score >= 1000) Unlock(Achievements.Tunneler);
            if (score >= 5000) Unlock(Achievements.Excavator);
            if (score >= 10000) Unlock(Achievements.DrillSergeant);
            if (score >= 30000) Unlock(Achievements.MasterDriller);
            if (score >= 100000) Unlock(Achievements.Legend);
            var rm = RoundRx.Match(t);
            int round; if (rm.Success && int.TryParse(rm.Groups[1].Value, out round) && round < 100)
            {
                if (round > stats.MaxRound) stats.MaxRound = round;
                if (round >= 3) Unlock(Achievements.Round3);
                if (round >= 5) Unlock(Achievements.Round5);
                if (round >= 10) Unlock(Achievements.Round10);
                if (round >= 15) Unlock(Achievements.Round15);
            }
        }

        // the player's score is the number on the 1UP line (the first number on the screen's score row)
        static int ParseScore(string t)
        {
            var lines = t.Split('\n');
            foreach (var l in lines)
            {
                var m = Regex.Match(l.Trim(), @"^(\d+)\s+(\d+)$");
                if (m.Success) { int v; if (int.TryParse(m.Groups[1].Value, out v)) return v; }
            }
            return 0;
        }

        // ================================================================== rewind
        readonly List<byte[]> ring = new List<byte[]>();
        const int RingMax = 900;       // 15 seconds

        void CaptureRewind()
        {
            if (machine.FrameCount < BootFrames) return;
            ring.Add(machine.SaveState());
            if (ring.Count > RingMax) ring.RemoveRange(0, ring.Count - RingMax);
        }

        bool CanRewind() { return !replayPlaying && !recording && ring.Count > 4; }
        void ClearRewind() { ring.Clear(); }

        void RewindStep()
        {
            // two snapshots per step = rewinding at double speed
            for (int k = 0; k < 2 && ring.Count > 1; k++) ring.RemoveAt(ring.Count - 1);
            machine.LoadState(ring[ring.Count - 1]);
            ring.RemoveAt(ring.Count - 1);
            lastCredits = machine.Chip51.Credits; lastDeaths = machine.Deaths;
            Unlock(Achievements.TimeTraveler);
        }

        // ================================================================== save states
        const int StateSlots = 5;
        int stateSlot = 1;

        static string SlotPath(int n) { return Path.Combine(Settings.SubDir("states"), "slot" + n + ".sav"); }

        string SlotInfo(int n)
        {
            string p = SlotPath(n);
            return File.Exists(p) ? File.GetLastWriteTime(p).ToString("dd/MM HH:mm") : "EMPTY";
        }

        void SaveSlot(int n)
        {
            if (machine.FrameCount < BootFrames) { Toast("NOT READY YET"); return; }
            try { File.WriteAllBytes(SlotPath(n), machine.SaveState()); Toast("STATE " + n + " SAVED"); Unlock(Achievements.SaveScummer); }
            catch (Exception ex) { Log("save state failed: " + ex.Message); Toast("SAVE FAILED"); }
        }

        void LoadSlot(int n)
        {
            string p = SlotPath(n);
            if (!File.Exists(p)) { Toast("STATE " + n + " IS EMPTY"); return; }
            try
            {
                if (!machine.LoadState(File.ReadAllBytes(p))) { Toast("STATE NOT COMPATIBLE"); return; }
                ClearRewind(); lastCredits = machine.Chip51.Credits; lastDeaths = machine.Deaths;
                Toast("STATE " + n + " LOADED"); Unlock(Achievements.SaveScummer);
            }
            catch (Exception ex) { Log("load state failed: " + ex.Message); Toast("LOAD FAILED"); }
        }

        // ================================================================== replays
        // A replay is a save state plus the controls for every frame after it; the emulation is deterministic, so playing the
        // controls back reproduces the game exactly.
        bool recording, replayPlaying;
        byte[] replayState;
        readonly List<byte> replayInput = new List<byte>();
        int replayPos;
        static readonly byte[] ReplayMagic = { (byte)'D', (byte)'D', (byte)'R', (byte)'1' };

        static string ReplayDir { get { return Settings.SubDir("replays"); } }

        void StartRecording()
        {
            if (replayPlaying || machine.FrameCount < BootFrames) { Toast("CANNOT RECORD NOW"); return; }
            replayState = machine.SaveState();
            replayInput.Clear();
            recording = true; ClearRewind();
            Toast("RECORDING");
        }

        void StopRecording()
        {
            if (!recording) return;
            recording = false;
            try
            {
                string file = Path.Combine(ReplayDir, "replay_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".ddr");
                using (var w = new BinaryWriter(File.Create(file)))
                {
                    w.Write(ReplayMagic); w.Write(replayState.Length); w.Write(replayState);
                    w.Write(replayInput.Count); w.Write(replayInput.ToArray());
                }
                Toast("REPLAY SAVED " + replayInput.Count / 60 + "S");
                Unlock(Achievements.Director);
            }
            catch (Exception ex) { Log("replay save failed: " + ex.Message); Toast("REPLAY SAVE FAILED"); }
        }

        string LatestReplay()
        {
            try
            {
                string best = null; DateTime bt = DateTime.MinValue;
                foreach (var f in Directory.GetFiles(ReplayDir, "*.ddr"))
                { var t = File.GetLastWriteTime(f); if (t >= bt) { bt = t; best = f; } }
                return best;
            }
            catch { return null; }
        }

        bool PlayLatestReplay()
        {
            string f = LatestReplay();
            if (f == null || recording) { Toast("NO REPLAY FOUND"); return false; }
            try
            {
                using (var r = new BinaryReader(File.OpenRead(f)))
                {
                    var mg = r.ReadBytes(4);
                    if (mg.Length != 4 || mg[0] != ReplayMagic[0] || mg[3] != ReplayMagic[3]) { Toast("BAD REPLAY FILE"); return false; }
                    var st = r.ReadBytes(r.ReadInt32());
                    var inp = r.ReadBytes(r.ReadInt32());
                    if (!machine.LoadState(st)) { Toast("REPLAY NOT COMPATIBLE"); return false; }
                    replayInput.Clear(); replayInput.AddRange(inp);
                }
            }
            catch (Exception ex) { Log("replay load failed: " + ex.Message); Toast("REPLAY LOAD FAILED"); return false; }
            replayPos = 0; replayPlaying = true; ClearRewind();
            lastCredits = machine.Chip51.Credits; lastDeaths = machine.Deaths;
            Toast("PLAYING REPLAY");
            return true;
        }

        void StopReplay() { if (replayPlaying) { replayPlaying = false; Toast("REPLAY STOPPED"); } }

        void RecordFrame()
        {
            var i = machine.Input;
            int b = (i.Coin1 ? 1 : 0) | (i.Start1 ? 2 : 0) | (i.Start2 ? 4 : 0) | (i.Fire ? 8 : 0) | (i.Service ? 16 : 0) | ((i.Dir < 0 ? 0 : i.Dir / 2 + 1) << 5);
            replayInput.Add((byte)b);
        }

        void ApplyReplayFrame()
        {
            if (replayPos >= replayInput.Count) { replayPlaying = false; Toast("REPLAY FINISHED"); PollInput(); return; }
            int b = replayInput[replayPos++];
            var i = machine.Input;
            i.Coin1 = (b & 1) != 0; i.Start1 = (b & 2) != 0; i.Start2 = (b & 4) != 0; i.Fire = (b & 8) != 0; i.Service = (b & 16) != 0;
            i.Coin2 = false; int d = b >> 5; i.Dir = d == 0 ? -1 : (d - 1) * 2;
        }

        // ================================================================== misc actions
        void OpenFolder(string path)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo("xdg-open", "\"" + path + "\"") { UseShellExecute = false });
            }
            catch (Exception ex) { Log("open folder failed: " + ex.Message); Toast("CANNOT OPEN FOLDER"); }
        }

        void ResetGame()
        {
            machine.Reset(); cfg.ApplyTo(machine); ClearRewind();
            lastCredits = 0; lastDeaths = machine.Deaths; wasInGame = false; inGameRun = 0;
        }

        void ResetHighScores()
        {
            Array.Clear(machine.Earom, 0, machine.Earom.Length);
            machine.SaveEarom(Settings.NvPath);
            ResetGame();
            Toast("HIGH SCORES RESET");
        }
    }
}
