using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DigDug
{
    static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        const string Usage =
            "Dig Dug (native port)\n\n" +
            "  DigDug [romfolder-or-zip] [options]\n\n" +
            "  --roms <path>      folder or .zip containing the Dig Dug ROM set\n" +
            "  --fullscreen       start in fullscreen\n" +
            "  --lives <1|2|3|5>  lives per game\n" +
            "  --rank <A|B|C|D>   difficulty rank\n" +
            "  --dip0 <hex> --dip1 <hex>   raw DIP switch bytes (advanced)\n\n" +
            "Developer options: --disasm --dumpgfx --makeicon --frames N (see docs/DEVELOPMENT.md)\n";

        static readonly string[] DevFlags = { "disasm", "dumpgfx", "frames", "makeicon", "help" };

        [STAThread]
        static int Main(string[] args)
        {
            string romPath = null;
            var opts = new Dictionary<string, string>();
            var scripted = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    string name = args[i].Substring(2);
                    string v = "1";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) v = args[++i];
                    if (name == "at") scripted.Add(v);
                    opts[name] = v;
                }
                else if (romPath == null) romPath = args[i];
            }

            bool dev = false;
            foreach (var f in DevFlags) if (opts.ContainsKey(f)) dev = true;
            if (dev && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { try { AttachConsole(-1); } catch { } }
            if (opts.ContainsKey("help")) { Console.Write(Usage); return 0; }
            if (opts.ContainsKey("makeicon")) { DevTools.MakeIcon(); return 0; }
            if (opts.ContainsKey("roms")) romPath = opts["roms"];

            var cfg = Settings.Load();
            RomSet roms = null;
            foreach (var cand in Candidates(romPath, cfg))
            {
                try { roms = RomSet.Load(cand); if (Path.IsPathRooted(cand)) cfg.RomPath = cand; break; }
                catch (Exception ex) { Console.Error.WriteLine("Skipping " + cand + ": " + ex.Message); }
            }

            if (dev)
            {
                if (roms == null) { Console.Error.WriteLine("ROMs not found. Use --roms <folder-or-zip>."); return 1; }
                if (opts.ContainsKey("disasm")) { DevTools.Disassemble(roms); return 0; }
                if (opts.ContainsKey("dumpgfx")) { DevTools.DumpGfx(roms); return 0; }
                if (opts.ContainsKey("frames")) { DevTools.Headless(roms, opts, scripted); return 0; }
            }

            if (opts.ContainsKey("fullscreen")) cfg.Fullscreen = true;
            string v2;
            if (opts.TryGetValue("lives", out v2)) { int n; if (int.TryParse(v2, out n) && Array.IndexOf(Settings.LivesValues, n) >= 0) cfg.Lives = n; }
            if (opts.TryGetValue("rank", out v2) && v2.Length > 0) cfg.Rank = Math.Max(0, Math.Min(3, char.ToUpperInvariant(v2[0]) - 'A'));
            return new App(cfg).Run(roms, opts);
        }

        // Translate command-line switches into the two DIP bytes the 53xx chip reports.
        public static void ApplyDipOptions(Machine m, Dictionary<string, string> o)
        {
            string v;
            if (o.TryGetValue("lives", out v))
            {
                int lives; int.TryParse(v, out lives);
                int bits = lives == 1 ? 0x00 : lives == 2 ? 0x40 : lives == 5 ? 0xc0 : 0x80;
                m.Dip0 = (m.Dip0 & ~0xc0) | bits;
            }
            if (o.TryGetValue("rank", out v) && v.Length > 0)
            {
                int bits;
                switch (char.ToUpperInvariant(v[0])) { case 'B': bits = 0x02; break; case 'C': bits = 0x01; break; case 'D': bits = 0x03; break; default: bits = 0; break; }
                m.Dip1 = (m.Dip1 & ~0x03) | bits;
            }
            if (o.TryGetValue("dip0", out v)) m.Dip0 = Convert.ToInt32(v, 16) & 0xff;
            if (o.TryGetValue("dip1", out v)) m.Dip1 = Convert.ToInt32(v, 16) & 0xff;
        }

        // Where to look for the user's ROM set (the ROMs are never bundled with this program).
        static IEnumerable<string> Candidates(string given, Settings cfg)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(given)) list.Add(given);
            if (!string.IsNullOrEmpty(cfg.RomPath)) list.Add(cfg.RomPath);
            string exeDir = AppContext.BaseDirectory;
            var roots = new List<string> { exeDir, Path.Combine(exeDir, ".."), Settings.ConfigDir, Directory.GetCurrentDirectory() };
            string appImage = Environment.GetEnvironmentVariable("APPIMAGE");   // folder containing the .AppImage file
            if (!string.IsNullOrEmpty(appImage)) roots.Insert(0, Path.GetDirectoryName(appImage));
            foreach (var root in roots)
            {
                list.Add(Path.Combine(root, "roms"));
                list.Add(Path.Combine(root, "digdug.zip"));
            }
            foreach (var c in list) if (Directory.Exists(c) || File.Exists(c)) yield return c;
        }
    }
}
