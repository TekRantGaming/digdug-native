// Tiny PNG writer (used by the developer tools and the icon generator).
using System;
using System.IO;
using System.IO.Compression;

namespace DigDug
{
    public static class Png
    {
        static uint[] table;

        static uint Crc(byte[] d, int off, int len, uint crc = 0xffffffff)
        {
            if (table == null)
            {
                table = new uint[256];
                for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1; table[n] = c; }
            }
            for (int i = 0; i < len; i++) crc = table[(crc ^ d[off + i]) & 0xff] ^ (crc >> 8);
            return crc;
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length };
            s.Write(len, 0, 4);
            var td = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
            Array.Copy(data, 0, td, 4, data.Length);
            s.Write(td, 0, td.Length);
            uint c = ~Crc(td, 0, td.Length);
            s.Write(new byte[] { (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c }, 0, 4);
        }

        public static byte[] Encode(int[] argb, int w, int h, int scale)
        {
            int W = w * scale, H = h * scale;
            var raw = new byte[H * (1 + W * 4)];
            int p = 0;
            for (int y = 0; y < H; y++)
            {
                raw[p++] = 0;
                for (int x = 0; x < W; x++)
                {
                    int c = argb[(y / scale) * w + x / scale];
                    raw[p++] = (byte)(c >> 16); raw[p++] = (byte)(c >> 8); raw[p++] = (byte)c; raw[p++] = (byte)(c >> 24);
                }
            }
            var ms = new MemoryStream();
            ms.Write(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, 0, 8);
            var ihdr = new byte[13];
            ihdr[0] = (byte)(W >> 24); ihdr[1] = (byte)(W >> 16); ihdr[2] = (byte)(W >> 8); ihdr[3] = (byte)W;
            ihdr[4] = (byte)(H >> 24); ihdr[5] = (byte)(H >> 16); ihdr[6] = (byte)(H >> 8); ihdr[7] = (byte)H;
            ihdr[8] = 8; ihdr[9] = 6;
            Chunk(ms, "IHDR", ihdr);
            var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) zs.Write(raw, 0, raw.Length);
            Chunk(ms, "IDAT", z.ToArray());
            Chunk(ms, "IEND", new byte[0]);
            return ms.ToArray();
        }

        public static void Write(string path, int[] argb, int w, int h, int scale)
        {
            File.WriteAllBytes(path, Encode(argb, w, h, scale));
        }
    }
}
