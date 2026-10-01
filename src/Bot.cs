// Developer test bot (headless runner only: --bot). Reads sprite RAM, tunnels toward the nearest enemy,
// faces it and pumps, so that kills / round clear / the exit sequence can be tested automatically.
using System;

namespace DigDug
{
    public sealed class Bot
    {
        public int PumpPhase;
        public bool IdleWhenOne;
        public int Kills;
        public string LastNote = "";
        readonly Machine m;
        public Bot(Machine mm) { m = mm; }

        static bool Parked(int yram, int xram) { return yram == 0; }

        void Pos(int slot, out int sx, out int sy, out bool parked)
        {
            int o = slot * 2;
            int yr = m.Ram[0x1380 + o], xr = m.Ram[0x1381 + o], hi = m.Ram[0x1b81 + o] & 3;
            sx = ((xr - 40) & 0xff) + 256 * hi;
            sy = ((256 - yr + 1) & 0xff) - 32;
            parked = Parked(yr, xr);
        }

        public void Step(InputState inp, long frame)
        {
            inp.Dir = -1; inp.Fire = false;
            int px, py; bool pp;
            Pos(17, out px, out py, out pp);
            if (pp) Pos(0, out px, out py, out pp);
            if (pp) return;

            // nearest enemy: pooka/fygar sprites live in slots 50..57
            int best = -1, bd = int.MaxValue, bx = 0, by = 0;
            for (int s = 50; s <= 57; s++)
            {
                int ex, ey; bool ep; Pos(s, out ex, out ey, out ep);
                if (ep) continue;
                int d = Math.Abs(ex - px) + Math.Abs(ey - py);
                if (d < bd) { bd = d; best = s; bx = ex; by = ey; }
            }
            if (best < 0) { LastNote = "no enemies visible"; return; }
            int visible = 0;
            for (int s = 50; s <= 57; s++) { int ex, ey; bool ep; Pos(s, out ex, out ey, out ep); if (!ep) visible++; }
            if (IdleWhenOne && visible <= 1 && frame > 3600) { LastNote = "idle: one enemy left (visible=" + visible + ")"; return; }

            int dx = bx - px, dy = by - py;       // dx>0: enemy is lower on screen, dy>0: enemy is further left
            bool alignedX = Math.Abs(dx) <= 6, alignedY = Math.Abs(dy) <= 6;
            int dir = -1;
            bool fire = false;
            if (alignedY && Math.Abs(dx) <= 34) { dir = dx > 0 ? 4 : 0; fire = Math.Abs(dx) <= 30; if (fire) dir = -1; }
            else if (alignedX && Math.Abs(dy) <= 34) { dir = dy > 0 ? 6 : 2; fire = Math.Abs(dy) <= 30; if (fire) dir = -1; }
            else if (Math.Abs(dx) >= Math.Abs(dy)) { if (!alignedX || true) dir = dx > 0 ? 4 : 0; if (alignedX) dir = dy > 0 ? 6 : 2; }
            else { dir = dy > 0 ? 6 : 2; if (alignedY) dir = dx > 0 ? 4 : 0; }

            inp.Dir = dir;
            if (fire)
            {
                // hold the button; tap-release briefly so each press registers as a fresh pump
                PumpPhase = (PumpPhase + 1) % 16;
                inp.Fire = PumpPhase < 12;
            }
            else PumpPhase = 0;
            LastNote = "target slot " + best + " d=(" + dx + "," + dy + ") dir=" + dir + " fire=" + fire;
        }
    }
}
