// Namco Dig Dug (1982) arcade board: 3x Z80, shared RAM, 06xx I/O controller with 51xx/53xx chips (emulated at
// function level), video and 3-voice wavetable sound.
using System;
using System.IO;

namespace DigDug
{
    public sealed class InputState
    {
        public bool Coin1, Coin2, Start1, Start2, Fire, Service;
        public int Dir = -1;   // -1 none, 0 up, 2 right, 4 down, 6 left
    }

    public sealed class Machine
    {
        public const int CpuHz = 3072000;
        public const int Lines = 264, CyclesPerLine = 192;

        public readonly byte[] Ram = new byte[0x2000];       // 0x8000-0x9fff, shared by all CPUs
        public readonly byte[] Earom = new byte[0x40];
        public readonly Z80[] Cpu = new Z80[3];
        readonly CpuBus[] bus = new CpuBus[3];
        public readonly RomSet Roms;
        public readonly InputState Input = new InputState();
        public readonly Namco51 Chip51;
        public readonly Namco53 Chip53;
        public readonly Video Video;
        public readonly Sound Sound;

        // latch at 0x6820
        bool irqMask0, irqMask1, subNmiEnable, subsRunning;
        // video latch at 0xa000
        public int BgSelect, BgColorBank;
        public bool TxColorMode, BgDisable, Flip;

        // 06xx
        int ctrl06;
        bool nmiTimer; int nmiCycles;
        const int Nmi06Period = 614; // ~200us

        // Factory-style defaults: 3 lives, bonus 10K/40K, rank A, upright, 1 coin/1 credit (53xx DIP bytes)
        public int Dip0 = 0xa1, Dip1 = 0x3c;
        public bool Trace06;
        public int HitPc = -1, HitCount;
        public bool[] Cov;
        public int WatchLo, WatchHi; public long WatchFrom;
        public long FrameCount;
        readonly int[] budget = new int[3];

        public Machine(RomSet roms)
        {
            Roms = roms;
            for (int i = 0; i < 3; i++) { bus[i] = new CpuBus(this, i); Cpu[i] = new Z80(bus[i]); }
            Chip51 = new Namco51(this);
            Chip53 = new Namco53(this);
            Video = new Video(this);
            Sound = new Sound(roms.WavProm);
            Reset();
        }

        public void Reset()
        {
            Array.Clear(Ram, 0, Ram.Length);
            for (int i = 0; i < 3; i++) { Cpu[i].Reset(); budget[i] = 0; }
            irqMask0 = irqMask1 = false; subNmiEnable = false; subsRunning = false;
            ctrl06 = 0; nmiTimer = false;
            FrameCount = 0;
            Chip51.Reset();
        }

        // ------------------------------------------------------------------ frame
        public void RunFrame()
        {
            for (int line = 0; line < Lines; line++)
            {
                if (line == 224)
                {
                    if (irqMask0) Cpu[0].IrqLine = true;
                    if (irqMask1) Cpu[1].IrqLine = true;
                }
                if ((line % 132) == 0 && subNmiEnable) Cpu[2].NmiPending = true;

                budget[0] += CyclesPerLine;
                while (budget[0] > 0)
                {
                    if (Cpu[0].PC == HitPc) HitCount++;
                    if (Cov != null) Cov[Cpu[0].PC] = true;
                    int c = Cpu[0].Step();
                    budget[0] -= c;
                    if (nmiTimer) { nmiCycles += c; if (nmiCycles >= Nmi06Period) { nmiCycles -= Nmi06Period; Cpu[0].NmiPending = true; } }
                }
                for (int i = 1; i < 3; i++)
                {
                    if (!subsRunning) { budget[i] = 0; continue; }
                    budget[i] += CyclesPerLine;
                    while (budget[i] > 0) budget[i] -= Cpu[i].Step();
                }
            }
            FrameCount++;
        }

        // ------------------------------------------------------------------ latch / io
        void LatchWrite(int bit, int d)
        {
            d &= 1;
            switch (bit)
            {
                case 0: irqMask0 = d != 0; if (d == 0) Cpu[0].IrqLine = false; break;
                case 1: irqMask1 = d != 0; if (d == 0) Cpu[1].IrqLine = false; break;
                case 2: subNmiEnable = d == 0; break;
                case 3:
                    if (d == 0) subsRunning = false;
                    else if (!subsRunning) { subsRunning = true; Cpu[1].Reset(); Cpu[2].Reset(); budget[1] = budget[2] = 0; }
                    break;
            }
        }

