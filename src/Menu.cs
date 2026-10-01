// Menu data model and the arcade-style text renderer (the game's own character ROM plus hand-drawn symbol glyphs).
using System;
using System.Collections.Generic;

namespace DigDug
{
    public sealed class MItem
    {
        public string Label = "";
        public Func<string> Value;          // right-hand value text (live)
        public Action<int> Adjust;          // left/right: -1 / +1
        public Action Activate;             // enter / A
        public bool Header;                 // section label, not selectable
        public Func<bool> Dim;              // greyed out
        public string Hint;                 // shown at the bottom while selected
        public bool Selectable { get { return !Header && (Adjust != null || Activate != null); } }
    }

    public sealed class MPage
    {
        public string Title = "";
        public Func<List<MItem>> Build;
        public List<MItem> Items = new List<MItem>();
        public int Sel, Scroll;
        public void Refresh()
        {
            if (Build != null) Items = Build();
            if (Sel >= Items.Count) Sel = 0;
            if (Sel < Items.Count && !Items[Sel].Selectable) Move(1);
        }
        public void Move(int d)
        {
            if (Items.Count == 0) return;
            for (int i = 0; i < Items.Count; i++)
            {
                Sel = (Sel + d + Items.Count) % Items.Count;
                if (Items[Sel].Selectable) break;
            }
        }
    }

    public static class ArcadeText
    {
        // ROM font code for a character (digits 10-19, letters 1A-33, '.' 34), or -1
        public static int RomCode(char c)
        {
            if (c >= '0' && c <= '9') return 0x10 + (c - '0');
            if (c >= 'A' && c <= 'Z') return 0x1a + (c - 'A');
            if (c == '.') return 0x34;
            return -1;
        }

        static readonly Dictionary<char, string[]> Sym = new Dictionary<char, string[]>();
        static void S(char c, string rows) { Sym[c] = rows.Split('/'); }

        static ArcadeText()
        {
            S('-', "......../......../......../.######./.######./......../......../........");
            S('+', "......../...##.../...##.../.######./.######./...##.../...##.../........");
            S(':', "......../...##.../...##.../......../......../...##.../...##.../........");
            S('/', ".......#/......##/.....##./....##../...##.../..##..../.##...../##......");
            S('%', "##....##/##...##./....##../...##.../..##..../.##...##/##....##/........");
            S('(', "....##../...##.../..##..../..##..../..##..../...##.../....##../........");
            S(')', "..##..../...##.../....##../....##../....##../...##.../..##..../........");
            S('>', "#......./###...../#####.../#######./#####.../###...../#......./........");
            S('<', "......../.....###/...#####/.#######/...#####/.....###/......../........");
            S('!', "...##.../...##.../...##.../...##.../...##.../......../...##.../...##...");
            S('?', ".#####../##...##./.....##./...###../..##..../......../..##..../..##....");
            S('=', "......../.######./.######./......../.######./.######./......../........");
            S('_', "......../......../......../......../......../......../......../########");
            S('*', "...##.../#.####.#/.######./..####../.######./#.####.#/...##.../........");
            S('#', "..#..#../########/..#..#../..#..#../########/..#..#../..#..#../........");
            S(',', "......../......../......../......../......../...##.../...##.../..##....");
            S('\'', "..##..../..##..../.##...../......../......../......../......../........");
            S('[', "..####../..##..../..##..../..##..../..##..../..##..../..####../........");
            S(']', "..####../....##../....##../....##../....##../....##../..####../........");
        }

        public static int Width(string s) { return s.Length * 8; }

        /// <summary>Draws text in screen orientation. The ROM glyphs are stored rotated (native (gx,gy) -> screen (7-gy, gx)).</summary>
        public static void Draw(int[] buf, int bw, int bh, byte[] romGlyphs, string s, int x, int y, int color)
        {
            for (int ci = 0; ci < s.Length; ci++)
            {
                char ch = char.ToUpperInvariant(s[ci]);
                int px = x + ci * 8;
                int g = RomCode(ch);
                if (g >= 0)
                {
                    for (int gy = 0; gy < 8; gy++)
                        for (int gx = 0; gx < 8; gx++)
                            if (romGlyphs[g * 64 + gy * 8 + gx] != 0)
                            {
                                int ox = px + (7 - gy), oy = y + gx;
                                if (ox >= 0 && ox < bw && oy >= 0 && oy < bh) buf[oy * bw + ox] = color;
                            }
                }
                else
                {
                    string[] rows;
                    if (!Sym.TryGetValue(ch, out rows)) continue;
                    for (int r = 0; r < 8; r++)
                        for (int c = 0; c < 8; c++)
                            if (rows[r][c] == '#')
                            {
                                int ox = px + c, oy = y + r;
                                if (ox >= 0 && ox < bw && oy >= 0 && oy < bh) buf[oy * bw + ox] = color;
                            }
                }
            }
        }

        /// <summary>Keeps only characters we can draw (so key names such as "Left Ctrl" stay readable).</summary>
        public static string Clean(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c0 in s)
            {
                char c = char.ToUpperInvariant(c0);
                if (c == ' ' || RomCode(c) >= 0 || Sym.ContainsKey(c)) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
