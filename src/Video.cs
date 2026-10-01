// Dig Dug video: 36x28 tile playfield (ROM-driven dirt map), 1bpp text layer, 64 16x16 sprites.
// Rendered natively at 288x224, then rotated 90 degrees to match the vertical monitor (224x288 output).
using System;

namespace DigDug
{
    public sealed class Video
    {
        public const int NativeW = 288, NativeH = 224;
        public const int OutW = 224, OutH = 288;

        readonly Machine m;
        public bool DbgNoPf, DbgNoTx;
        public readonly int[] Pixels = new int[OutW * OutH];
        readonly int[] native = new int[NativeW * NativeH];
        public readonly int[] Pal = new int[32];

        public readonly byte[] CharPix = new byte[256 * 64];    // 8x8, pen 0/1
        public readonly byte[] PfPix = new byte[256 * 64];      // 8x8, pen 0..3
        public readonly byte[] SpritePix = new byte[256 * 256]; // 16x16, pen 0..3

        public Video(Machine mm)
        {
            m = mm;
            var r = mm.Roms;
            for (int i = 0; i < 32; i++)
            {
                int b = r.PalProm[i];
                int R = 0x21 * (b & 1) + 0x47 * ((b >> 1) & 1) + 0x97 * ((b >> 2) & 1);
                int G = 0x21 * ((b >> 3) & 1) + 0x47 * ((b >> 4) & 1) + 0x97 * ((b >> 5) & 1);
                int B = 0x51 * ((b >> 6) & 1) + 0xae * ((b >> 7) & 1);
                Pal[i] = (255 << 24) | (R << 16) | (G << 8) | B;
            }
            DecodeChars(r.CharGfx);
            DecodePf(r.PfGfx);
            DecodeSprites(r.SpriteGfx);
        }

        // MAME-style bit addressing: bit offset n -> (byte[n/8] >> (7 - n%8)) & 1
        static int Bit(byte[] d, int bitOfs) { int i = bitOfs >> 3; if (i >= d.Length) return 0; return (d[i] >> (7 - (bitOfs & 7))) & 1; }

        void DecodeChars(byte[] d)
        {
            for (int c = 0; c < 256; c++)
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                        CharPix[c * 64 + y * 8 + x] = (byte)Bit(d, c * 64 + y * 8 + (7 - x));
        }

        void DecodePf(byte[] d)
        {
            int[] xo = { 0, 1, 2, 3, 8, 9, 10, 11 };
            for (int c = 0; c < 256; c++)
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        int b = c * 128 + y * 16 + xo[x];
                        PfPix[c * 64 + y * 8 + x] = (byte)(Bit(d, b) << 1 | Bit(d, b + 4));
                    }
        }

        void DecodeSprites(byte[] d)
        {
            int[] xo = { 0, 1, 2, 3, 64, 65, 66, 67, 128, 129, 130, 131, 192, 193, 194, 195 };
            int[] yo = { 0, 8, 16, 24, 32, 40, 48, 56, 256, 264, 272, 280, 288, 296, 304, 312 };
            for (int c = 0; c < 256; c++)
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                    {
                        int b = c * 512 + yo[y] + xo[x];
                        SpritePix[c * 256 + y * 16 + x] = (byte)(Bit(d, b) << 1 | Bit(d, b + 4));
                    }
        }

        // memory offset in the 32x32 tile RAM for tilemap position (col 0..35, row 0..27)
        static int ScanOffset(int col, int row)
        {
            row += 2; col -= 2;
            if ((col & 0x20) != 0) return row + ((col & 0x1f) << 5);
            return col + (row << 5);
        }

