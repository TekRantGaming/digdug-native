// Zilog Z80 CPU core (documented instruction set, cycle-counted).
using System;

namespace DigDug
{
    public interface IBus
    {
        int Read(int addr);
        void Write(int addr, int value);
    }

    public sealed class Z80
    {
        const int FS = 0x80, FZ = 0x40, FY = 0x20, FH = 0x10, FX = 0x08, FP = 0x04, FN = 0x02, FC = 0x01;

        public int A, F, B, C, D, E, H, L;
        int A2, F2, B2, C2, D2, E2, H2, L2;
        public int IX, IY, SP, PC, I, R;
        public bool IFF1, IFF2, Halted;
        public int IM;
        public bool IrqLine;
        public bool NmiPending;
        bool eiDelay;
        int pfx; // 0 none, 1 IX, 2 IY
        int cyc;
        readonly IBus bus;

        static readonly int[] Par = new int[256];
        static readonly int[] SZP = new int[256];

        static Z80()
        {
            for (int i = 0; i < 256; i++)
            {
                int p = 1, v = i;
                for (int b = 0; b < 8; b++) { p ^= v & 1; v >>= 1; }
                Par[i] = p != 0 ? FP : 0;
                SZP[i] = (i & (FS | FY | FX)) | (i == 0 ? FZ : 0) | Par[i];
            }
        }

        public Z80(IBus b) { bus = b; Reset(); }

        public void Reset()
        {
            A = F = B = C = D = E = H = L = 0xff;
            A2 = F2 = B2 = C2 = D2 = E2 = H2 = L2 = 0xff;
            IX = IY = 0xffff; SP = 0xffff; PC = 0; I = R = 0;
            IFF1 = IFF2 = false; IM = 0; Halted = false; IrqLine = false; NmiPending = false; eiDelay = false;
        }

        int BC { get { return B << 8 | C; } set { B = (value >> 8) & 255; C = value & 255; } }
        int DE { get { return D << 8 | E; } set { D = (value >> 8) & 255; E = value & 255; } }
        int HL { get { return H << 8 | L; } set { H = (value >> 8) & 255; L = value & 255; } }
        int AF { get { return A << 8 | F; } set { A = (value >> 8) & 255; F = value & 255; } }

        int Fetch() { int v = bus.Read(PC); PC = (PC + 1) & 0xffff; return v; }
        int Fetch16() { int l = Fetch(); return l | Fetch() << 8; }
        int Rd16(int a) { return bus.Read(a) | bus.Read((a + 1) & 0xffff) << 8; }
        void Wr16(int a, int v) { bus.Write(a, v & 255); bus.Write((a + 1) & 0xffff, (v >> 8) & 255); }
        void Push(int v) { SP = (SP - 1) & 0xffff; bus.Write(SP, (v >> 8) & 255); SP = (SP - 1) & 0xffff; bus.Write(SP, v & 255); }
        int Pop() { int l = bus.Read(SP); SP = (SP + 1) & 0xffff; int h = bus.Read(SP); SP = (SP + 1) & 0xffff; return l | h << 8; }

        // index-aware HL
        int XHL { get { return pfx == 0 ? HL : pfx == 1 ? IX : IY; } set { if (pfx == 0) HL = value; else if (pfx == 1) IX = value & 0xffff; else IY = value & 0xffff; } }

        int GetR(int i)
        {
            switch (i)
            {
                case 0: return B; case 1: return C; case 2: return D; case 3: return E;
                case 4: return pfx == 0 ? H : (pfx == 1 ? IX : IY) >> 8;
                case 5: return pfx == 0 ? L : (pfx == 1 ? IX : IY) & 255;
                default: return A;
            }
        }
        void SetR(int i, int v)
        {
            v &= 255;
            switch (i)
            {
                case 0: B = v; break; case 1: C = v; break; case 2: D = v; break; case 3: E = v; break;
                case 4: if (pfx == 0) H = v; else if (pfx == 1) IX = (IX & 0xff) | v << 8; else IY = (IY & 0xff) | v << 8; break;
                case 5: if (pfx == 0) L = v; else if (pfx == 1) IX = (IX & 0xff00) | v; else IY = (IY & 0xff00) | v; break;
                default: A = v; break;
            }
        }
        // plain (non-indexed) register access, used when the other operand is (IX+d)
        int GetPlain(int i) { int s = pfx; pfx = 0; int v = GetR(i); pfx = s; return v; }
        void SetPlain(int i, int v) { int s = pfx; pfx = 0; SetR(i, v); pfx = s; }

