// Small table-driven Z80 disassembler (developer tool: `DigDug.exe --disasm`).
using System;
using System.Text;

namespace DigDug
{
    public static class Disasm
    {
        static readonly string[] R8 = { "b", "c", "d", "e", "h", "l", "(hl)", "a" };
        static readonly string[] RP = { "bc", "de", "hl", "sp" };
        static readonly string[] RP2 = { "bc", "de", "hl", "af" };
        static readonly string[] CC = { "nz", "z", "nc", "c", "po", "pe", "p", "m" };
        static readonly string[] ALU = { "add a,", "adc a,", "sub ", "sbc a,", "and ", "xor ", "or ", "cp " };
        static readonly string[] ROT = { "rlc", "rrc", "rl", "rr", "sla", "sra", "sll", "srl" };

        static string H8(int v) { return "$" + (v & 255).ToString("x2"); }
        static string H16(int v) { return "$" + (v & 0xffff).ToString("x4"); }

        // returns text; len = instruction length
        public static string One(byte[] m, int pc, out int len)
        {
            int start = pc;
            Func<int> fetch = () => { int v = m[pc & 0xffff]; pc++; return v; };
            string idx = null;
            int op = fetch();
            while (op == 0xdd || op == 0xfd) { idx = op == 0xdd ? "ix" : "iy"; op = fetch(); }
            string hl = idx ?? "hl";
            Func<int, string> r8 = i =>
            {
                if (i == 6) { if (idx == null) return "(hl)"; int d = (sbyte)fetch(); return "(" + idx + (d < 0 ? "-" + (-d) : "+" + d) + ")"; }
                if (idx != null && i == 4) return idx + "h";
                if (idx != null && i == 5) return idx + "l";
                return R8[i];
            };
            int x = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
            string s;
            switch (x)
            {
                case 0:
                    switch (z)
                    {
                        case 0:
                            if (y == 0) s = "nop";
                            else if (y == 1) s = "ex af,af'";
                            else { int d = (sbyte)fetch(); string t = H16(pc + d); s = (y == 2 ? "djnz " : y == 3 ? "jr " : "jr " + CC[y - 4] + ",") + t; }
                            break;
                        case 1:
                            if (q == 0) { int lo = fetch(); int n = lo | fetch() << 8; s = "ld " + (p == 2 ? hl : RP[p]) + "," + H16(n); }
                            else s = "add " + hl + "," + (p == 2 ? hl : RP[p]);
                            break;
                        case 2:
                            if (p == 0) s = q == 0 ? "ld (bc),a" : "ld a,(bc)";
                            else if (p == 1) s = q == 0 ? "ld (de),a" : "ld a,(de)";
                            else { int lo = fetch(); int n = lo | fetch() << 8; s = p == 2 ? (q == 0 ? "ld (" + H16(n) + ")," + hl : "ld " + hl + ",(" + H16(n) + ")") : (q == 0 ? "ld (" + H16(n) + "),a" : "ld a,(" + H16(n) + ")"); }
                            break;
                        case 3: s = (q == 0 ? "inc " : "dec ") + (p == 2 ? hl : RP[p]); break;
                        case 4: s = "inc " + r8(y); break;
                        case 5: s = "dec " + r8(y); break;
                        case 6: { string t = r8(y); s = "ld " + t + "," + H8(fetch()); break; }
                        default: s = new[] { "rlca", "rrca", "rla", "rra", "daa", "cpl", "scf", "ccf" }[y]; break;
                    }
                    break;
                case 1:
                    if (y == 6 && z == 6) s = "halt";
                    else if (y == 6) { string t = r8(6); s = "ld " + t + "," + R8[z]; }
                    else if (z == 6) { string t = r8(6); s = "ld " + R8[y] + "," + t; }
                    else s = "ld " + r8(y) + "," + r8(z);
                    break;
                case 2: s = ALU[y] + r8(z); break;
                default:
                    switch (z)
                    {
                        case 0: s = "ret " + CC[y]; break;
                        case 1:
                            if (q == 0) s = "pop " + (p == 2 ? hl : RP2[p]);
                            else s = new[] { "ret", "exx", "jp (" + hl + ")", "ld sp," + hl }[p];
                            break;
                        case 2: { int lo = fetch(); int n = lo | fetch() << 8; s = "jp " + CC[y] + "," + H16(n); break; }
                        case 3:
                            switch (y)
                            {
                                case 0: { int lo = fetch(); int n = lo | fetch() << 8; s = "jp " + H16(n); break; }
                                case 1:
                                    {
                                        string t = null; if (idx != null) { int d = (sbyte)fetch(); t = "(" + idx + (d < 0 ? "-" + (-d) : "+" + d) + ")"; }
                                        int o2 = fetch(); int x2 = o2 >> 6, y2 = (o2 >> 3) & 7, z2 = o2 & 7;
                                        string tgt = t ?? R8[z2];
                                        s = x2 == 0 ? ROT[y2] + " " + tgt : (x2 == 1 ? "bit " : x2 == 2 ? "res " : "set ") + y2 + "," + tgt;
                                        break;
                                    }
                                case 2: s = "out (" + H8(fetch()) + "),a"; break;
                                case 3: s = "in a,(" + H8(fetch()) + ")"; break;
                                case 4: s = "ex (sp)," + hl; break;
                                case 5: s = "ex de,hl"; break;
                                case 6: s = "di"; break;
                                default: s = "ei"; break;
                            }
                            break;
                        case 4: { int lo = fetch(); int n = lo | fetch() << 8; s = "call " + CC[y] + "," + H16(n); break; }
                        case 5:
                            if (q == 0) s = "push " + (p == 2 ? hl : RP2[p]);
                            else if (p == 0) { int lo = fetch(); int n = lo | fetch() << 8; s = "call " + H16(n); }
                            else
                            {
                                int o2 = fetch(); int x2 = o2 >> 6, y2 = (o2 >> 3) & 7, z2 = o2 & 7;
                                if (x2 == 1)
                                {
                                    switch (z2)
                                    {
                                        case 0: s = "in " + (y2 == 6 ? "f" : R8[y2]) + ",(c)"; break;
                                        case 1: s = "out (c)," + (y2 == 6 ? "0" : R8[y2]); break;
                                        case 2: s = (y2 & 1) == 0 ? "sbc hl," + RP[y2 >> 1] : "adc hl," + RP[y2 >> 1]; break;
                                        case 3: { int lo = fetch(); int n = lo | fetch() << 8; s = (y2 & 1) == 0 ? "ld (" + H16(n) + ")," + RP[y2 >> 1] : "ld " + RP[y2 >> 1] + ",(" + H16(n) + ")"; break; }
                                        case 4: s = "neg"; break;
                                        case 5: s = y2 == 1 ? "reti" : "retn"; break;
                                        case 6: s = "im " + new[] { 0, 0, 1, 2, 0, 0, 1, 2 }[y2]; break;
                                        default: s = new[] { "ld i,a", "ld r,a", "ld a,i", "ld a,r", "rrd", "rld", "nop", "nop" }[y2]; break;
                                    }
                                }
                                else if (x2 == 2 && y2 >= 4 && z2 < 4)
                                    s = new[] { new[] { "ldi", "cpi", "ini", "outi" }, new[] { "ldd", "cpd", "ind", "outd" }, new[] { "ldir", "cpir", "inir", "otir" }, new[] { "lddr", "cpdr", "indr", "otdr" } }[y2 - 4][z2];
                                else s = "db $ed,$" + o2.ToString("x2");
                            }
                            break;
                        case 6: s = ALU[y] + H8(fetch()); break;
                        default: s = "rst " + H8(y * 8); break;
                    }
                    break;
            }
            len = pc - start;
            return s;
        }

        public static string Range(byte[] m, int from, int to)
        {
            var sb = new StringBuilder();
            int pc = from;
            while (pc < to)
            {
                int len; string s = One(m, pc, out len);
                var bytes = new StringBuilder();
                for (int i = 0; i < len; i++) bytes.Append(m[(pc + i) & 0xffff].ToString("x2")).Append(' ');
                sb.Append(pc.ToString("x4")).Append(":  ").Append(bytes.ToString().PadRight(13)).Append(s).Append("\r\n");
                pc += len;
            }
            return sb.ToString();
        }
    }
}