        public void Render()
        {
            var ram = m.Ram;
            var roms = m.Roms;
            // --- playfield
            for (int row = 0; row < 28; row++)
                for (int col = 0; col < 36; col++)
                {
                    int ofs = ScanOffset(col, row);
                    int px = col * 8, py = row * 8;
                    if (m.BgDisable || DbgNoPf) { FillTile(px, py, Pal[DbgNoPf ? 3 : 0x10]); continue; }
                    int code = roms.PfMap[(ofs & 0x3ff) | (m.BgSelect << 10)];
                    int color = (code >> 4) | (m.BgColorBank << 4);
                    int tile = code;
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                        {
                            int pen = PfPix[tile * 64 + y * 8 + x];
                            int idx = roms.CharLut[((color & 0x3f) << 2 | pen) & 0xff] & 0x0f;
                            native[(py + y) * NativeW + px + x] = Pal[idx];
                        }
                }

            if (WantPfOnly) Array.Copy(native, pfSnap, native.Length);

            // --- text layer (also carries the tunnel shapes)
            for (int row = 0; row < 28; row++)
                for (int col = 0; col < 36; col++)
                {
                    int ofs = ScanOffset(col, row);
                    int code = ram[ofs];
                    int color = ((code >> 4) & 0x0e) | ((code >> 3) & 2);
                    int tile = code & 0x7f;
                    int px = col * 8, py = row * 8;
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                            if (CharPix[tile * 64 + y * 8 + x] != 0)
                                native[(py + y) * NativeW + px + x] = Pal[color & 0x0f];
                }

            // --- sprites (above the text layer)
            DrawSprites();

            // --- rotate 90 degrees clockwise: (x,y) -> (H-1-y, x)
            for (int y = 0; y < NativeH; y++)
                for (int x = 0; x < NativeW; x++)
                    Pixels[x * OutW + (NativeH - 1 - y)] = native[y * NativeW + x];
            if (WantPfOnly)
                for (int y = 0; y < NativeH; y++)
                    for (int x = 0; x < NativeW; x++)
                        PfOnly[x * OutW + (NativeH - 1 - y)] = pfSnap[y * NativeW + x];
        }

        // Playfield (dirt/sky) without tunnels, text or sprites - used by the widescreen mode to extend the level sideways.
        public bool WantPfOnly;
        public readonly int[] PfOnly = new int[OutW * OutH];
        readonly int[] pfSnap = new int[NativeW * NativeH];

        void FillTile(int px, int py, int c)
        {
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) native[(py + y) * NativeW + px + x] = c;
        }

        void DrawSprites()
        {
            var ram = m.Ram;
            var roms = m.Roms;
            int o1 = 0x0b80, o2 = 0x1380, o3 = 0x1b80;
            for (int offs = 0; offs < 0x80; offs += 2)
            {
                int sprite = ram[o1 + offs];
                int color = ram[o1 + offs + 1] & 0x3f;
                // the 8-bit position wraps (x_ram 0..39 -> 216..255); the high bit(s) add 256 per step
                int sx = ((ram[o2 + offs + 1] - 40) & 0xff) + 0x100 * (ram[o3 + offs + 1] & 3);
                int sy = 256 - ram[o2 + offs] + 1;
                int flipx = ram[o3 + offs] & 1;
                int flipy = (ram[o3 + offs] >> 1) & 1;
                int size = (sprite & 0x80) != 0 ? 1 : 0;
                int[,] gfxOffs = { { 0, 1 }, { 2, 3 } };
                if (size != 0) sprite = (sprite & 0xc0) | ((sprite & ~0xc0) << 2);
                sy -= 16 * size;
                sy = (sy & 0xff) - 32;
                for (int yy = 0; yy <= size; yy++)
                    for (int xx = 0; xx <= size; xx++)
                    {
                        int code = (sprite + gfxOffs[yy ^ (size * flipy), xx ^ (size * flipx)]) & 0xff;
                        DrawSprite(code, color, flipx != 0, flipy != 0, sx + 16 * xx, sy + 16 * yy);
                    }
            }
        }

        void DrawSprite(int code, int color, bool fx, bool fy, int sx, int sy)
        {
            var lut = m.Roms.SpriteLut;
            for (int y = 0; y < 16; y++)
            {
                int dy = sy + y; if (dy < 0 || dy >= NativeH) continue;
                int srcY = fy ? 15 - y : y;
                for (int x = 0; x < 16; x++)
                {
                    int dx = sx + x; if (dx < 0 || dx >= NativeW) continue;
                    int srcX = fx ? 15 - x : x;
                    int pen = SpritePix[code * 256 + srcY * 16 + srcX];
                    int idx = lut[((color << 2) | pen) & 0xff] & 0x0f;
                    if (idx == 0x0f) continue;
                    native[dy * NativeW + dx] = Pal[idx | 0x10];
                }
            }
        }
    }
}

