// Front end - the hierarchical menu (drawn with the game's own character set).
using System;
using System.Collections.Generic;

namespace DigDug
{
    public sealed unsafe partial class App
    {
        readonly List<MPage> stack = new List<MPage>();
        string confirm;                 // label of a destructive item that is waiting for a second press
        const int MenuRows = 14, MenuTop = 44, MenuRowH = 14;
        const string Version = "1.1.0";

        MPage CurPage { get { return stack.Count > 0 ? stack[stack.Count - 1] : null; } }

        // ================================================================== opening / closing
        void OpenMainMenu() { stack.Clear(); Push(PageMain()); }
        void OpenMenu() { menuOpen = true; menuPausesGame = true; OpenMainMenu(); }
        void CloseMenu() { menuOpen = false; confirm = null; cfg.Save(); ApplyVideoOptions(); }

        void OpenMenuPage(string name)
        {
            OpenMainMenu();
            switch (name)
            {
                case "video": Push(PageVideo()); break;
                case "audio": Push(PageAudio()); break;
                case "controls": Push(PageControls()); break;
                case "game": Push(PageGame()); break;
                case "cheats": Push(PageCheats()); break;
                case "states": Push(PageStates()); break;
                case "extras": Push(PageExtras()); break;
                case "stats": Push(PageStats()); break;
                case "achievements": Push(PageStats()); Push(PageAchievements()); break;
                case "about": Push(PageAbout()); break;
            }
        }

        void Push(MPage p) { p.Refresh(); stack.Add(p); confirm = null; }
        void RefreshMenu() { if (CurPage != null) CurPage.Refresh(); }

        // ================================================================== input
        void MenuMove(int d) { var p = CurPage; p.Move(d); confirm = null; }

        void MenuAdjust(int d)
        {
            var it = Sel();
            if (it == null) return;
            if (it.Adjust != null) { it.Adjust(d); RefreshMenu(); }
        }

        void MenuActivate()
        {
            var it = Sel();
            if (it == null) return;
            if (it.Activate != null) it.Activate();
            else if (it.Adjust != null) it.Adjust(1);
            RefreshMenu();
        }

        void MenuBack()
        {
            if (stack.Count > 1) { stack.RemoveAt(stack.Count - 1); confirm = null; RefreshMenu(); }
            else if (menuPausesGame || machine.FrameCount > BootFrames) CloseMenu();
        }

        MItem Sel()
        {
            var p = CurPage;
            return p != null && p.Sel >= 0 && p.Sel < p.Items.Count && p.Items[p.Sel].Selectable ? p.Items[p.Sel] : null;
        }

        // ================================================================== item helpers
        static MItem Head(string label, Func<string> value = null) { return new MItem { Label = label, Header = true, Value = value }; }
        static MItem Btn(string label, Action act, string hint = null, Func<string> value = null) { return new MItem { Label = label, Activate = act, Hint = hint, Value = value }; }

        static MItem Tog(string label, Func<bool> get, Action<bool> set, string hint = null)
        {
            return new MItem { Label = label, Value = () => get() ? "ON" : "OFF", Adjust = d => set(!get()), Hint = hint };
        }

        static MItem Cho(string label, Func<string> val, Action<int> adj, string hint = null)
        {
            return new MItem { Label = label, Value = val, Adjust = adj, Hint = hint };
        }

        MItem Sub(string label, Func<MPage> page, string hint = null)
        {
            return new MItem { Label = label, Activate = () => Push(page()), Hint = hint };
        }

        // destructive item: the first press asks, the second does it
        MItem Danger(string label, Action act, string hint)
        {
            return new MItem
            {
                Label = label, Hint = hint,
                Value = () => confirm == label ? "SURE?" : "",
                Activate = () => { if (confirm == label) { confirm = null; act(); } else confirm = label; }
            };
        }