        void VideoLatchWrite(int bit, int d)
        {
            d &= 1;
            switch (bit)
            {
                case 0: BgSelect = (BgSelect & 2) | d; break;
                case 1: BgSelect = (BgSelect & 1) | d << 1; break;
                case 2: TxColorMode = d != 0; break;
                case 3: BgDisable = d != 0; break;
                case 4: BgColorBank = (BgColorBank & 2) | d; break;
                case 5: BgColorBank = (BgColorBank & 1) | d << 1; break;
                case 7: Flip = d != 0; break;
            }
        }

        void Ctrl06Write(int d)
        {
            ctrl06 = d & 0xff;
            if ((d & 0x0f) == 0) { nmiTimer = false; Cpu[0].NmiPending = false; }
            else { if (!nmiTimer) nmiCycles = 0; nmiTimer = true; }
            if ((d & 0x10) != 0) { Chip51.BeginRead(); Chip53.BeginRead(); }
            if (Trace06) Console.WriteLine("06xx ctrl <- " + d.ToString("x2"));
        }

        int Data06Read()
        {
            if ((ctrl06 & 0x10) == 0) return 0xff;
            int r = 0xff;
            if ((ctrl06 & 1) != 0) r &= Chip51.Read();
            if ((ctrl06 & 2) != 0) r &= Chip53.Read();
            if (Trace06) Console.WriteLine("06xx read -> " + r.ToString("x2"));
            return r;
        }

        void Data06Write(int d)
        {
            if ((ctrl06 & 0x10) != 0) return;
            if ((ctrl06 & 1) != 0) Chip51.Write(d);
            if (Trace06) Console.WriteLine("06xx write " + d.ToString("x2") + " (ctrl " + ctrl06.ToString("x2") + ")");
        }

        // ------------------------------------------------------------------ bus
        sealed class CpuBus : IBus
        {
            readonly Machine m; readonly int id; readonly byte[] rom;
            public CpuBus(Machine mm, int i)
            {
                m = mm; id = i;
                rom = i == 0 ? mm.Roms.Main : i == 1 ? mm.Roms.Sub : mm.Roms.Sub2;
            }

            public int Read(int a)
            {
                a &= 0xffff;
                if (a < rom.Length) return rom[a];
                if (a >= 0x8000 && a < 0xa000) return m.Ram[a - 0x8000];
                if (a >= 0x7000 && a < 0x7100) return m.Data06Read();
                if (a == 0x7100) return m.ctrl06;
                if (a >= 0xb800 && a < 0xb840) return m.Earom[a - 0xb800];
                return 0xff;
            }

            public void Write(int a, int v)
            {
                a &= 0xffff; v &= 0xff;
                if (a >= 0x8000 && a < 0xa000)
                {
                    if (m.WatchLo > 0 && a >= m.WatchLo && a <= m.WatchHi && m.FrameCount >= m.WatchFrom && m.FrameCount < m.WatchFrom + 700)
                        Console.WriteLine("f" + m.FrameCount + " cpu" + (id + 1) + " pc=" + m.Cpu[id].PC.ToString("x4") + " ["+a.ToString("x4")+"]=" + v.ToString("x2"));
                    m.Ram[a - 0x8000] = (byte)v; return;
                }
                if (a >= 0x6800 && a < 0x6820) { m.Sound.Write(a - 0x6800, v); return; }
                if (a >= 0x6820 && a < 0x6828) { m.LatchWrite(a - 0x6820, v); return; }
                if (a >= 0x7000 && a < 0x7100) { m.Data06Write(v); return; }
                if (a == 0x7100) { m.Ctrl06Write(v); return; }
                if (a >= 0xa000 && a < 0xa008) { m.VideoLatchWrite(a - 0xa000, v); return; }
                if (a >= 0xb800 && a < 0xb840) { m.Earom[a - 0xb800] = (byte)v; return; }
            }
        }

        // ------------------------------------------------------------------ nvram helpers
        public void LoadEarom(string path) { try { if (File.Exists(path)) { var b = File.ReadAllBytes(path); Array.Copy(b, Earom, Math.Min(b.Length, Earom.Length)); } } catch { } }
        public void SaveEarom(string path) { try { File.WriteAllBytes(path, Earom); } catch { } }
    }

