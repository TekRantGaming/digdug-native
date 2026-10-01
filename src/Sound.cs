// Namco 3-voice wavetable sound generator (as used on Pac-Man / Dig Dug), 96 kHz native, rendered at 48 kHz.
using System;
using System.IO;

namespace DigDug
{
    public sealed class Sound
    {
        public const int SampleRate = 48000;
        public const int ScopeLen = 256;
        readonly int[] regs = new int[0x20];
        readonly int[,] wave = new int[8, 32];
        readonly int[] freq = new int[3], vol = new int[3], sel = new int[3];
        readonly uint[] counter = new uint[3];
        readonly object gate = new object();

        // player-facing options
        public int MuteMask;            // bit n set = voice n silenced
        public bool Smooth;             // gentle low-pass filter (softens the harsh 4-bit steps)
        float lp;

        // oscilloscope data for the on-screen visualiser: the last ScopeLen output samples of each voice
        public readonly short[][] Scope = { new short[ScopeLen], new short[ScopeLen], new short[ScopeLen] };
        int scopePos;
        public int[] Volumes { get { return vol; } }

        public Sound(byte[] wavProm)
        {
            for (int w = 0; w < 8; w++)
                for (int i = 0; i < 32; i++)
                    wave[w, i] = (wavProm[w * 32 + i] & 0x0f) - 8;
        }

        public void Write(int offset, int data)
        {
            data &= 0x0f;
            lock (gate)
            {
                if (regs[offset] == data) return;
                regs[offset] = data;
                int ch;
                if (offset < 0x10) ch = (offset - 5) / 5;
                else if (offset == 0x10) ch = 0;
                else ch = (offset - 0x11) / 5;
                if (ch < 0 || ch >= 3) return;
                int rel = offset - ch * 5;
                switch (rel)
                {
                    case 0x05: sel[ch] = data & 7; break;
                    case 0x10: case 0x11: case 0x12: case 0x13: case 0x14:
                        {
                            int bs = ch * 5;
                            int f = ch == 0 ? regs[bs + 0x10] : 0;
                            f += (regs[bs + 0x11] << 4) + (regs[bs + 0x12] << 8) + (regs[bs + 0x13] << 12) + (regs[bs + 0x14] << 16);
                            freq[ch] = f;
                            break;
                        }
                    case 0x15: vol[ch] = data; break;
                }
            }
        }

        public void Mix(short[] buf, int offset, int count)
        {
            lock (gate)
            {
                for (int n = 0; n < count; n++)
                {
                    int s = 0;
                    for (int v = 0; v < 3; v++)
                    {
                        int o = 0;
                        if (vol[v] != 0)
                        {
                            counter[v] += (uint)(freq[v] * 2); // 96 kHz native -> 48 kHz
                            o = wave[sel[v], (int)((counter[v] >> 15) & 0x1f)] * vol[v];
                        }
                        if ((n & 3) == 0) Scope[v][(scopePos + (n >> 2)) % ScopeLen] = (short)(o * 80);
                        if ((MuteMask & (1 << v)) == 0) s += o;
                    }
                    float x = s * 80;
                    if (Smooth) { lp += 0.35f * (x - lp); x = lp; }
                    buf[offset + n] = (short)x;
                }
                scopePos = (scopePos + (count >> 2)) % ScopeLen;
            }
        }

        public void Save(BinaryWriter w)
        {
            lock (gate)
            {
                foreach (int v in regs) w.Write(v);
                for (int i = 0; i < 3; i++) { w.Write(freq[i]); w.Write(vol[i]); w.Write(sel[i]); w.Write(counter[i]); }
            }
        }

        public void Load(BinaryReader r)
        {
            lock (gate)
            {
                for (int i = 0; i < regs.Length; i++) regs[i] = r.ReadInt32();
                for (int i = 0; i < 3; i++) { freq[i] = r.ReadInt32(); vol[i] = r.ReadInt32(); sel[i] = r.ReadInt32(); counter[i] = r.ReadUInt32(); }
            }
        }
    }
}
