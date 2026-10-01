// Colour themes: transform the game's palette (no effect on gameplay). Includes accessibility options.
using System;

namespace DigDug
{
    public static class Themes
    {
        public static readonly string[] Names =
        {
            "ORIGINAL", "GRAYSCALE", "SEPIA", "GREEN PHOSPHOR", "AMBER MONITOR", "GAME BOY", "NEGATIVE",
            "VIVID", "COLORBLIND RG", "COLORBLIND BY", "NIGHT MODE", "ULTRAVIOLET", "SUNSET", "ICE"
        };
        public static int Count { get { return Names.Length; } }

        static int C(double v) { return v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5); }

        public static void Apply(int theme, ref int r, ref int g, ref int b)
        {
            double lum = 0.299 * r + 0.587 * g + 0.114 * b;
            switch (theme)
            {
                case 1: r = g = b = C(lum); break;
                case 2: r = C(lum * 1.07); g = C(lum * 0.88); b = C(lum * 0.66); break;
                case 3: r = C(lum * 0.12); g = C(lum * 1.15); b = C(lum * 0.25); break;
                case 4: r = C(lum * 1.2); g = C(lum * 0.78); b = C(lum * 0.08); break;
                case 5:
                    {
                        // four-shade handheld LCD look
                        int q = lum < 40 ? 0 : lum < 110 ? 1 : lum < 180 ? 2 : 3;
                        int[][] sh = { new[] { 15, 56, 15 }, new[] { 48, 98, 48 }, new[] { 139, 172, 15 }, new[] { 155, 188, 15 } };
                        if (lum < 8) { r = 15; g = 56; b = 15; } else { r = sh[q][0]; g = sh[q][1]; b = sh[q][2]; }
                        break;
                    }
                case 6: r = 255 - r; g = 255 - g; b = 255 - b; break;
                case 7:
                    {
                        // boost saturation
                        double nr = lum + (r - lum) * 1.7, ng = lum + (g - lum) * 1.7, nb = lum + (b - lum) * 1.7;
                        r = C(nr); g = C(ng); b = C(nb); break;
                    }
                case 8:
                    {
                        // red/green colour-blind friendly: shift red/green differences into blue/yellow axis
                        double rr = r, gg = g, bb = b;
                        double nr = 0.625 * rr + 0.375 * gg, ng = 0.7 * gg + 0.3 * rr, nb = bb + 0.3 * (rr - gg);
                        r = C(nr); g = C(ng); b = C(nb); break;
                    }
                case 9:
                    {
                        // blue/yellow colour-blind friendly
                        double rr = r, gg = g, bb = b;
                        double nr = rr + 0.25 * (bb - gg), ng = gg + 0.1 * (bb - rr), nb = 0.6 * bb + 0.4 * gg;
                        r = C(nr); g = C(ng); b = C(nb); break;
                    }
                case 10: r = C(r * 0.95); g = C(g * 0.78); b = C(b * 0.45); break;           // reduced blue light
                case 11: { int nr = C(b * 0.7 + r * 0.3), ng = C(g * 0.45), nb = C(r * 0.4 + b * 1.1); r = nr; g = ng; b = nb; break; }
                case 12: { double t = lum / 255.0; r = C(255 * Math.Pow(t, 0.6)); g = C(190 * Math.Pow(t, 1.4)); b = C(120 * Math.Pow(t, 2.4) + 40 * t); break; }
                case 13: { double t = lum / 255.0; r = C(60 + 160 * t * t); g = C(120 + 130 * t); b = C(160 + 95 * t); if (lum < 8) { r = g = b = 0; } break; }
            }
        }
    }
}
