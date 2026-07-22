using System;
using System.Collections.Generic;
using System.Linq;

namespace EconomicGame.Models.Poker
{
    /// <summary>
    /// Card suits. Order carries no gameplay meaning.
    /// </summary>
    public enum CardSuit { Clubs = 0, Diamonds = 1, Hearts = 2, Spades = 3 }

    /// <summary>
    /// A playing card. Rank: 2..14 (14 = Ace).
    /// </summary>
    public readonly struct Card : IEquatable<Card>
    {
        public int Rank { get; }          // 2..14
        public CardSuit Suit { get; }

        public Card(int rank, CardSuit suit)
        {
            if (rank < 2 || rank > 14) throw new ArgumentOutOfRangeException(nameof(rank));
            Rank = rank;
            Suit = suit;
        }

        public bool Equals(Card other) => Rank == other.Rank && Suit == other.Suit;
        public override bool Equals(object? obj) => obj is Card c && Equals(c);
        public override int GetHashCode() => (Rank << 2) | (int)Suit;

        public string RankSymbol => Rank switch
        {
            14 => "A", 13 => "K", 12 => "Q", 11 => "J", 10 => "10",
            _ => Rank.ToString()
        };

        public string SuitSymbol => Suit switch
        {
            CardSuit.Clubs => "♣",
            CardSuit.Diamonds => "♦",
            CardSuit.Hearts => "♥",
            _ => "♠"
        };

        public bool IsRed => Suit == CardSuit.Hearts || Suit == CardSuit.Diamonds;

        public override string ToString() => RankSymbol + SuitSymbol;
    }

    /// <summary>
    /// A standard 52-card deck with Fisher-Yates shuffle.
    /// </summary>
    public class Deck
    {
        private readonly List<Card> _cards = new(52);
        private int _next;

        public Deck(Random rng)
        {
            for (int suit = 0; suit < 4; suit++)
                for (int rank = 2; rank <= 14; rank++)
                    _cards.Add(new Card(rank, (CardSuit)suit));

            // Fisher-Yates
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
            }
        }

        public int Remaining => _cards.Count - _next;

