// Developer helpers (disassembly, graphics sheets, headless frame dumps). Output goes to ./out and is git-ignored.
using System;
using System.Collections.Generic;
using System.IO;

namespace DigDug
{
    static class DevTools
    {
        public static void SavePng(int[] px, int w, int h, int scale, string path)
        {
            Png.Write(path, px, w, h, scale);
        }

        // Original (non-ROM) pixel-art icon, so the repository ships no game assets: `DigDug --makeicon`
        public static void MakeIcon()
        {
            Directory.CreateDirectory("assets");
            const int N = 64;
            var px = new int[N * N];
            int sky = unchecked((int)0xff2840d0), dirt = unchecked((int)0xffe07818), dirt2 = unchecked((int)0xffa83808), tunnel = unchecked((int)0xff101010);
            int white = unchecked((int)0xfff0f0ff), blue = unchecked((int)0xff2060f0), red = unchecked((int)0xffe02020), gold = unchecked((int)0xffffd800);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int c = y < 16 ? sky : (y < 40 ? dirt : dirt2);
                    if (((x * 7 + y * 13) % 11) == 0 && y >= 16) c = gold;           // speckles
                    if (y >= 30 && y < 44 && x < 44) c = tunnel;                      // tunnel
                    px[y * N + x] = c;
                }
            // little driller: white suit, blue visor, red pump
            Action<int, int, int, int, int> rect = (x0, y0, w, h, col) => { for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) px[y * N + x] = col; };
            rect(20, 26, 14, 14, white); rect(26, 29, 8, 5, blue); rect(18, 40, 18, 4, white); rect(34, 33, 14, 3, red); rect(46, 31, 4, 7, red);
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                { int dx = Math.Min(x, N - 1 - x), dy = Math.Min(y, N - 1 - y); if (dx < 6 && dy < 6 && (6 - dx) * (6 - dx) + (6 - dy) * (6 - dy) > 36) px[y * N + x] = 0; }
            var big = Png.Encode(px, N, N, 4);
            File.WriteAllBytes("assets/icon.png", big);
            using (var bw = new BinaryWriter(File.Create("assets/icon.ico")))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)1);
                bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
                bw.Write(big.Length); bw.Write(22); bw.Write(big);
            }
            Console.WriteLine("assets/icon.png and assets/icon.ico written");
        }

        public static void Disassemble(RomSet rs)
        {
            Directory.CreateDirectory("out");
            var m = new byte[0x10000]; Array.Copy(rs.Main, m, 0x4000);
            File.WriteAllText("out/cpu1.asm", Disasm.Range(m, 0, 0x4000));
            m = new byte[0x10000]; Array.Copy(rs.Sub, m, 0x2000);
            File.WriteAllText("out/cpu2.asm", Disasm.Range(m, 0, 0x2000));
            m = new byte[0x10000]; Array.Copy(rs.Sub2, m, 0x1000);
            File.WriteAllText("out/cpu3.asm", Disasm.Range(m, 0, 0x1000));
            Console.WriteLine("disassembly written to out/");
        }

        public static void DumpGfx(RomSet rs)
        {
            Directory.CreateDirectory("out");
            var m = new Machine(rs);
            var v = m.Video;
            int[] gray = { unchecked((int)0xff000000), unchecked((int)0xff606060), unchecked((int)0xffb0b0b0), unchecked((int)0xffffffff) };

            var img = new int[128 * 128];
            for (int c = 0; c < 256; c++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        img[((c / 16) * 8 + y) * 128 + (c % 16) * 8 + x] = gray[v.CharPix[c * 64 + y * 8 + x] * 3];
            SavePng(img, 128, 128, 4, "out/chars.png");

            img = new int[128 * 128];
            for (int c = 0; c < 256; c++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        img[((c / 16) * 8 + y) * 128 + (c % 16) * 8 + x] = gray[v.PfPix[c * 64 + y * 8 + x]];
            SavePng(img, 128, 128, 4, "out/pftiles.png");

            img = new int[256 * 256];
            for (int c = 0; c < 256; c++) for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                        img[((c / 16) * 16 + y) * 256 + (c % 16) * 16 + x] = gray[v.SpritePix[c * 256 + y * 16 + x]];
            SavePng(img, 256, 256, 3, "out/sprites.png");
            // experiment: treat each 4K ROM as 2bpp tiles ({0,4} planes) and as 1bpp
            string[] nm = { "dd1.11", "dd1.10b" };
            byte[][] src = { rs.PfGfx, rs.PfMap };
            for (int k = 0; k < 2; k++)
            {
                img = new int[128 * 128];
                int[] xo = { 0, 1, 2, 3, 8, 9, 10, 11 };
                for (int c = 0; c < 256; c++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        {
                            int b = c * 128 + y * 16 + xo[x];
                            int p = (((src[k][b >> 3] >> (7 - (b & 7))) & 1) << 1) | ((src[k][(b + 4) >> 3] >> (7 - ((b + 4) & 7))) & 1);
                            img[((c / 16) * 8 + y) * 128 + (c % 16) * 8 + x] = gray[p];
                        }
                SavePng(img, 128, 128, 3, "out/rom_" + nm[k] + "_2bpp.png");
                // as raw bytes map: 64 wide x 64 high, gray by value
                img = new int[64 * 64];
                for (int i = 0; i < 4096; i++) { int bv = src[k][i]; img[i] = unchecked((int)0xff000000) | bv << 16 | bv << 8 | bv; }
                SavePng(img, 64, 64, 6, "out/rom_" + nm[k] + "_bytes.png");
            }
            // first 64 chars, large, with 1px gutters (chars 0-63 in 16x4 grid)
            img = new int[16 * 9 * 4 * 9];
            for (int i = 0; i < img.Length; i++) img[i] = unchecked((int)0xff203060);
            for (int c = 0; c < 64; c++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        img[((c / 16) * 9 + y) * (16 * 9) + (c % 16) * 9 + x] = gray[v.CharPix[c * 64 + y * 8 + x] * 3];
            SavePng(img, 16 * 9, 4 * 9, 6, "out/chars_first64.png");
            // palette swatches
            img = new int[32 * 16 * 16];
            for (int i = 0; i < 32; i++) for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) img[y * 512 + i * 16 + x] = v.Pal[i];
            SavePng(img, 512, 16, 2, "out/palette.png");
            // 1bpp experiment for dd1.10b: 512 tiles of 8 bytes, LSB-first pixels
            img = new int[128 * 256];
            for (int c = 0; c < 512; c++) for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                        img[((c / 16) * 8 + y) * 128 + (c % 16) * 8 + x] = gray[((rs.PfMap[c * 8 + y] >> x) & 1) * 3];
            SavePng(img, 128, 256, 3, "out/rom_dd1.10b_1bpp.png");
            Console.WriteLine("graphics sheets written to out/");
        }

        static void WriteWav(string path, byte[] pcm)
        {
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + pcm.Length); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Sound.SampleRate); w.Write(Sound.SampleRate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length); w.Write(pcm);
            }
        }

        static void DumpCov(bool[] cov)
        {
            if (cov == null) return;
            var sb = new System.Text.StringBuilder("coverage ranges:");
            int i = 0;
            while (i < 0x4000)
            {
                if (!cov[i]) { i++; continue; }
                int s = i; int gap = 0;
                while (i < 0x4000 && gap < 6) { if (cov[i]) gap = 0; else gap++; i++; }
                sb.Append(" " + s.ToString("x4") + "-" + (i - gap - 1).ToString("x4"));
            }
            Console.WriteLine(sb.ToString());
        }

        public static string ScreenHex(Machine m, int cols)
        {
            var sb = new System.Text.StringBuilder();
            for (int col = 0; col < cols; col++)
            {
                for (int row = 27; row >= 0; row--)
                {
                    int row2 = row + 2, c2 = col - 2, ofs;
                    if ((c2 & 0x20) != 0) ofs = row2 + ((c2 & 0x1f) << 5); else ofs = c2 + (row2 << 5);
                    sb.Append(m.Ram[ofs].ToString("x2")).Append(' ');
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string ScreenText(Machine m)
        {
            var sb = new System.Text.StringBuilder();
            for (int col = 0; col < 36; col++)
            {
                var line = new System.Text.StringBuilder();
                for (int row = 27; row >= 0; row--)
                {
                    int row2 = row + 2, c2 = col - 2, ofs;
                    if ((c2 & 0x20) != 0) ofs = row2 + ((c2 & 0x1f) << 5); else ofs = c2 + (row2 << 5);
                    int code = m.Ram[ofs] & 0x7f;
                    line.Append(code >= 0x10 && code <= 0x19 ? (char)('0' + code - 0x10) : code >= 0x1a && code <= 0x33 ? (char)('A' + code - 0x1a) : code == 0x34 ? '.' : ' ');
                }
                string s = line.ToString().TrimEnd();
                if (s.Length > 0) sb.AppendLine(s);
            }
            return sb.ToString();
        }

        // --frames N [--out base] [--shotframes a,b,c] [--dip0 n --dip1 n] [--at frame:key:dur ...] [--trace06] [--log file]
        public static void Headless(RomSet rs, Dictionary<string, string> o, List<string> scripted)
        {
            Directory.CreateDirectory("out");
            var m = new Machine(rs);
            Program.ApplyDipOptions(m, o);
            if (o.ContainsKey("nv")) m.LoadEarom(Settings.NvPath);
            if (o.ContainsKey("trace06")) m.Trace06 = true;
            if (o.ContainsKey("nopf")) m.Video.DbgNoPf = true;
            if (o.ContainsKey("watch")) { var w = o["watch"].Split('-'); m.WatchLo = Convert.ToInt32(w[0], 16); m.WatchHi = Convert.ToInt32(w[1], 16); m.WatchFrom = o.ContainsKey("watchfrom") ? long.Parse(o["watchfrom"]) : 0; }
            if (o.ContainsKey("hit")) m.HitPc = Convert.ToInt32(o["hit"], 16);
            int frames = int.Parse(o["frames"]);
            string baseName = o.ContainsKey("out") ? o["out"] : "out/frame";
            var shots = new HashSet<int>();
            if (o.ContainsKey("shotframes")) foreach (var s in o["shotframes"].Split(',')) shots.Add(int.Parse(s));
            else shots.Add(frames);

            var events = new List<string[]>();
            foreach (var s in scripted) events.Add(s.Split(':'));

            Bot bot = o.ContainsKey("bot") ? new Bot(m) : null;
            if (bot != null && o.ContainsKey("botidle")) bot.IdleWhenOne = true;
            int botStart = o.ContainsKey("bot") ? int.Parse(o["bot"]) : 0;
            int botLog = o.ContainsKey("botlog") ? int.Parse(o["botlog"]) : 300;
            var sprStats = new SortedDictionary<string, int>();
            var wavMs = o.ContainsKey("wav") ? new MemoryStream() : null;
            var abuf = new short[800];
            for (int f = 1; f <= frames; f++)
            {
                var inp = m.Input;
                inp.Coin1 = inp.Coin2 = inp.Start1 = inp.Start2 = inp.Fire = false; inp.Dir = -1;
                foreach (var e in events)
                {
                    int start = int.Parse(e[0]), dur = e.Length > 2 ? int.Parse(e[2]) : 5;
                    if (f < start || f >= start + dur) continue;
                    switch (e[1])
                    {
                        case "coin1": inp.Coin1 = true; break;
                        case "coin2": inp.Coin2 = true; break;
                        case "start1": inp.Start1 = true; break;
                        case "start2": inp.Start2 = true; break;
                        case "service": inp.Service = true; break;
                        case "fire": inp.Fire = true; break;
                        case "up": inp.Dir = 0; break;
                        case "right": inp.Dir = 2; break;
                        case "down": inp.Dir = 4; break;
                        case "left": inp.Dir = 6; break;
                    }
                }
                if (bot != null && f >= botStart)
                {
                    bot.Step(inp, f);
                    if (f % botLog == 0)
                    {
                        string st = ScreenText(m); var mm = System.Text.RegularExpressions.Regex.Match(st, @"(\d+)\s+10000"); var rr = System.Text.RegularExpressions.Regex.Match(st, @"ROUND\s+(\d+)");
                        Console.WriteLine("bot f" + f + " score=" + mm.Groups[1].Value + " round=" + rr.Groups[1].Value + " " + bot.LastNote);
                    }
                }
                if (o.ContainsKey("cov"))
                {
                    var cr = o["cov"].Split('-');
                    int c0 = int.Parse(cr[0]), c1 = int.Parse(cr[1]);
                    if (f == c0) m.Cov = new bool[65536];
                    if (f == c1 + 1) { DumpCov(m.Cov); m.Cov = null; }
                }
                m.RunFrame();
                if (wavMs != null) { m.Sound.Mix(abuf, 0, 792); for (int k = 0; k < 792; k++) { wavMs.WriteByte((byte)abuf[k]); wavMs.WriteByte((byte)(abuf[k] >> 8)); } }
                if (o.ContainsKey("trace8970") && f >= int.Parse(o["trace8970"].Split('-')[0]) && f <= int.Parse(o["trace8970"].Split('-')[1]))
                    Console.WriteLine("f" + f + " 8970=" + m.Ram[0x970].ToString("x2") + " 8657=" + m.Ram[0x657].ToString("x2") + " 8401=" + m.Ram[0x401].ToString("x2") + " 8403=" + m.Ram[0x403].ToString("x2") + " 8404=" + m.Ram[0x404].ToString("x2") + " 85b0=" + m.Ram[0x5b0].ToString("x2"));
                if (o.ContainsKey("pcs") && f % int.Parse(o["pcs"]) == 0)
                    Console.WriteLine("f" + f + " PC " + m.Cpu[0].PC.ToString("x4") + " " + m.Cpu[1].PC.ToString("x4") + " " + m.Cpu[2].PC.ToString("x4")
                        + " cnt=" + (m.Ram[0x423] | m.Ram[0x424] << 8).ToString("x4") + " 8400=" + m.Ram[0x400].ToString("x2") + " " + m.Ram[0x401].ToString("x2") + " " + m.Ram[0x402].ToString("x2")
                        + " 87cc=" + m.Ram[0x7cc].ToString("x2") + " 85a7=" + m.Ram[0x5a7].ToString("x2") + " " + m.Ram[0x5a8].ToString("x2") + " " + m.Ram[0x5a9].ToString("x2") + " 8657=" + m.Ram[0x657].ToString("x2"));
                if (o.ContainsKey("sprstats") && f > 1900)
                    for (int s = 0; s < 64; s++)
                    {
                        int a = s * 2, code = m.Ram[0xb80 + a], fl = m.Ram[0x1b80 + a], y = m.Ram[0x1380 + a];
                        if (y == 0 && m.Ram[0x1381 + a] == 0x37) continue; // parked
                        string key = (code >= 0x80 ? "BIG " : "    ") + "code=" + code.ToString("x2") + " flags=" + fl.ToString("x2");
                        if (code >= 0x80 || (fl & 0x0c) != 0) { int c; sprStats.TryGetValue(key, out c); sprStats[key] = c + 1; }
                    }
                if (shots.Contains(f))
                {
                    m.Video.Render();
                    SavePng(m.Video.Pixels, Video.OutW, Video.OutH, 2, baseName + "_" + f + ".png");
                    Console.WriteLine("saved " + baseName + "_" + f + ".png");
                }
            }
            for (int c = 0; c < 3; c++)
            {
                var z = m.Cpu[c];
                Console.WriteLine("CPU" + (c + 1) + " PC=" + z.PC.ToString("x4") + " SP=" + z.SP.ToString("x4") + " IFF=" + z.IFF1 + " IM=" + z.IM + " halted=" + z.Halted);
            }
            Console.WriteLine("frame counter $8423: " + (m.Ram[0x423] | m.Ram[0x424] << 8).ToString("x4"));
            foreach (var kv in sprStats) Console.WriteLine("sprstat " + kv.Key + " x" + kv.Value);
            if (o.ContainsKey("hit")) Console.WriteLine("hits at " + o["hit"] + ": " + m.HitCount);
            if (o.ContainsKey("peek"))
                foreach (var a in o["peek"].Split(','))
                {
                    int addr = Convert.ToInt32(a, 16);
                    Console.WriteLine("peek " + a + " = " + m.Ram[addr - 0x8000].ToString("x2"));
                }
            if (o.ContainsKey("text")) Console.Write(ScreenText(m));
            if (o.ContainsKey("hex")) Console.Write(ScreenHex(m, int.Parse(o["hex"])));
            if (o.ContainsKey("spr"))
                for (int s = 0; s < 64; s++)
                {
                    int a = s * 2;
                    Console.WriteLine("spr" + s + ": code=" + m.Ram[0xb80 + a].ToString("x2") + " col=" + m.Ram[0xb81 + a].ToString("x2") + " y=" + m.Ram[0x1380 + a].ToString("x2") + " x=" + m.Ram[0x1381 + a].ToString("x2") + " f=" + m.Ram[0x1b80 + a].ToString("x2") + " xh=" + m.Ram[0x1b81 + a].ToString("x2"));
                }
            if (wavMs != null) WriteWav(o["wav"], wavMs.ToArray());
            if (o.ContainsKey("ramdump")) File.WriteAllBytes(o["ramdump"], m.Ram);
        }
    }
}





