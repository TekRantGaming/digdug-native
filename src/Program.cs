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

        public static readonly List<string> StartupLog = new List<string>();
        public static string RomSource;     // where the ROM set was loaded from (also shown in the menu)

        static readonly string[] DevFlags = { "disasm", "dumpgfx", "frames", "makeicon", "help", "padtest", "rom-status" };

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
            if (opts.ContainsKey("padtest")) return PadTest();
            if (opts.ContainsKey("roms")) romPath = opts["roms"];

            var cfg = Settings.Load();
            RomSet roms = null;
            StartupLog.Add("exe folder: " + AppContext.BaseDirectory + "; working folder: " + Directory.GetCurrentDirectory() + "; config folder: " + Settings.ConfigDir);
            StartupLog.Add("remembered ROM path in settings: '" + cfg.RomPath + "'");
            foreach (var cand in Candidates(romPath, cfg))
            {
                try { roms = RomSet.Load(cand); StartupLog.Add("ROM set LOADED from: " + cand); RomSource = cand; if (Path.IsPathRooted(cand)) cfg.RomPath = cand; break; }
                catch (Exception ex) { StartupLog.Add("candidate rejected: " + cand + " (" + ex.Message + ")"); Console.Error.WriteLine("Skipping " + cand + ": " + ex.Message); }
            }
            if (roms == null) StartupLog.Add("no ROM set found");

            if (opts.ContainsKey("rom-status"))
            {
                foreach (var line in StartupLog) Console.WriteLine(line);
                Console.WriteLine(roms != null ? "ROM-STATUS: FOUND at " + RomSource : "ROM-STATUS: NOT FOUND");
                return roms != null ? 0 : 3;
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
            var app = new App(cfg);
            foreach (var s in scripted) app.Script.Add(s.Split(':'));
            return app.Run(roms, opts);
        }

        // `DigDug --padtest`: print live controller state (diagnostics)
        static int PadTest()
        {
            Sdl.SDL_Init(Sdl.InitVideo | Sdl.InitGameController | Sdl.InitEvents);
            var ev = Marshal.AllocHGlobal(64);
            var pads = new List<IntPtr>();
            for (int i = 0; i < Sdl.SDL_NumJoysticks(); i++)
            {
                Console.WriteLine("device " + i + " isGameController=" + Sdl.SDL_IsGameController(i));
                if (Sdl.SDL_IsGameController(i) != 0) { var p = Sdl.SDL_GameControllerOpen(i); if (p != IntPtr.Zero) { pads.Add(p); Console.WriteLine("  opened: " + Sdl.ControllerName(p)); } }
            }
            for (int s = 0; s < 6; s++)
            {
                while (Sdl.SDL_PollEvent(ev) != 0) { }
                foreach (var p in pads)
                {
                    var sb = new System.Text.StringBuilder("t" + s + " buttons:");
                    for (int b = 0; b < 15; b++) if (Sdl.SDL_GameControllerGetButton(p, b) != 0) sb.Append(" " + b);
                    sb.Append("  axes:");
                    for (int a = 0; a < 6; a++) sb.Append(" " + Sdl.SDL_GameControllerGetAxis(p, a));
                    Console.WriteLine(sb.ToString());
                }
                Sdl.SDL_Delay(1000);
            }
            return 0;
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
            // only the documented places: next to the program (or the .AppImage) and the per-user config folder
            var roots = new List<string> { exeDir, Settings.ConfigDir };
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