        public Card Draw()
        {
            if (_next >= _cards.Count) throw new InvalidOperationException("Deck is empty");
            return _cards[_next++];
        }
    }

    /// <summary>
    /// Poker hand categories, ascending strength.
    /// </summary>
    public enum HandCategory
    {
        HighCard = 0,
        Pair = 1,
        TwoPair = 2,
        ThreeOfAKind = 3,
        Straight = 4,
        Flush = 5,
        FullHouse = 6,
        FourOfAKind = 7,
        StraightFlush = 8
    }

    /// <summary>
    /// Evaluates the best 5-card poker hand out of 5-7 cards.
    /// Produces a single comparable score: higher score = stronger hand.
    /// Score layout: category in the top bits, then five 4-bit kickers.
    /// </summary>
    public static class HandEvaluator
    {
        public static string CategoryNameRu(HandCategory c) => c switch
        {
            HandCategory.StraightFlush => "Стрит-флеш",
            HandCategory.FourOfAKind => "Каре",
            HandCategory.FullHouse => "Фулл-хаус",
            HandCategory.Flush => "Флеш",
            HandCategory.Straight => "Стрит",
            HandCategory.ThreeOfAKind => "Тройка",
            HandCategory.TwoPair => "Две пары",
            HandCategory.Pair => "Пара",
            _ => "Старшая карта"
        };

        public static (long score, HandCategory category) Evaluate(IReadOnlyList<Card> cards)
        {
            if (cards.Count < 5 || cards.Count > 7)
                throw new ArgumentException("Evaluate expects 5-7 cards");

            // Rank counts
            Span<int> rankCount = stackalloc int[15]; // index 2..14
            Span<int> suitCount = stackalloc int[4];
            foreach (var c in cards)
            {
                rankCount[c.Rank]++;
                suitCount[(int)c.Suit]++;
            }

            // --- Flush? ---
            int flushSuit = -1;
            for (int s = 0; s < 4; s++)
                if (suitCount[s] >= 5) flushSuit = s;

            // --- Straight helper over a rank bitmask ---
            static int BestStraightHigh(int rankMask)
            {
                // Ace also plays low (wheel): map A to bit 1
                int mask = rankMask;
                if ((rankMask & (1 << 14)) != 0) mask |= (1 << 1);
                for (int high = 14; high >= 5; high--)
                {
                    int need = 0;
                    for (int r = high; r > high - 5; r--) need |= (1 << r);
                    if ((mask & need) == need) return high;
                }
                return 0;
            }

            // --- Straight flush ---
            if (flushSuit >= 0)
            {
                int sfMask = 0;
                foreach (var c in cards)
                    if ((int)c.Suit == flushSuit) sfMask |= (1 << c.Rank);
                int sfHigh = BestStraightHigh(sfMask);
                if (sfHigh > 0)
                    return (Compose(HandCategory.StraightFlush, sfHigh, 0, 0, 0, 0), HandCategory.StraightFlush);
            }

            // Group ranks by multiplicity (descending count, then rank)
            var quads = new List<int>();
            var trips = new List<int>();
            var pairs = new List<int>();
            var singles = new List<int>();
            for (int r = 14; r >= 2; r--)
            {
                switch (rankCount[r])
                {
                    case 4: quads.Add(r); break;
                    case 3: trips.Add(r); break;
                    case 2: pairs.Add(r); break;
                    case 1: singles.Add(r); break;
                }
            }

            // --- Four of a kind ---
            if (quads.Count > 0)
            {
                int quad = quads[0];
                int kicker = HighestExcept(rankCount, quad);
                return (Compose(HandCategory.FourOfAKind, quad, kicker, 0, 0, 0), HandCategory.FourOfAKind);
            }

            // --- Full house (trips + pair, or two trips) ---
            if (trips.Count >= 2)
                return (Compose(HandCategory.FullHouse, trips[0], trips[1], 0, 0, 0), HandCategory.FullHouse);
            if (trips.Count == 1 && pairs.Count > 0)
                return (Compose(HandCategory.FullHouse, trips[0], pairs[0], 0, 0, 0), HandCategory.FullHouse);

            // --- Flush ---
            if (flushSuit >= 0)
            {
                var flushRanks = cards.Where(c => (int)c.Suit == flushSuit)
                                      .Select(c => c.Rank)
                                      .OrderByDescending(r => r)
                                      .Take(5)
                                      .ToArray();
                return (Compose(HandCategory.Flush, flushRanks[0], flushRanks[1], flushRanks[2], flushRanks[3], flushRanks[4]), HandCategory.Flush);
            }

            // --- Straight ---
            int allMask = 0;
            for (int r = 2; r <= 14; r++)
                if (rankCount[r] > 0) allMask |= (1 << r);
            int straightHigh = BestStraightHigh(allMask);
            if (straightHigh > 0)
                return (Compose(HandCategory.Straight, straightHigh, 0, 0, 0, 0), HandCategory.Straight);

            // --- Three of a kind ---
            if (trips.Count == 1)
            {
                var kickers = singles.Take(2).ToArray();
                return (Compose(HandCategory.ThreeOfAKind, trips[0], At(kickers, 0), At(kickers, 1), 0, 0), HandCategory.ThreeOfAKind);
            }

            // --- Two pair ---
            if (pairs.Count >= 2)
            {
                int kicker = 0;
                // Best kicker among remaining cards (third pair's rank can also serve as kicker)
                for (int r = 14; r >= 2; r--)
                {
                    if (r == pairs[0] || r == pairs[1]) continue;
                    if (rankCount[r] > 0) { kicker = r; break; }
                }
                return (Compose(HandCategory.TwoPair, pairs[0], pairs[1], kicker, 0, 0), HandCategory.TwoPair);
            }

            // --- One pair ---
            if (pairs.Count == 1)
            {
                var kickers = singles.Take(3).ToArray();
                return (Compose(HandCategory.Pair, pairs[0], At(kickers, 0), At(kickers, 1), At(kickers, 2), 0), HandCategory.Pair);
            }

            // --- High card ---
            var top = singles.Take(5).ToArray();
            return (Compose(HandCategory.HighCard, At(top, 0), At(top, 1), At(top, 2), At(top, 3), At(top, 4)), HandCategory.HighCard);
        }

        private static int At(int[] arr, int i) => i < arr.Length ? arr[i] : 0;

        private static int HighestExcept(Span<int> rankCount, int except)
        {
            for (int r = 14; r >= 2; r--)
                if (r != except && rankCount[r] > 0) return r;
            return 0;
        }

        private static long Compose(HandCategory cat, int k1, int k2, int k3, int k4, int k5) =>
            ((long)cat << 20) | ((long)k1 << 16) | ((long)k2 << 12) | ((long)k3 << 8) | ((long)k4 << 4) | (long)k5;
    }
}