    // ---------------------------------------------------------------------- 51xx (inputs / credits), function-level emulation
    public sealed class Namco51
    {
        readonly Machine m;
        int mode;               // 0 = switch mode (raw inputs), 1 = credit mode
        int coinageLeft; int coinIdx;
        readonly int[] coinage = { 1, 1, 1, 1 };
        int credits;
        readonly int[] coins = new int[2];
        int readIdx, pendingStart, pendingDelay;
        public bool AutoCoin = true;
        bool lastCoin1, lastCoin2, lastStart1, lastStart2, lastFire, fireHeld, fireEdge;

        public Namco51(Machine mm) { m = mm; }

        public void BeginRead() { readIdx = 0; }

        public void Reset()
        {
            mode = 0; coinageLeft = 0; credits = 0; pendingStart = 0; coins[0] = coins[1] = 0;
            lastCoin1 = lastCoin2 = lastStart1 = lastStart2 = lastFire = fireHeld = fireEdge = false;
        }

        public void Write(int d)
        {
            d &= 7;
            if (coinageLeft > 0)
            {
                coinage[coinIdx++] = d;
                coinageLeft--;
                return;
            }
            switch (d)
            {
                case 1: coinageLeft = 4; coinIdx = 0; break;
                case 2: if (mode != 1) { credits = 0; coins[0] = coins[1] = 0; } mode = 1; break;
                case 3: case 4: break;   // joystick remap on/off: the keyboard front end always reports clean 4-way codes
                case 5: mode = 0; break;
            }
        }

        static int ToBcd(int v) { return ((v / 10) << 4) | (v % 10); }

        void UpdateCredits()
        {
            var i = m.Input;
            if (i.Coin1 && !lastCoin1) AddCoin(0);
            if (i.Coin2 && !lastCoin2) AddCoin(1);
            if (pendingStart > 0)
            {
                // auto-coin: let the game see the new credit for a few polls (it runs its "coin inserted"
                // screen transition), as it would with a real coin, then spend it
                if (--pendingDelay <= 0)
                {
                    if (credits >= pendingStart) credits -= pendingStart;
                    pendingStart = 0;
                }
            }
            else
            {
                bool s1 = i.Start1 && !lastStart1, s2 = i.Start2 && !lastStart2;
                if (s2 && credits >= 2) credits -= 2;
                else if (s1 && credits >= 1) credits -= 1;
                else if (AutoCoin && (s1 || s2) && credits < 2)
                {
                    pendingStart = s2 ? 2 : 1; pendingDelay = 8;
                    credits = Math.Max(credits, pendingStart);
                }
            }
            lastCoin1 = i.Coin1; lastCoin2 = i.Coin2; lastStart1 = i.Start1; lastStart2 = i.Start2;
            if (credits > 99) credits = 99;
        }

        void AddCoin(int slot)
        {
            int perCredit = coinage[slot * 2], creditsPer = coinage[slot * 2 + 1];
            if (perCredit == 0) { credits = Math.Min(99, credits + 1); return; }
            coins[slot]++;
            if (coins[slot] >= perCredit) { coins[slot] -= perCredit; credits += Math.Max(1, creditsPer); }
        }

        int JoyByte()
        {
            var i = m.Input;
            int dir = i.Dir < 0 ? 8 : i.Dir;
            int b = dir;
            if (!fireEdge) b |= 0x10;              // bit4: 0 for one poll on a fresh press
            if (fireHeld && !fireEdge) b |= 0x20;  // bit5: button held (clear on the press frame itself, as in the game's own demo data)
            return b;
        }

        public int Read()
        {
            int idx = readIdx++ % 3;
            if (mode == 0)
            {
                var i = m.Input;
                // Switch mode: raw inputs. Bit 7 of the first byte is "not in service/test mode".
                if (idx == 0) return (i.Service ? 0 : 0x80) | (i.Coin1 ? 1 : 0) | (i.Coin2 ? 2 : 0) | (i.Start1 ? 4 : 0) | (i.Start2 ? 8 : 0);
                return 0x0f;
            }
            if (idx == 0)
            {
                // sample the buttons once per transaction so both player bytes agree
                fireHeld = m.Input.Fire;
                fireEdge = fireHeld && !lastFire;
                lastFire = fireHeld;
                UpdateCredits();
                return ToBcd(credits);
            }
            return JoyByte();
        }
    }

    // ---------------------------------------------------------------------- 53xx (DIP switches)
    public sealed class Namco53
    {
        readonly Machine m;
        int idx;
        public Namco53(Machine mm) { m = mm; }
        public void BeginRead() { idx = 0; }
        public int Read() { return (idx++ & 1) == 0 ? m.Dip0 : m.Dip1; }
    }
}