        int EA()
        {
            if (pfx == 0) return HL;
            int d = (sbyte)Fetch();
            cyc += 8;
            return ((pfx == 1 ? IX : IY) + d) & 0xffff;
        }

        int RP(int p) { switch (p) { case 0: return BC; case 1: return DE; case 2: return XHL; default: return SP; } }
        void SetRP(int p, int v) { v &= 0xffff; switch (p) { case 0: BC = v; break; case 1: DE = v; break; case 2: XHL = v; break; default: SP = v; break; } }

        bool Cond(int c)
        {
            switch (c)
            {
                case 0: return (F & FZ) == 0; case 1: return (F & FZ) != 0;
                case 2: return (F & FC) == 0; case 3: return (F & FC) != 0;
                case 4: return (F & FP) == 0; case 5: return (F & FP) != 0;
                case 6: return (F & FS) == 0; default: return (F & FS) != 0;
            }
        }

        void Alu(int op, int v)
        {
            int r, c;
            switch (op)
            {
                case 0: // ADD
                    r = A + v; F = (r & (FS | FY | FX)) | ((r & 255) == 0 ? FZ : 0) | ((A ^ v ^ r) & FH) | ((~(A ^ v) & (A ^ r) & 0x80) != 0 ? FP : 0) | (r > 255 ? FC : 0);
                    A = r & 255; break;
                case 1: // ADC
                    c = F & FC; r = A + v + c; F = (r & (FS | FY | FX)) | ((r & 255) == 0 ? FZ : 0) | ((A ^ v ^ r) & FH) | ((~(A ^ v) & (A ^ r) & 0x80) != 0 ? FP : 0) | (r > 255 ? FC : 0);
                    A = r & 255; break;
                case 2: // SUB
                    r = A - v; F = (r & (FS | FY | FX)) | ((r & 255) == 0 ? FZ : 0) | ((A ^ v ^ r) & FH) | (((A ^ v) & (A ^ r) & 0x80) != 0 ? FP : 0) | FN | (r < 0 ? FC : 0);
                    A = r & 255; break;
                case 3: // SBC
                    c = F & FC; r = A - v - c; F = (r & (FS | FY | FX)) | ((r & 255) == 0 ? FZ : 0) | ((A ^ v ^ r) & FH) | (((A ^ v) & (A ^ r) & 0x80) != 0 ? FP : 0) | FN | (r < 0 ? FC : 0);
                    A = r & 255; break;
                case 4: A &= v; F = SZP[A] | FH; break;
                case 5: A ^= v; F = SZP[A]; break;
                case 6: A |= v; F = SZP[A]; break;
                default: // CP
                    r = A - v; F = (v & (FY | FX)) | (r & FS) | ((r & 255) == 0 ? FZ : 0) | ((A ^ v ^ r) & FH) | (((A ^ v) & (A ^ r) & 0x80) != 0 ? FP : 0) | FN | (r < 0 ? FC : 0);
                    break;
            }
        }

        int Inc8(int v)
        {
            int r = (v + 1) & 255;
            F = (F & FC) | (r & (FS | FY | FX)) | (r == 0 ? FZ : 0) | ((v & 15) == 15 ? FH : 0) | (v == 0x7f ? FP : 0);
            return r;
        }
        int Dec8(int v)
        {
            int r = (v - 1) & 255;
            F = (F & FC) | (r & (FS | FY | FX)) | (r == 0 ? FZ : 0) | ((v & 15) == 0 ? FH : 0) | (v == 0x80 ? FP : 0) | FN;
            return r;
        }

