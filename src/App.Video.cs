// Front end - presenting the picture: scaling modes, rotation, widescreen side panels, CRT overlays, HUD, screenshots.
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DigDug
{
    public sealed unsafe partial class App
    {
        public static readonly string[] PanelNames = { "DIRT", "BLACK", "GLOW", "SCOPE", "INFO" };
        const int MaxWide = 1280;

        int texW = W;
        int[] wideBuf = new int[0];
        IntPtr overlayTex;
        string overlayKey;
        int overlayW, overlayH;
        bool shotClean;                 // screenshots leave out the on-screen notices (menus are kept)

        // fps counter
        double fpsStamp; int fpsCount, fpsValue;

        void RenderBlank()
        {
            Sdl.SDL_SetRenderDrawColor(ren, 0, 0, 0, 255);
            Sdl.SDL_RenderClear(ren);
            Sdl.SDL_RenderPresent(ren);
        }

        void ApplyVideoOptions()
        {
            if (machine != null) machine.Video.WantPfOnly = cfg.Widescreen && (cfg.SidePanels == 0);
            overlayKey = null;
        }

        // ================================================================== the 224x288 source image
        // The game frame, or the dimmed game with the menu drawn over it; on-screen notices (fps, toasts, ...) are drawn on top.
        int[] BuildSource()
        {
            int[] src = machine.Video.Pixels;
            bool hud = !shotClean && HudActive();
            if (menuOpen)
            {
                for (int i = 0; i < src.Length; i++) { int c = src[i]; frameBuf[i] = (int)(0xff000000 | (uint)((c >> 3) & 0x1f1f1f)); }
                DrawMenu();
                src = frameBuf;
            }
            else if (hud) { Array.Copy(src, frameBuf, src.Length); src = frameBuf; }
            if (hud) DrawHud();
            return src;
        }

        // ================================================================== widescreen
        // Builds the wide frame: the 224x288 game in the middle and decorated side panels. A bright line marks the real playfield edge.
        int[] ComposeWide(int[] src, int tw)
        {
            if (wideBuf.Length != tw * H) wideBuf = new int[tw * H];
            int[] pf = machine.Video.PfOnly;
            int x0 = (tw - W) / 2, pw = x0;
            int style = cfg.SidePanels;
            int black = unchecked((int)0xff000000);

            // glow: per-row average of the picture's outermost columns, blurred vertically
            int[] gl = null, gr = null;
            if (style == 2)
            {
                gl = new int[H]; gr = new int[H];
                for (int y = 0; y < H; y++)
                {
                    int rl = 0, gg = 0, bl = 0, rr = 0, g2 = 0, b2 = 0;
                    for (int k = 0; k < 6; k++)
                    {
                        int c = src[y * W + k]; rl += (c >> 16) & 255; gg += (c >> 8) & 255; bl += c & 255;
                        c = src[y * W + W - 1 - k]; rr += (c >> 16) & 255; g2 += (c >> 8) & 255; b2 += c & 255;
                    }
                    gl[y] = (rl / 6 << 16) | (gg / 6 << 8) | bl / 6; gr[y] = (rr / 6 << 16) | (g2 / 6 << 8) | b2 / 6;
                }
                for (int pass = 0; pass < 2; pass++)
                    foreach (var arr in new[] { gl, gr })
                    {
                        var t = (int[])arr.Clone();
                        for (int y = 0; y < H; y++)
                        {
                            int r = 0, g = 0, b = 0, n = 0;
                            for (int k = -8; k <= 8; k++) { int yy = y + k; if (yy < 0 || yy >= H) continue; r += (t[yy] >> 16) & 255; g += (t[yy] >> 8) & 255; b += t[yy] & 255; n++; }
                            arr[y] = (r / n << 16) | (g / n << 8) | b / n;
                        }
                    }
            }

            for (int y = 0; y < H; y++)
            {
                int row = y * tw;
                for (int x = 0; x < tw; x++)
                {
                    int cx = x - x0;
                    if (cx >= 0 && cx < W) { wideBuf[row + x] = src[y * W + cx]; continue; }
                    int dd = cx < 0 ? -cx - 1 : cx - W;
                    int v;
                    switch (style)
                    {
                        case 0:
                            {
                                int mm = dd % (2 * W); int off = mm < W ? mm : 2 * W - 1 - mm;
                                int col = cx < 0 ? off : W - 1 - off;
                                int c = pf[y * W + col];
                                v = (int)(0xff000000 | (uint)((c >> 1) & 0x7f7f7f));
                                if (y >= 264) v = black;                               // keep the status strip (lives icons) out of the extension
                                break;
                            }
                        case 2:
                            {
                                int c = cx < 0 ? gl[y] : gr[y];
                                double f = 0.7 * (1.0 - Math.Min(1.0, dd / (double)Math.Max(1, pw)) * 0.85);
                                v = unchecked((int)0xff000000) | ((int)(((c >> 16) & 255) * f) << 16) | ((int)(((c >> 8) & 255) * f) << 8) | (int)((c & 255) * f);
                                break;
                            }
                        default: v = black; break;
                    }
                    if (dd == 0) v = unchecked((int)0xffd0d0d0);
                    else if (dd == 1 && style != 2) v = black;
                    wideBuf[row + x] = v;
                }
            }

            if (style == 3 && pw > 16) DrawScopes(tw, x0, pw);
            else if (style == 4 && pw > 40) DrawInfoPanels(tw, x0, pw);
            return wideBuf;
        }

        void DrawScopes(int tw, int x0, int pw)
        {
            int[] cols = { unchecked((int)0xff60ff60), unchecked((int)0xffffd800), unchecked((int)0xff60c0ff) };
            for (int side = 0; side < 2; side++)
            {
                int px = side == 0 ? 0 : x0 + W;
                for (int v = 0; v < 3; v++)
                {
                    int cy = 48 + v * 96;
                    var sc = machine.Sound.Scope[v];
                    int prev = cy;
                    // faint centre line
                    for (int x = 4; x < pw - 4; x++) if (cy * tw + px + x < wideBuf.Length) wideBuf[cy * tw + px + x] = unchecked((int)0xff203020);
                    for (int x = 4; x < pw - 4; x++)
                    {
                        int s = sc[(x - 4) * Sound.ScopeLen / Math.Max(1, pw - 8)];
                        int y = cy - s * 36 / 9600;
                        y = Math.Max(2, Math.Min(H - 3, y));
                        int lo = Math.Min(prev, y), hi = Math.Max(prev, y);
                        for (int yy = lo; yy <= hi; yy++) wideBuf[yy * tw + px + x] = cols[v];
                        prev = y;
                    }
                    string lab = "VOICE " + (v + 1);
                    Font5x7.Draw(wideBuf, tw, H, lab, px + 6, cy - 44, (machine.Sound.MuteMask & (1 << v)) != 0 ? unchecked((int)0xff606060) : cols[v], 1);
                }
            }
        }

        void DrawInfoPanels(int tw, int x0, int pw)
        {
            int gold = unchecked((int)0xffffd800), white = unchecked((int)0xffe0e0ff), dim = unchecked((int)0xff8890b0);
            Action<string, int, int, int> T = (s, x, y, c) => { if (s.Length * 6 > pw - 8) s = s.Substring(0, Math.Max(0, (pw - 8) / 6)); Font5x7.Draw(wideBuf, tw, H, s, x, y, c, 1); };
            double secs = stats.PlaySeconds;
            string time = ((int)(secs / 3600)).ToString("00") + ":" + ((int)(secs / 60) % 60).ToString("00") + ":" + ((int)secs % 60).ToString("00");
            int y0 = 24;
            T("DIG DUG", 8, y0, gold);
            T("STATS", 8, y0 + 22, dim);
            T("TIME " + time, 8, y0 + 36, white);
            T("GAMES " + stats.Games, 8, y0 + 48, white);
            T("DEATHS " + stats.Deaths, 8, y0 + 60, white);
            T("BEST " + stats.BestScore, 8, y0 + 72, white);
            T("MAX ROUND " + stats.MaxRound, 8, y0 + 84, white);
            T("MEDALS " + stats.UnlockedCount + "/" + Achievements.All.Length, 8, y0 + 96, gold);
            int rx = x0 + W + 8;
            T("KEYS", rx, y0, gold);
            string[] k = { "ESC  MENU", "P    PAUSE", "F5   SAVE", "F7   LOAD", "F6   SLOT", "R    REWIND", "TAB  FAST", "BRKT SPEED", "F10  THEME", "F8   SCAN", "F11  FULL", "F12  PHOTO" };
            for (int i = 0; i < k.Length; i++) T(k[i], rx, y0 + 18 + i * 12, white);
            T("CHEATS " + (cfg.CheatLives || cfg.CheatInvincible || cfg.CheatRound > 1 ? "ON" : "OFF"), rx, y0 + 18 + k.Length * 12 + 10, dim);
        }

        // ================================================================== presenting
        void Present(int[] fixedSrc = null)
        {
            int ow, oh; Sdl.SDL_GetRendererOutputSize(ren, out ow, out oh);
            DrawScene(ow, oh, fixedSrc, true);
            Sdl.SDL_RenderPresent(ren);
            fpsCount++;
            double t = sw.Elapsed.TotalSeconds;
            if (t - fpsStamp >= 1.0) { fpsValue = (int)(fpsCount / (t - fpsStamp) + 0.5); fpsCount = 0; fpsStamp = t; }
        }

        // Draws the whole picture into the current render target (the window, or an offscreen texture for screenshots).
        void DrawScene(int ow, int oh, int[] fixedSrc, bool allowResize)
        {
            int[] src = fixedSrc ?? BuildSource();
            bool rot = (cfg.Rotation & 1) != 0 && fixedSrc == null;

            // widescreen: extend the level sideways so wide screens are not left with black bars
            int tw = W;
            double ratio = oh > 0 ? (double)ow / oh : 0;
            if (fixedSrc == null && cfg.Widescreen && !rot && cfg.Scaling != ScaleMode.Stretch && ratio > (double)W / H + 0.02)
            { tw = Math.Min(MaxWide, (int)Math.Ceiling(H * ratio)); tw += tw & 1; if (tw < W) tw = W; }
            if (tw != texW) { texW = tw; MakeTexture(); }
            if (tw > W) src = ComposeWide(src, tw);
            fixed (int* p = src) Sdl.SDL_UpdateTexture(tex, IntPtr.Zero, (IntPtr)p, tw * 4);

            // size of the image as it appears on screen (after rotation) and its placement
            int dw = rot ? H : tw, dh = rot ? tw : H;
            SdlRect box;
            if (cfg.Scaling == ScaleMode.Stretch) box = new SdlRect { W = ow, H = oh };
            else if (cfg.Scaling == ScaleMode.Sharp && ow >= dw && oh >= dh)
            {
                int s = Math.Max(1, Math.Min(ow / dw, oh / dh));
                box = new SdlRect { W = dw * s, H = dh * s };
            }
            else
            {
                double s = Math.Min((double)ow / dw, (double)oh / dh);
                box = new SdlRect { W = Math.Max(1, (int)(dw * s)), H = Math.Max(1, (int)(dh * s)) };
            }
            box.X = (ow - box.W) / 2; box.Y = (oh - box.H) / 2;

            Sdl.SDL_SetRenderDrawColor(ren, 0, 0, 0, 255);
            Sdl.SDL_RenderClear(ren);
            if (fixedSrc == null && cfg.Rotation != 0)
            {
                // RenderCopyEx rotates the destination rectangle about its centre
                SdlRect d;
                if (rot) d = new SdlRect { W = box.H, H = box.W, X = box.X + (box.W - box.H) / 2, Y = box.Y + (box.H - box.W) / 2 };
                else d = box;
                Sdl.SDL_RenderCopyEx(ren, tex, IntPtr.Zero, ref d, cfg.Rotation * 90.0, IntPtr.Zero, 0);
            }
            else Sdl.SDL_RenderCopy(ren, tex, IntPtr.Zero, ref box);

            if (fixedSrc == null) DrawOverlay(ow, oh, box, rot, tw);
        }

        // ---- CRT overlay: scanlines, phosphor mask and vignette, generated at output resolution and cached
        void DrawOverlay(int ow, int oh, SdlRect box, bool rot, int tw)
        {
            if (cfg.Scanlines == 0 && !cfg.CrtMask && !cfg.Vignette) return;
            string key = ow + "x" + oh + "/" + box.X + "," + box.Y + "," + box.W + "," + box.H + "/" + cfg.Scanlines + cfg.AuthenticScan + cfg.CrtMask + cfg.Vignette + rot + cfg.Rotation + tw;
            if (key != overlayKey || overlayTex == IntPtr.Zero || overlayW != ow || overlayH != oh)
            {
                if (overlayTex != IntPtr.Zero) Sdl.SDL_DestroyTexture(overlayTex);
                overlayTex = Sdl.SDL_CreateTexture(ren, Sdl.PixelFormatArgb8888, Sdl.TextureAccessStreaming, ow, oh);
                Sdl.SDL_SetTextureBlendMode(overlayTex, Sdl.BlendModeBlend);
                overlayW = ow; overlayH = oh; overlayKey = key;
                BuildOverlay(ow, oh, box, rot, tw);
            }
            var full = new SdlRect { W = ow, H = oh };
            Sdl.SDL_RenderCopy(ren, overlayTex, IntPtr.Zero, ref full);
        }

        void BuildOverlay(int ow, int oh, SdlRect box, bool rot, int tw)
        {
            var px = new int[ow * oh];
            int sl = cfg.Scanlines;
            double strength = sl == 1 ? 0.28 : sl == 2 ? 0.45 : 0.65;
            // size of one game pixel on screen along each display axis
            double gx = box.W / (double)(rot ? H : tw), gy = box.H / (double)(rot ? tw : H);
            // which display axis the scanlines run across: normal = the picture's rows; authentic = the rotated monitor's real scan
            bool bandsOnY = cfg.AuthenticScan ? rot : !rot;
            double period = bandsOnY ? gy : gx;
            double cx = ow / 2.0, cy = oh / 2.0, rx = box.W / 2.0, ry = box.H / 2.0;
            for (int y = 0; y < oh; y++)
            {
                for (int x = 0; x < ow; x++)
                {
                    int ia = 0;                 // alpha of black
                    int tr = 0, tg = 0, tb = 0, ta = 0;
                    if (x >= box.X && x < box.X + box.W && y >= box.Y && y < box.Y + box.H)
                    {
                        if (sl > 0 && period >= 1.5)
                        {
                            double pos = (bandsOnY ? (y - box.Y) : (x - box.X)) / period;
                            double frac = pos - Math.Floor(pos);
                            double lum = Math.Sin(Math.PI * frac);
                            ia = (int)(255 * strength * (1.0 - Math.Pow(lum, 0.55)));
                        }
                        if (cfg.CrtMask)
                        {
                            switch (x % 3) { case 0: tr = 255; tg = 40; tb = 40; break; case 1: tr = 40; tg = 255; tb = 40; break; default: tr = 40; tg = 40; tb = 255; break; }
                            ta = 38;
                        }
                        if (cfg.Vignette)
                        {
                            double dx = (x - cx) / Math.Max(1, rx), dy = (y - cy) / Math.Max(1, ry);
                            double d = Math.Sqrt(dx * dx + dy * dy);
                            if (d > 0.55) ia = Math.Max(ia, (int)(Math.Min(1.0, (d - 0.55) / 0.75) * 190));
                        }
                    }
                    // combine: mask tint drawn with its alpha, darkening on top
                    if (ia == 0 && ta == 0) continue;
                    int a, r, g, b;
                    if (ta == 0) { a = ia; r = g = b = 0; }
                    else if (ia == 0) { a = ta; r = tr; g = tg; b = tb; }
                    else
                    {
                        // composite black(ia) over tint(ta)
                        double fa = ia / 255.0, ba = ta / 255.0;
                        double oa = fa + ba * (1 - fa);
                        a = (int)(oa * 255); r = (int)(tr * ba * (1 - fa) / oa); g = (int)(tg * ba * (1 - fa) / oa); b = (int)(tb * ba * (1 - fa) / oa);
                    }
                    px[y * ow + x] = (a << 24) | (r << 16) | (g << 8) | b;
                }
            }
            fixed (int* p = px) Sdl.SDL_UpdateTexture(overlayTex, IntPtr.Zero, (IntPtr)p, ow * 4);
        }

        // ================================================================== on-screen notices
        bool HudActive()
        {
            double nowT = sw.Elapsed.TotalSeconds;
            toasts.RemoveAll(t => t.Until < nowT);
            return cfg.ShowFps || toasts.Count > 0 || recording || replayPlaying || paused || (machine != null && CanRewindKey() && !menuOpen)
                || cfg.GameSpeed != 100 || KeyDown(ScTab) || capAct >= 0;
        }

        void DrawHud()
        {
            int white = unchecked((int)0xffffffff), gold = unchecked((int)0xffffd800), red = unchecked((int)0xffff5050), green = unchecked((int)0xff80ff80);
            if (cfg.ShowFps) { string s = fpsValue + " FPS"; Font5x7.Draw(frameBuf, W, H, s, W - Font5x7.Width(s, 1) - 2, 2, green, 1); }
            int x = 2;
            Action<string, int> tag = (s, c) => { Font5x7.Draw(frameBuf, W, H, s, x, 2, c, 1); x += Font5x7.Width(s, 1) + 6; };
            if (recording) tag("REC", (animTick++ / 20) % 2 == 0 ? red : unchecked((int)0xff802020));
            if (replayPlaying) tag("REPLAY", gold);
            if (paused) tag("PAUSED", gold);
            if (!menuOpen && CanRewindKey() && CanRewind()) tag("<< REWIND", gold);
            else if (KeyDown(ScTab)) tag("FAST >>", gold);
            if (cfg.GameSpeed != 100) tag(cfg.GameSpeed + "%", gold);

            int y = H - 22;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                int w = Font5x7.Width(t.Text, 1) + 10;
                int bx = (W - w) / 2;
                int bg = t.Gold ? unchecked((int)0xff3a2c00) : unchecked((int)0xff101830);
                for (int yy = y - 3; yy < y + 11; yy++)
                    for (int xx = bx; xx < bx + w; xx++)
                        if (xx >= 0 && xx < W && yy >= 0 && yy < H) frameBuf[yy * W + xx] = bg;
                int fg = t.Gold ? gold : white;
                for (int xx = bx; xx < bx + w; xx++) { frameBuf[(y - 3) * W + xx] = fg; frameBuf[(y + 10) * W + xx] = fg; }
                Font5x7.Draw(frameBuf, W, H, t.Text, bx + 5, y, fg, 1);
                y -= 18;
            }
        }

        // ================================================================== screenshots
        // Renders the scene at the requested size (default: the window) into an offscreen target and writes a PNG.
        void TakeScreenshot(string file, int w, int h, bool clean = false)
        {
            int ow, oh; Sdl.SDL_GetRendererOutputSize(ren, out ow, out oh);
            if (w <= 0 || h <= 0) { w = ow; h = oh; }
            IntPtr target = Sdl.SDL_CreateTexture(ren, Sdl.PixelFormatArgb8888, Sdl.TextureAccessTarget, w, h);
            if (target == IntPtr.Zero) { Log("screenshot target failed: " + Sdl.Error()); return; }
            shotClean = clean;
            Sdl.SDL_SetRenderTarget(ren, target);
            DrawScene(w, h, null, false);
            var px = new int[w * h];
            fixed (int* p = px) Sdl.SDL_RenderReadPixels(ren, IntPtr.Zero, Sdl.PixelFormatArgb8888, (IntPtr)p, w * 4);
            Sdl.SDL_SetRenderTarget(ren, IntPtr.Zero);
            Sdl.SDL_DestroyTexture(target);
            shotClean = false;
            overlayKey = null;      // the cached overlay was built for the screenshot size
            for (int i = 0; i < px.Length; i++) px[i] |= unchecked((int)0xff000000);
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(file));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                Png.Write(file, px, w, h, 1);
            }
            catch (Exception ex) { Log("screenshot failed: " + ex.Message); }
        }

        void TakeScreenshotToFile()
        {
            if (machine == null) return;
            string dir = Settings.SubDir("screenshots");
            string file = Path.Combine(dir, "digdug_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            TakeScreenshot(file, 0, 0, true);
            Toast("SCREENSHOT SAVED");
            Unlock(Achievements.Photographer);
            Log("screenshot " + file);
        }
    }
}