        static int Wrap(int v, int d, int n) { return ((v + d) % n + n) % n; }
        static int Step(int v, int d, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v + d)); }

        // ================================================================== pages
        MPage PageMain()
        {
            return new MPage
            {
                Title = "DIG DUG",
                Build = () => new List<MItem>
                {
                    Btn(menuPausesGame ? "RESUME" : "PLAY", CloseMenu, "CLOSE THE MENU"),
                    Sub("VIDEO", PageVideo, "WINDOW|SCALING|THEMES|CRT FILTERS"),
                    Sub("AUDIO", PageAudio, "VOLUME|OUTPUT DEVICE|VOICES"),
                    Sub("CONTROLS", PageControls, "REBIND KEYS|CONTROLLER OPTIONS"),
                    Sub("GAME", PageGame, "DIFFICULTY|SPEED|CREDITS"),
                    Sub("CHEATS", PageCheats, "INFINITE LIVES|INVINCIBLE|START ROUND"),
                    Sub("SAVE STATES", PageStates, "SAVE AND LOAD|REWIND|REPLAYS"),
                    Sub("EXTRAS", PageExtras, "SCREENSHOT|FOLDERS|RESET"),
                    Sub("STATS", PageStats, "PLAY STATS|ACHIEVEMENTS"),
                    Sub("ABOUT", PageAbout),
                    Btn("RESET GAME", () => { ResetGame(); menuPausesGame = false; }, "RESTART THE ARCADE BOARD"),
                    Btn("QUIT", () => running = false)
                }
            };
        }

        MPage PageVideo()
        {
            return new MPage
            {
                Title = "VIDEO",
                Build = () => new List<MItem>
                {
                    Tog("FULLSCREEN", () => cfg.Fullscreen, v => ToggleFullscreen(), "ALSO F11"),
                    Cho("WINDOW SIZE", () => cfg.WindowScale == 0 ? "AUTO" : cfg.WindowScale + "X",
                        d => { cfg.WindowScale = Wrap(cfg.WindowScale, d, 9); if (!cfg.Fullscreen) ApplyWindowSize(); }),
                    Cho("SCALING", () => cfg.Scaling == ScaleMode.Sharp ? "SHARP" : cfg.Scaling == ScaleMode.Fit ? "FIT" : "STRETCH",
                        d => cfg.Scaling = (ScaleMode)Wrap((int)cfg.Scaling, d, 3), "SHARP = WHOLE PIXELS|FIT = KEEP ASPECT|STRETCH = FILL WINDOW"),
                    Tog("SMOOTH FILTER", () => cfg.Smooth, v => { cfg.Smooth = v; MakeTexture(); }, "BILINEAR INSTEAD OF PIXELS"),
                    Tog("WIDESCREEN", () => cfg.Widescreen, v => { cfg.Widescreen = v; ApplyVideoOptions(); }, "FILL WIDE SCREENS|BRIGHT LINE = REAL EDGE"),
                    Cho("SIDE PANELS", () => PanelNames[cfg.SidePanels], d => { cfg.SidePanels = Wrap(cfg.SidePanels, d, PanelNames.Length); ApplyVideoOptions(); },
                        "DIRT BLACK GLOW|SCOPE = LIVE WAVEFORMS|INFO = STATS AND KEYS"),
                    Cho("ROTATION", () => cfg.Rotation == 0 ? "NORMAL" : cfg.Rotation * 90 + " DEG",
                        d => { cfg.Rotation = Wrap(cfg.Rotation, d, 4); if (!cfg.Fullscreen) ApplyWindowSize(); }, "TILT YOUR MONITOR|90 = LANDSCAPE"),
                    Cho("THEME", () => Themes.Names[cfg.Theme], d =>
                        { cfg.Theme = Wrap(cfg.Theme, d, Themes.Count); machine.Video.SetTheme(cfg.Theme); Unlock(Achievements.Stylish); },
                        "COLOUR PALETTES|INCLUDES COLOURBLIND MODES|ALSO F10"),
                    Cho("SCANLINES", () => new[] { "OFF", "LIGHT", "MEDIUM", "HEAVY" }[cfg.Scanlines], d => cfg.Scanlines = Wrap(cfg.Scanlines, d, 4), "ALSO F8"),
                    Cho("SCAN DIRECTION", () => cfg.AuthenticScan ? "ARCADE" : "NORMAL", d => cfg.AuthenticScan = !cfg.AuthenticScan,
                        "ARCADE = LINES RUN SIDEWAYS|AS ON THE REAL TILTED TUBE"),
                    Tog("CRT MASK", () => cfg.CrtMask, v => cfg.CrtMask = v, "RGB PHOSPHOR DOTS"),
                    Tog("VIGNETTE", () => cfg.Vignette, v => cfg.Vignette = v, "DARK CORNERS"),
                    Tog("VSYNC", () => cfg.VSync, v => { cfg.VSync = v; ApplyWindowFlags(); }),
                    Tog("SHOW FPS", () => cfg.ShowFps, v => cfg.ShowFps = v, "ALSO F9"),
                    Tog("ALWAYS ON TOP", () => cfg.AlwaysOnTop, v => { cfg.AlwaysOnTop = v; ApplyWindowFlags(); }),
                    Tog("BORDERLESS", () => cfg.Borderless, v => { cfg.Borderless = v; ApplyWindowFlags(); }, "HIDE THE WINDOW FRAME"),
                    Btn("BACK", MenuBack)
                }
            };
        }

        MPage PageAudio()
        {
            return new MPage
            {
                Title = "AUDIO",
                Build = () => new List<MItem>
                {
                    Cho("VOLUME", () => cfg.Volume + "", d => cfg.Volume = Step(cfg.Volume, d * 10, 0, 100)),
                    Cho("DEVICE", AudioLabel, d => CycleAudioDevice(d), "PICK THE OUTPUT DEVICE|IF YOU HEAR NOTHING"),
                    Btn("TEST SOUND", PlayTestTone),
                    Head("VOICES"),
                    Tog("VOICE 1", () => (cfg.MuteMask & 1) == 0, v => SetMute(0, v), "SILENCE ONE OF THE 3 CHANNELS"),
                    Tog("VOICE 2", () => (cfg.MuteMask & 2) == 0, v => SetMute(1, v)),
                    Tog("VOICE 3", () => (cfg.MuteMask & 4) == 0, v => SetMute(2, v)),
                    Tog("SMOOTH SOUND", () => cfg.SoundSmooth, v => { cfg.SoundSmooth = v; machine.Sound.Smooth = v; }, "SOFTER HIGH NOTES"),
                    Tog("MUTE IN BG", () => cfg.MuteInBackground, v => cfg.MuteInBackground = v, "SILENT WHEN NOT FOCUSED"),
                    Btn("BACK", MenuBack)
                }
            };
        }

        void SetMute(int voice, bool on)
        {
            if (on) cfg.MuteMask &= ~(1 << voice); else cfg.MuteMask |= 1 << voice;
            machine.Sound.MuteMask = cfg.MuteMask;
        }

        string KeyLabel(int a)
        {
            if (capAct == a) return "PRESS KEY";
            var k = cfg.Keys[a];
            string s0 = Trunc(ArcadeText.Clean(Sdl.ScancodeName(k[0])), 8);
            string s1 = k[1] > 0 ? Trunc(ArcadeText.Clean(Sdl.ScancodeName(k[1])), 6) : "";
            return s1.Length > 0 ? s0 + "/" + s1 : s0;
        }

        static string Trunc(string s, int n) { return s.Length > n ? s.Substring(0, n) : s; }

        MPage PageControls()
        {
            return new MPage
            {
                Title = "CONTROLS",
                Build = () =>
                {
                    var l = new List<MItem> { Head("KEYBOARD") };
                    for (int a = 0; a < 8; a++)
                    {
                        int act = a;
                        l.Add(new MItem
                        {
                            Label = Settings.ActNames[a], Value = () => KeyLabel(act),
                            Activate = () => { capAct = act; capSlot = 0; },
                            Adjust = d => { capAct = act; capSlot = 1; },
                            Hint = "ENTER = NEW KEY 1|LEFT RIGHT = NEW KEY 2|ESC CANCELS BACKSPACE CLEARS"
                        });
                    }
                    l.Add(Btn("RESET KEYS", () => { cfg.ResetKeys(); Toast("KEYS RESET"); }));
                    l.Add(Head("CONTROLLER", () => pads.Count > 0 ? Trunc(ArcadeText.Clean(Sdl.ControllerName(pads[0].Handle)), 12) : "NONE"));
                    l.Add(Cho("PUMP BUTTON", () => new[] { "ANY", "A", "B", "X", "Y", "TRIGGER" }[cfg.PadFire], d => cfg.PadFire = Wrap(cfg.PadFire, d, 6),
                        "WHICH BUTTON PUMPS|ANY = A B X Y OR TRIGGERS"));
                    l.Add(Cho("STICK DEADZONE", () => cfg.Deadzone + "%", d => cfg.Deadzone = Step(cfg.Deadzone, d * 5, 10, 80), "RAISE IF YOUR STICK DRIFTS"));
                    l.Add(Cho("RUMBLE", () => new[] { "OFF", "LOW", "MEDIUM", "STRONG" }[cfg.Rumble], d => { cfg.Rumble = Wrap(cfg.Rumble, d, 4); Rumble(60, 250); }, "BUZZ WHEN YOU LOSE A LIFE"));
                    l.Add(Tog("AUTO PUMP", () => cfg.AutoPump, v => cfg.AutoPump = v, "HOLD PUMP TO KEEP INFLATING"));
                    l.Add(Btn("BACK", MenuBack));
                    return l;
                }
            };
        }

        MPage PageGame()
        {
            return new MPage
            {
                Title = "GAME",
                Build = () => new List<MItem>
                {
                    Cho("LIVES", () => cfg.Lives + "", d => { cfg.Lives = Settings.LivesValues[Wrap(Array.IndexOf(Settings.LivesValues, cfg.Lives), d, 4)]; cfg.ApplyTo(machine); }, "APPLIES TO NEW GAMES"),
                    Cho("BONUS", () => Settings.BonusNames[Array.IndexOf(Settings.BonusValues, cfg.Bonus)],
                        d => { cfg.Bonus = Settings.BonusValues[Wrap(Array.IndexOf(Settings.BonusValues, cfg.Bonus), d, 5)]; cfg.ApplyTo(machine); }, "EXTRA LIFE SCORES"),
                    Cho("RANK", () => ((char)('A' + cfg.Rank)).ToString(), d => { cfg.Rank = Wrap(cfg.Rank, d, 4); cfg.ApplyTo(machine); }, "A = EASIEST  D = HARDEST"),
                    Tog("AUTO COIN", () => cfg.AutoCoin, v => { cfg.AutoCoin = v; machine.Chip51.AutoCoin = v; }, "INSERT A COIN FOR YOU WHEN|YOU PRESS START"),
                    Btn("ADD CREDIT", () => { machine.Chip51.AddCredits(1); Toast("CREDIT ADDED"); }, "LIKE PRESSING 5", () => "CREDITS " + machine.Chip51.Credits),
                    Cho("GAME SPEED", () => cfg.GameSpeed + "%", d => cfg.GameSpeed = Step(cfg.GameSpeed, d * 25, 25, 400), "SLOW MOTION OR TURBO|KEYS [ ] AND \\"),
                    Tog("AUTO PAUSE", () => cfg.PauseOnFocusLoss, v => cfg.PauseOnFocusLoss = v, "PAUSE WHEN YOU SWITCH WINDOWS"),
                    Btn("BACK", MenuBack)
                }
            };
        }

        MPage PageCheats()
        {
            return new MPage
            {
                Title = "CHEATS",
                Build = () => new List<MItem>
                {
                    Head("LIVE HOOKS - NO ROM CHANGES"),
                    Tog("INFINITE LIVES", () => cfg.CheatLives, v => { cfg.CheatLives = v; cfg.ApplyTo(machine); }, "YOU NEVER RUN OUT OF LIVES"),
                    Tog("INVINCIBLE", () => cfg.CheatInvincible, v => { cfg.CheatInvincible = v; cfg.ApplyTo(machine); }, "MONSTERS CANNOT HURT YOU"),
                    Cho("START ROUND", () => cfg.CheatRound + "", d => { cfg.CheatRound = Step(cfg.CheatRound, d, 1, 30); cfg.ApplyTo(machine); }, "NEW GAMES BEGIN AT THIS ROUND"),
                    Btn("TURN ALL OFF", () => { cfg.CheatLives = cfg.CheatInvincible = false; cfg.CheatRound = 1; cfg.ApplyTo(machine); Toast("CHEATS OFF"); }),
                    Head("ACTIVE", () => machine.Cheats.Summary),
                    Btn("BACK", MenuBack)
                }
            };
        }

        MPage PageStates()
        {
            return new MPage
            {
                Title = "SAVE STATES",
                Build = () => new List<MItem>
                {
                    Cho("SLOT", () => stateSlot + "", d => stateSlot = Wrap(stateSlot - 1, d, StateSlots) + 1, "F6 CHANGES SLOT"),
                    Btn("SAVE STATE", () => SaveSlot(stateSlot), "ALSO F5", () => SlotInfo(stateSlot)),
                    Btn("LOAD STATE", () => { LoadSlot(stateSlot); if (menuPausesGame) CloseMenu(); }, "ALSO F7"),
                    Head("REWIND"),
                    Btn("HOLD R OR L3", null, "REWIND UP TO 15 SECONDS"),
                    Head("REPLAYS"),
                    recording ? Btn("STOP RECORDING", StopRecording) : Btn("RECORD REPLAY", () => { StartRecording(); if (recording) CloseMenu(); }, "RECORDS FROM THIS MOMENT"),
                    Btn("PLAY LAST REPLAY", () => { if (PlayLatestReplay()) CloseMenu(); }, "WATCH YOUR LAST RECORDING"),
                    replayPlaying ? Btn("STOP REPLAY", StopReplay) : Head("FILES IN CONFIG REPLAYS"),
                    Btn("BACK", MenuBack)
                }
            };
        }

        MPage PageExtras()
        {
            return new MPage
            {
                Title = "EXTRAS",
                Build = () => new List<MItem>
                {
                    Btn("SCREENSHOT", () => { CloseMenu(); machine.Video.Render(); TakeScreenshotToFile(); }, "SAVES A PNG|ALSO F12"),
                    Btn("OPEN SCREENSHOTS", () => OpenFolder(Settings.SubDir("screenshots"))),
                    Btn("OPEN CONFIG FOLDER", () => OpenFolder(Settings.ConfigDir)),
                    Head("DATA"),
                    Danger("RESET HI SCORES", ResetHighScores, "CLEARS THE SCORE TABLE|PRESS AGAIN TO CONFIRM"),
                    Danger("RESET STATS", () => { stats.Reset(); Toast("STATS RESET"); }, "CLEARS STATS AND MEDALS|PRESS AGAIN TO CONFIRM"),
                    Btn("BACK", MenuBack)
                }
            };
        }

        static string Clock(double secs)
        {
            return ((int)(secs / 3600)).ToString("00") + ":" + ((int)(secs / 60) % 60).ToString("00") + ":" + ((int)secs % 60).ToString("00");
        }

        MPage PageStats()
        {
            return new MPage
            {
                Title = "STATS",
                Build = () => new List<MItem>
                {
                    Head("PLAY TIME", () => Clock(stats.PlaySeconds)),
                    Head("GAMES PLAYED", () => stats.Games + ""),
                    Head("COINS", () => stats.Coins + ""),
                    Head("LIVES LOST", () => stats.Deaths + ""),
                    Head("BEST SCORE", () => stats.BestScore + ""),
                    Head("DEEPEST ROUND", () => stats.MaxRound + ""),
                    Sub("ACHIEVEMENTS", PageAchievements, "WHAT YOU HAVE UNLOCKED"),
                    Head("UNLOCKED", () => stats.UnlockedCount + "/" + Achievements.All.Length),
                    Btn("BACK", MenuBack)
                }
            };
        }

        MPage PageAchievements()
        {
            return new MPage
            {
                Title = "ACHIEVEMENTS",
                Build = () =>
                {
                    var l = new List<MItem>();
                    for (int i = 0; i < Achievements.All.Length; i++)
                    {
                        int id = i;
                        l.Add(new MItem { Label = Achievements.All[i].Name, Value = () => stats.Has(id) ? "DONE" : "-", Activate = () => { }, Hint = Achievements.All[i].Desc });
                    }
                    l.Add(Btn("BACK", MenuBack));
                    return l;
                }
            };
        }

        MPage PageAbout()
        {
            return new MPage
            {
                Title = "ABOUT",
                Build = () => new List<MItem>
                {
                    Head("DIG DUG NATIVE PORT"),
                    Head("VERSION", () => Version),
                    Head(""),
                    Head("AN EMULATOR OF THE 1982"),
                    Head("NAMCO ARCADE BOARD"),
                    Head("ROMS ARE NOT INCLUDED"),
                    Head(""),
                    Head("THIS PROJECT WAS CREATED"),
                    Head("WITH AI. IT MAY CONTAIN"),
                    Head("ERRORS."),
                    Head(""),
                    Btn("BACK", MenuBack)
                }
            };
        }

        // ================================================================== drawing
        void DrawMenu()
        {
            var p = CurPage;
            if (p == null) return;
            var glyphs = machine.Video.CharPix;
            int gold = unchecked((int)0xffffd800), norm = unchecked((int)0xffe0e0ff), sel = unchecked((int)0xffffff40),
                dim = unchecked((int)0xff7078a0), green = unchecked((int)0xff80ff80);
            string title = p.Title;
            ArcadeText.Draw(frameBuf, W, H, glyphs, title, (W - title.Length * 8) / 2, 20, gold);

            // keep the selection on screen
            if (p.Sel < p.Scroll) p.Scroll = p.Sel;
            if (p.Sel >= p.Scroll + MenuRows) p.Scroll = p.Sel - MenuRows + 1;
            if (p.Scroll > Math.Max(0, p.Items.Count - MenuRows)) p.Scroll = Math.Max(0, p.Items.Count - MenuRows);

            for (int r = 0; r < MenuRows; r++)
            {
                int idx = p.Scroll + r;
                if (idx >= p.Items.Count) break;
                var it = p.Items[idx];
                int y = MenuTop + r * MenuRowH;
                bool on = idx == p.Sel;
                string val = it.Value != null ? it.Value() : "";
                if (it.Header)
                {
                    if (it.Label.Length > 0) ArcadeText.Draw(frameBuf, W, H, glyphs, it.Label, 16, y, val.Length > 0 ? dim : gold);
                    if (val.Length > 0) ArcadeText.Draw(frameBuf, W, H, glyphs, val, W - 8 - val.Length * 8, y, norm);
                    continue;
                }
                int col = on ? sel : norm;
                ArcadeText.Draw(frameBuf, W, H, glyphs, it.Label, 16, y, col);
                if (val.Length > 0) ArcadeText.Draw(frameBuf, W, H, glyphs, val, W - 8 - val.Length * 8, y, on ? sel : (it.Adjust != null ? green : norm));
                if (on) Marker(4, y, col);
            }
            if (p.Scroll > 0) Arrow(W - 6, MenuTop - 8, true, dim);
            if (p.Scroll + MenuRows < p.Items.Count) Arrow(W - 6, MenuTop + MenuRows * MenuRowH - 2, false, dim);

            // hint for the selected item
            var cur = Sel();
            string hint = confirm != null ? "PRESS AGAIN TO CONFIRM" : (cur != null && cur.Hint != null ? cur.Hint : "");
            if (capAct >= 0) hint = "PRESS THE NEW KEY|ESC CANCELS";
            var hl = hint.Split('|');
            for (int i = 0; i < hl.Length && i < 3; i++)
                ArcadeText.Draw(frameBuf, W, H, glyphs, hl[i], (W - Math.Min(hl[i].Length, 27) * 8) / 2, 246 + i * 10, green);

            string foot = stack.Count > 1 ? "ESC OR B = BACK" : "ARROWS ENTER OR PAD";
            Font5x7.Draw(frameBuf, W, H, foot, 2, 278, unchecked((int)0xff9098c0), 1);
            if (stack.Count == 1)
            {
                string src = Program.RomSource ?? "";
                if (src.Length > 14) src = ".." + src.Substring(src.Length - 12);
                Font5x7.Draw(frameBuf, W, H, src, W - Font5x7.Width(src, 1) - 2, 278, unchecked((int)0xff9098c0), 1);
            }
        }

        void Marker(int x, int y, int color)
        {
            for (int row = 0; row < 7; row++)
            {
                int w = 4 - Math.Abs(row - 3);
                for (int c = 0; c < w; c++) { int ox = x + c, oy = y + row; if (ox >= 0 && ox < W && oy >= 0 && oy < H) frameBuf[oy * W + ox] = color; }
            }
        }

        void Arrow(int x, int y, bool up, int color)
        {
            for (int r = 0; r < 4; r++)
                for (int c = -r; c <= r; c++)
                {
                    int oy = up ? y + 3 - r : y + r;
                    int ox = x + c;
                    if (ox >= 0 && ox < W && oy >= 0 && oy < H) frameBuf[oy * W + ox] = color;
                }
        }
    }
}