        int Add16(int a, int b)
        {
            int r = a + b;
            F = (F & (FS | FZ | FP)) | ((r >> 8) & (FY | FX)) | (((a ^ b ^ r) >> 8) & FH) | (r > 0xffff ? FC : 0);
            return r & 0xffff;
        }
        void Adc16(int b)
        {
            int a = HL, c = F & FC, r = a + b + c;
            F = ((r >> 8) & (FS | FY | FX)) | ((r & 0xffff) == 0 ? FZ : 0) | (((a ^ b ^ r) >> 8) & FH) | ((~(a ^ b) & (a ^ r) & 0x8000) != 0 ? FP : 0) | (r > 0xffff ? FC : 0);
            HL = r;
        }
        void Sbc16(int b)
        {
            int a = HL, c = F & FC, r = a - b - c;
            F = ((r >> 8) & (FS | FY | FX)) | ((r & 0xffff) == 0 ? FZ : 0) | (((a ^ b ^ r) >> 8) & FH) | (((a ^ b) & (a ^ r) & 0x8000) != 0 ? FP : 0) | FN | (r < 0 ? FC : 0);
            HL = r;
        }

        int Rot(int op, int v)
        {
            int c, r;
            switch (op)
            {
                case 0: c = v >> 7; r = ((v << 1) | c) & 255; break;          // RLC
                case 1: c = v & 1; r = (v >> 1) | (c << 7); break;             // RRC
                case 2: c = v >> 7; r = ((v << 1) | (F & FC)) & 255; break;    // RL
                case 3: c = v & 1; r = (v >> 1) | ((F & FC) << 7); break;      // RR
                case 4: c = v >> 7; r = (v << 1) & 255; break;                 // SLA
                case 5: c = v & 1; r = (v >> 1) | (v & 0x80); break;           // SRA
                case 6: c = v >> 7; r = ((v << 1) | 1) & 255; break;           // SLL
                default: c = v & 1; r = v >> 1; break;                         // SRL
            }
            F = SZP[r] | c;
            return r;
        }

        void Daa()
        {
            int a = A, add = 0, carry = F & FC;
            if ((F & FH) != 0 || (a & 15) > 9) add |= 6;
            if (carry != 0 || a > 0x99) { add |= 0x60; carry = FC; }
            int r;
            if ((F & FN) != 0) { r = (a - add) & 255; F = SZP[r] | carry | FN | (((a ^ r) & FH)); }
            else { r = (a + add) & 255; F = SZP[r] | carry | (((a ^ r) & FH)); }
            A = r;
        }

        void ExecCB()
        {
            int op, ea = -1, d = 0;
            if (pfx != 0)
            {
                d = (sbyte)Fetch();
                ea = ((pfx == 1 ? IX : IY) + d) & 0xffff;
                op = Fetch();
            }
            else { op = Fetch(); R = (R & 0x80) | ((R + 1) & 0x7f); }
            int x = op >> 6, y = (op >> 3) & 7, z = op & 7;
            int v;
            if (ea >= 0) v = bus.Read(ea);
            else if (z == 6) v = bus.Read(HL);
            else v = GetPlain(z);

            if (x == 1)
            {
                int m = v & (1 << y);
                F = (F & FC) | FH | (m == 0 ? FZ | FP : 0) | (m & FS) | (v & (FY | FX));
                cyc += (ea >= 0) ? 20 : (z == 6 ? 12 : 8);
                return;
            }
            int r;
            if (x == 0) r = Rot(y, v);
            else if (x == 2) r = v & ~(1 << y);
            else r = v | (1 << y);

            if (ea >= 0) { bus.Write(ea, r); if (z != 6) SetPlain(z, r); cyc += 23; }
            else if (z == 6) { bus.Write(HL, r); cyc += 15; }
            else { SetPlain(z, r); cyc += 8; }
        }

