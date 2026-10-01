// Loads the Dig Dug ROM files from a folder or a .zip archive (user supplied; never shipped with this project).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace DigDug
{
    public sealed class RomSet
    {
        public byte[] Main = new byte[0x4000];   // dd1a.1-4
        public byte[] Sub = new byte[0x2000];    // dd1a.5-6
        public byte[] Sub2 = new byte[0x1000];   // dd1.7
        public byte[] CharGfx;                   // dd1.9   (1bpp text)
        public byte[] SpriteGfx = new byte[0x4000]; // dd1.15,14,13,12
        public byte[] PfGfx;                     // dd1.11  (playfield tiles)
        public byte[] PfMap;                     // dd1.10b (playfield tile map)
        public byte[] PalProm, SpriteLut, CharLut, WavProm;

        static readonly string[] Required = {
            "dd1a.1","dd1a.2","dd1a.3","dd1a.4","dd1a.5","dd1a.6","dd1.7","dd1.9","dd1.10b","dd1.11","dd1.12","dd1.13","dd1.14","dd1.15",
            "136007.110","136007.111","136007.112","136007.113" };

        public static RomSet Load(string path)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(path))
            {
                foreach (var f in Directory.GetFiles(path)) files[Path.GetFileName(f)] = File.ReadAllBytes(f);
            }
            else if (File.Exists(path))
            {
                using (var zf = ZipFile.OpenRead(path))
                    foreach (var e in zf.Entries)
                    {
                        if (e.Length == 0) continue;
                        using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); files[Path.GetFileName(e.FullName)] = ms.ToArray(); }
                    }
            }
            else throw new FileNotFoundException("ROM folder or zip not found: " + path);

            var missing = new List<string>();
            foreach (var r in Required) if (!files.ContainsKey(r)) missing.Add(r);
            if (missing.Count > 0) throw new InvalidDataException("Missing ROM files: " + string.Join(", ", missing.ToArray()));

            var rs = new RomSet();
            Copy(files["dd1a.1"], rs.Main, 0); Copy(files["dd1a.2"], rs.Main, 0x1000);
            Copy(files["dd1a.3"], rs.Main, 0x2000); Copy(files["dd1a.4"], rs.Main, 0x3000);
            Copy(files["dd1a.5"], rs.Sub, 0); Copy(files["dd1a.6"], rs.Sub, 0x1000);
            Copy(files["dd1.7"], rs.Sub2, 0);
            rs.CharGfx = files["dd1.9"];
            Copy(files["dd1.15"], rs.SpriteGfx, 0); Copy(files["dd1.14"], rs.SpriteGfx, 0x1000);
            Copy(files["dd1.13"], rs.SpriteGfx, 0x2000); Copy(files["dd1.12"], rs.SpriteGfx, 0x3000);
            rs.PfGfx = files["dd1.11"]; rs.PfMap = files["dd1.10b"];
            rs.PalProm = files["136007.113"]; rs.SpriteLut = files["136007.111"]; rs.CharLut = files["136007.112"]; rs.WavProm = files["136007.110"];
            return rs;
        }

        static void Copy(byte[] src, byte[] dst, int off) { Array.Copy(src, 0, dst, off, Math.Min(src.Length, dst.Length - off)); }
    }
}
