// Cheats: small patches applied at known points in the game's own code (no ROM changes are made).
// The hook addresses were found by tracing the game: 0x1B8C = "lose a life", 0x1395 = enemy-contact kill check,
// 0x19AD/0x19B9 = "next round" counter increment.
namespace DigDug
{
    public sealed class Cheats
    {
        public bool InfiniteLives;
        public bool Invincible;       // enemies can no longer kill by touching you (rocks and flames still can)
        public int StartRound = 1;    // 1 = normal; N starts a new game at round N

        public bool Active { get { return InfiniteLives || Invincible || StartRound > 1; } }
        public string Summary
        {
            get
            {
                var l = new System.Collections.Generic.List<string>();
                if (InfiniteLives) l.Add("INF LIVES");
                if (Invincible) l.Add("INVINCIBLE");
                if (StartRound > 1) l.Add("ROUND " + StartRound);
                return string.Join(" ", l.ToArray());
            }
        }
    }
}