        void ExecED()
        {
            int op = Fetch(); R = (R & 0x80) | ((R + 1) & 0x7f);
            int x = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
            int s = pfx; pfx = 0;
            if (x == 1)
            {
                switch (z)
                {
                    case 0: { int v = 0xff; F = (F & FC) | SZP[v]; if (y != 6) SetR(y, v); cyc += 12; break; }
                    case 1: cyc += 12; break;
                    case 2: if (q == 0) Sbc16(RP(p)); else Adc16(RP(p)); cyc += 15; break;
                    case 3:
                        { int a = Fetch16(); if (q == 0) Wr16(a, RP(p)); else SetRP(p, Rd16(a)); cyc += 20; break; }
                    case 4:
                        { int v = A; A = 0; Alu(2, v); cyc += 8; break; }
                    case 5: IFF1 = IFF2; PC = Pop(); cyc += 14; break;
                    case 6: IM = (y & 3) < 2 ? 0 : (y & 3) == 2 ? 1 : 2; cyc += 8; break;
                    default:
                        switch (y)
                        {
                            case 0: I = A; cyc += 9; break;
                            case 1: R = A; cyc += 9; break;
                            case 2: A = I; F = (F & FC) | SZP[A] & ~FP | (IFF2 ? FP : 0); cyc += 9; break;
                            case 3: A = R; F = (F & FC) | SZP[A] & ~FP | (IFF2 ? FP : 0); cyc += 9; break;
                            case 4: { int m = bus.Read(HL); bus.Write(HL, ((A << 4) | (m >> 4)) & 255); // RRD
                                      A = (A & 0xf0) | (m & 15); F = (F & FC) | SZP[A]; cyc += 18; break; }
                            case 5: { int m = bus.Read(HL); bus.Write(HL, ((m << 4) | (A & 15)) & 255); A = (A & 0xf0) | (m >> 4); F = (F & FC) | SZP[A]; cyc += 18; break; }
                            default: cyc += 8; break;
                        }
                        break;
                }
            }
            else if (x == 2 && y >= 4 && z < 4)
            {
                bool inc = (y & 1) == 0, rep = y >= 6;
                switch (z)
                {
                    case 0: // LDI/LDD/LDIR/LDDR
                        {
                            bus.Write(DE, bus.Read(HL));
                            HL = (HL + (inc ? 1 : -1)) & 0xffff; DE = (DE + (inc ? 1 : -1)) & 0xffff; BC = (BC - 1) & 0xffff;
                            F = (F & (FS | FZ | FC)) | (BC != 0 ? FP : 0);
                            if (rep && BC != 0) { PC = (PC - 2) & 0xffff; cyc += 21; } else cyc += 16;
                            break;
                        }
                    case 1: // CPI/CPD/CPIR/CPDR
                        {
                            int v = bus.Read(HL), r = (A - v) & 255;
                            HL = (HL + (inc ? 1 : -1)) & 0xffff; BC = (BC - 1) & 0xffff;
                            F = (F & FC) | FN | (r & FS) | (r == 0 ? FZ : 0) | ((A ^ v ^ r) & FH) | (BC != 0 ? FP : 0);
                            if (rep && BC != 0 && r != 0) { PC = (PC - 2) & 0xffff; cyc += 21; } else cyc += 16;
                            break;
                        }
                    case 2: // INI.. (no port hardware)
                        { bus.Write(HL, 0xff); HL = (HL + (inc ? 1 : -1)) & 0xffff; B = (B - 1) & 255; F = (B == 0 ? FZ : 0) | FN; if (rep && B != 0) { PC = (PC - 2) & 0xffff; cyc += 21; } else cyc += 16; break; }
                    default: // OUTI..
                        { B = (B - 1) & 255; HL = (HL + (inc ? 1 : -1)) & 0xffff; F = (B == 0 ? FZ : 0) | FN; if (rep && B != 0) { PC = (PC - 2) & 0xffff; cyc += 21; } else cyc += 16; break; }
                }
            }
            else cyc += 8;
            pfx = s;
        }

        static readonly int[] Tbl = new int[256];

        public int Step()
        {
            cyc = 0;
            bool canInt = !eiDelay; eiDelay = false;
            if (NmiPending)
            {
                NmiPending = false; Halted = false; IFF2 = IFF1; IFF1 = false;
                Push(PC); PC = 0x66; return 11;
            }
            if (IrqLine && IFF1 && canInt)
            {
                Halted = false; IFF1 = IFF2 = false;
                Push(PC);
                if (IM == 2) { PC = Rd16(I << 8 | 0xff); return 19; }
                PC = 0x38; return 13;
            }
            if (Halted) return 4;

            pfx = 0;
            int op = Fetch(); R = (R & 0x80) | ((R + 1) & 0x7f);
            while (op == 0xdd || op == 0xfd)
            {
                pfx = op == 0xdd ? 1 : 2; cyc += 4;
                op = Fetch(); R = (R & 0x80) | ((R + 1) & 0x7f);
            }
            int x = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
            int t, v;

            switch (x)
            {
                case 0:
                    switch (z)
                    {
                        case 0:
                            switch (y)
                            {
                                case 0: cyc += 4; break;
                                case 1: t = A; A = A2; A2 = t; t = F; F = F2; F2 = t; cyc += 4; break;
                                case 2: { int d = (sbyte)Fetch(); B = (B - 1) & 255; if (B != 0) { PC = (PC + d) & 0xffff; cyc += 13; } else cyc += 8; break; }
                                case 3: { int d = (sbyte)Fetch(); PC = (PC + d) & 0xffff; cyc += 12; break; }
                                default: { int d = (sbyte)Fetch(); if (Cond(y - 4)) { PC = (PC + d) & 0xffff; cyc += 12; } else cyc += 7; break; }
                            }
                            break;
                        case 1:
                            if (q == 0) { SetRP(p, Fetch16()); cyc += 10; }
                            else { XHL = Add16(XHL, RP(p)); cyc += 11; }
                            break;
                        case 2:
                            switch (p)
                            {
                                case 0: if (q == 0) bus.Write(BC, A); else A = bus.Read(BC); cyc += 7; break;
                                case 1: if (q == 0) bus.Write(DE, A); else A = bus.Read(DE); cyc += 7; break;
                                case 2: { int a = Fetch16(); if (q == 0) Wr16(a, XHL); else XHL = Rd16(a); cyc += 16; break; }
                                default: { int a = Fetch16(); if (q == 0) bus.Write(a, A); else A = bus.Read(a); cyc += 13; break; }
                            }
                            break;
                        case 3:
                            SetRP(p, RP(p) + (q == 0 ? 1 : -1)); cyc += 6; break;
                        case 4:
                            if (y == 6) { int a = EA(); bus.Write(a, Inc8(bus.Read(a))); cyc += 11; }
                            else { SetR(y, Inc8(GetR(y))); cyc += 4; }
                            break;
                        case 5:
                            if (y == 6) { int a = EA(); bus.Write(a, Dec8(bus.Read(a))); cyc += 11; }
                            else { SetR(y, Dec8(GetR(y))); cyc += 4; }
                            break;
                        case 6:
                            if (y == 6) { int a = EA(); bus.Write(a, Fetch()); cyc += 10; }
                            else { SetR(y, Fetch()); cyc += 7; }
                            break;
                        default:
                            switch (y)
                            {
                                case 0: { int c = A >> 7; A = ((A << 1) | c) & 255; F = (F & (FS | FZ | FP)) | (A & (FY | FX)) | c; break; }
                                case 1: { int c = A & 1; A = (A >> 1) | (c << 7); F = (F & (FS | FZ | FP)) | (A & (FY | FX)) | c; break; }
                                case 2: { int c = A >> 7; A = ((A << 1) | (F & FC)) & 255; F = (F & (FS | FZ | FP)) | (A & (FY | FX)) | c; break; }
                                case 3: { int c = A & 1; A = (A >> 1) | ((F & FC) << 7); F = (F & (FS | FZ | FP)) | (A & (FY | FX)) | c; break; }
                                case 4: Daa(); break;
                                case 5: A ^= 0xff; F = (F & (FS | FZ | FP | FC)) | (A & (FY | FX)) | FH | FN; break;
                                case 6: F = (F & (FS | FZ | FP)) | (A & (FY | FX)) | FC; break;
                                default: F = (F & (FS | FZ | FP)) | (A & (FY | FX)) | ((F & FC) != 0 ? FH : FC); break;
                            }
                            cyc += 4;
                            break;
                    }
                    break;
                case 1:
                    if (y == 6 && z == 6) { Halted = true; PC = (PC - 1) & 0xffff; cyc += 4; }
                    else if (y == 6) { int a = EA(); bus.Write(a, GetPlain(z)); cyc += 7; }
                    else if (z == 6) { int a = EA(); SetPlain(y, bus.Read(a)); cyc += 7; }
                    else { SetR(y, GetR(z)); cyc += 4; }
                    break;
                case 2:
                    if (z == 6) { int a = EA(); Alu(y, bus.Read(a)); cyc += 7; }
                    else { Alu(y, GetR(z)); cyc += 4; }
                    break;
                default:
                    switch (z)
                    {
                        case 0: if (Cond(y)) { PC = Pop(); cyc += 11; } else cyc += 5; break;
                        case 1:
                            if (q == 0)
                            {
                                v = Pop();
                                switch (p) { case 0: BC = v; break; case 1: DE = v; break; case 2: XHL = v; break; default: AF = v; break; }
                                cyc += 10;
                            }
                            else
                            {
                                switch (p)
                                {
                                    case 0: PC = Pop(); cyc += 10; break;
                                    case 1:
                                        t = B; B = B2; B2 = t; t = C; C = C2; C2 = t; t = D; D = D2; D2 = t;
                                        t = E; E = E2; E2 = t; t = H; H = H2; H2 = t; t = L; L = L2; L2 = t; cyc += 4; break;
                                    case 2: PC = XHL; cyc += 4; break;
                                    default: SP = XHL; cyc += 6; break;
                                }
                            }
                            break;
                        case 2: { int a = Fetch16(); if (Cond(y)) PC = a; cyc += 10; break; }
                        case 3:
                            switch (y)
                            {
                                case 0: PC = Fetch16(); cyc += 10; break;
                                case 1: ExecCB(); break;
                                case 2: Fetch(); cyc += 11; break; // OUT (n),A - unused
                                case 3: Fetch(); A = 0xff; cyc += 11; break; // IN A,(n) - unused
                                case 4: { v = Rd16(SP); Wr16(SP, XHL); XHL = v; cyc += 19; break; }
                                case 5: t = D; D = H; H = t; t = E; E = L; L = t; cyc += 4; break;
                                case 6: IFF1 = IFF2 = false; cyc += 4; break;
                                default: IFF1 = IFF2 = true; eiDelay = true; cyc += 4; break;
                            }
                            break;
                        case 4: { int a = Fetch16(); if (Cond(y)) { Push(PC); PC = a; cyc += 17; } else cyc += 10; break; }
                        case 5:
                            if (q == 0)
                            {
                                switch (p) { case 0: v = BC; break; case 1: v = DE; break; case 2: v = XHL; break; default: v = AF; break; }
                                Push(v); cyc += 11;
                            }
                            else if (p == 0) { int a = Fetch16(); Push(PC); PC = a; cyc += 17; }
                            else if (p == 2) ExecED();
                            else cyc += 4;
                            break;
                        case 6: Alu(y, Fetch()); cyc += 7; break;
                        default: Push(PC); PC = y * 8; cyc += 11; break;
                    }
                    break;
            }
            return cyc;
        }
    }
}
