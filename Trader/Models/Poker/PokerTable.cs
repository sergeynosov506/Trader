using System;
using System.Collections.Generic;
using System.Linq;

namespace EconomicGame.Models.Poker
{
    public enum PokerStreet { Preflop, Flop, Turn, River, Showdown, HandOver }

    public enum PokerActionType { Fold, Check, Call, Bet, Raise, AllIn }

    /// <summary>
    /// A seat at the poker table. Stack is table chips (escrowed from the player's real money
    /// by PokerService; the table itself only moves chips).
    /// </summary>
    public class PokerSeat
    {
        public Guid PlayerId { get; set; }
        public string Name { get; set; } = "";
        public bool IsHuman { get; set; }
        public decimal Stack { get; set; }

        public Card[] HoleCards { get; set; } = Array.Empty<Card>();
        public bool HasFolded { get; set; }
        public bool IsAllIn { get; set; }
        public bool HasActedThisRound { get; set; }

        /// <summary>Chips committed on the current betting street.</summary>
        public decimal StreetCommitted { get; set; }
        /// <summary>Total chips committed during the whole hand (for side pots).</summary>
        public decimal TotalCommitted { get; set; }

        /// <summary>Still contesting the pot (didn't fold, has cards).</summary>
        public bool IsLive => !HasFolded && HoleCards.Length == 2;

        /// <summary>Can still make decisions (live and not all-in).</summary>
        public bool CanAct => IsLive && !IsAllIn && Stack > 0;
    }

    public class PokerActionRecord
    {
        public Guid PlayerId { get; set; }
        public string Name { get; set; } = "";
        public PokerActionType Action { get; set; }
        public decimal Amount { get; set; }
        public PokerStreet Street { get; set; }
    }

    public class PotResult
    {
        public Guid PlayerId { get; set; }
        public string Name { get; set; } = "";
        public decimal AmountWon { get; set; }
        public HandCategory? Category { get; set; }
        public bool WonWithoutShowdown { get; set; }
    }

    /// <summary>
    /// No-limit Texas Hold'em hand state machine for 2-6 players.
    /// Pure chip logic: no knowledge of the game's economy, bots, or UI.
    /// Betting flow: call AdvanceUntil* helpers from the orchestrating service;
    /// the table exposes WhoseTurn and accepts Apply(action).
    /// </summary>
    public class PokerHand
    {
        private readonly Random _rng;
        private readonly Deck _deck;

        public List<PokerSeat> Seats { get; }
        public List<Card> Board { get; } = new(5);
        public PokerStreet Street { get; private set; } = PokerStreet.Preflop;
        public decimal SmallBlind { get; }
        public decimal BigBlind { get; }
        public int DealerIndex { get; }
        public List<PokerActionRecord> ActionLog { get; } = new();
        public List<PotResult> Results { get; } = new();

        /// <summary>Highest total committed on the current street (the amount to match).</summary>
        public decimal CurrentBet { get; private set; }
        /// <summary>Size of the last bet/raise increment (for min-raise rules).</summary>
        public decimal LastRaiseSize { get; private set; }

        private int _turnIndex = -1;

        public decimal TotalPot => Seats.Sum(s => s.TotalCommitted);

        public PokerHand(List<PokerSeat> seats, int dealerIndex, decimal smallBlind, decimal bigBlind, Random? rng = null)
        {
            if (seats.Count < 2 || seats.Count > 6)
                throw new ArgumentException("Poker table supports 2-6 players");
            Seats = seats;
            DealerIndex = dealerIndex % seats.Count;
            SmallBlind = smallBlind;
            BigBlind = bigBlind;
            _rng = rng ?? Random.Shared;
            _deck = new Deck(_rng);

            // Deal hole cards
            foreach (var seat in Seats)
            {
                seat.HoleCards = new[] { _deck.Draw(), _deck.Draw() };
                seat.HasFolded = false;
                seat.IsAllIn = false;
                seat.StreetCommitted = 0;
                seat.TotalCommitted = 0;
                seat.HasActedThisRound = false;
            }

            // Post blinds. Heads-up: dealer is SB.
            int sbIndex = Seats.Count == 2 ? DealerIndex : NextIndex(DealerIndex);
            int bbIndex = NextIndex(sbIndex);

            Commit(Seats[sbIndex], Math.Min(SmallBlind, Seats[sbIndex].Stack));
            Commit(Seats[bbIndex], Math.Min(BigBlind, Seats[bbIndex].Stack));
            CurrentBet = BigBlind;
            LastRaiseSize = BigBlind;

            _turnIndex = NextActingIndex(bbIndex);
            if (_turnIndex < 0)
            {
                // Everyone is all-in from the blinds — run out the board
                RunOutIfNeeded();
            }
        }

        /// <summary>Index of the seat whose turn it is, or null when the street/hand is complete.</summary>
        public PokerSeat? WhoseTurn => _turnIndex >= 0 ? Seats[_turnIndex] : null;

        public decimal AmountToCall(PokerSeat seat) => Math.Max(0, CurrentBet - seat.StreetCommitted);

        /// <summary>Minimum total street commitment for a legal raise.</summary>
        public decimal MinRaiseTo => CurrentBet + LastRaiseSize;

        private int NextIndex(int from) => (from + 1) % Seats.Count;

        private int NextActingIndex(int from)
        {
            // Betting round ends when every live player has either matched CurrentBet
            // and acted, or is all-in.
            if (Seats.Count(s => s.IsLive) <= 1) return -1;

            int idx = from;
            for (int i = 0; i < Seats.Count; i++)
            {
                idx = NextIndex(idx);
                var s = Seats[idx];
                if (!s.CanAct) continue;
                if (!s.HasActedThisRound || s.StreetCommitted < CurrentBet)
                    return idx;
            }
            return -1;
        }

        private void Commit(PokerSeat seat, decimal amount)
        {
            amount = Math.Min(amount, seat.Stack);
            seat.Stack -= amount;
            seat.StreetCommitted += amount;
            seat.TotalCommitted += amount;
            if (seat.Stack <= 0) seat.IsAllIn = true;
        }

        /// <summary>
        /// Apply an action for the current seat. betTo is the TOTAL street commitment
        /// for Bet/Raise (not the increment). Returns the recorded action.
        /// </summary>
        public PokerActionRecord Apply(PokerActionType action, decimal betTo = 0)
        {
            if (_turnIndex < 0) throw new InvalidOperationException("No one to act");
            var seat = Seats[_turnIndex];
            var toCall = AmountToCall(seat);
            decimal recorded = 0;

            switch (action)
            {
                case PokerActionType.Fold:
                    seat.HasFolded = true;
                    break;

                case PokerActionType.Check:
                    if (toCall > 0) throw new InvalidOperationException("Cannot check facing a bet");
                    break;

                case PokerActionType.Call:
                    if (toCall <= 0) { action = PokerActionType.Check; break; }
                    recorded = Math.Min(toCall, seat.Stack);
                    Commit(seat, recorded);
                    if (seat.IsAllIn) action = PokerActionType.AllIn;
                    break;

                case PokerActionType.Bet:
                case PokerActionType.Raise:
                case PokerActionType.AllIn:
                    // Normalize: betTo is the target street commitment
                    decimal maxTo = seat.StreetCommitted + seat.Stack;
                    if (action == PokerActionType.AllIn) betTo = maxTo;
                    betTo = Math.Min(betTo, maxTo);

                    if (betTo <= CurrentBet)
                    {
                        // Not enough for a raise — treat as call (short all-in included)
                        recorded = Math.Min(toCall, seat.Stack);
                        Commit(seat, recorded);
                        action = seat.IsAllIn ? PokerActionType.AllIn : PokerActionType.Call;
                        break;
                    }

                    // Enforce min-raise unless it's an all-in for less
                    if (betTo < MinRaiseTo && betTo < maxTo)
                        betTo = Math.Min(MinRaiseTo, maxTo);

                    decimal increment = betTo - CurrentBet;
                    recorded = betTo - seat.StreetCommitted;
                    Commit(seat, recorded);

                    if (increment >= LastRaiseSize || betTo >= MinRaiseTo)
                    {
                        // Full raise re-opens the action
                        LastRaiseSize = increment;
                        foreach (var s in Seats)
                            if (s != seat && s.CanAct) s.HasActedThisRound = false;
                    }
                    CurrentBet = betTo;
                    if (seat.IsAllIn) action = PokerActionType.AllIn;
                    else action = CurrentBet > 0 && increment > 0 && ActionLog.Any(a => a.Street == Street && (a.Action == PokerActionType.Bet || a.Action == PokerActionType.Raise || a.Action == PokerActionType.AllIn))
                        ? PokerActionType.Raise : action;
                    break;
            }

            seat.HasActedThisRound = true;
            var record = new PokerActionRecord
            {
                PlayerId = seat.PlayerId,
                Name = seat.Name,
                Action = action,
                Amount = recorded,
                Street = Street
            };
            ActionLog.Add(record);

            AdvanceState();
            return record;
        }

        private void AdvanceState()
        {
            // Only one live player? Hand ends immediately, no showdown.
            if (Seats.Count(s => s.IsLive) <= 1)
            {
                FinishByFold();
                return;
            }

            _turnIndex = NextActingIndex(_turnIndex);
            if (_turnIndex >= 0) return;

            // Street complete
            foreach (var s in Seats)
            {
                s.StreetCommitted = 0;
                s.HasActedThisRound = false;
            }
            CurrentBet = 0;
            LastRaiseSize = BigBlind;

            switch (Street)
            {
                case PokerStreet.Preflop:
                    Board.Add(_deck.Draw()); Board.Add(_deck.Draw()); Board.Add(_deck.Draw());
                    Street = PokerStreet.Flop;
                    break;
                case PokerStreet.Flop:
                    Board.Add(_deck.Draw());
                    Street = PokerStreet.Turn;
                    break;
                case PokerStreet.Turn:
                    Board.Add(_deck.Draw());
                    Street = PokerStreet.River;
                    break;
                case PokerStreet.River:
                    Street = PokerStreet.Showdown;
                    ResolveShowdown();
                    return;
            }

            // First to act post-flop: left of the dealer
            _turnIndex = NextActingIndex(DealerIndex);
            if (_turnIndex < 0)
            {
                // Everyone all-in — run out the rest of the board
                RunOutIfNeeded();
            }
        }

        private void RunOutIfNeeded()
        {
            while (Street != PokerStreet.Showdown && Street != PokerStreet.HandOver)
            {
                switch (Street)
                {
                    case PokerStreet.Preflop:
                        Board.Add(_deck.Draw()); Board.Add(_deck.Draw()); Board.Add(_deck.Draw());
                        Street = PokerStreet.Flop;
                        break;
                    case PokerStreet.Flop:
                        Board.Add(_deck.Draw());
                        Street = PokerStreet.Turn;
                        break;
                    case PokerStreet.Turn:
                        Board.Add(_deck.Draw());
                        Street = PokerStreet.River;
                        break;
                    case PokerStreet.River:
                        Street = PokerStreet.Showdown;
                        ResolveShowdown();
                        return;
                }
            }
        }

        private void FinishByFold()
        {
            var winner = Seats.FirstOrDefault(s => s.IsLive);
            if (winner != null)
            {
                var pot = TotalPot;
                winner.Stack += pot;
                Results.Add(new PotResult
                {
                    PlayerId = winner.PlayerId,
                    Name = winner.Name,
                    AmountWon = pot,
                    WonWithoutShowdown = true
                });
            }
            Street = PokerStreet.HandOver;
            _turnIndex = -1;
        }

        private void ResolveShowdown()
        {
            _turnIndex = -1;

            // Evaluate live hands
            var live = Seats.Where(s => s.IsLive).ToList();
            var scores = new Dictionary<PokerSeat, (long score, HandCategory cat)>();
            foreach (var s in live)
            {
                var cards = new List<Card>(7);
                cards.AddRange(s.HoleCards);
                cards.AddRange(Board);
                scores[s] = HandEvaluator.Evaluate(cards);
            }

            // Build side pots from commitment layers
            var contributions = Seats.Where(s => s.TotalCommitted > 0)
                                     .ToDictionary(s => s, s => s.TotalCommitted);
            var winnings = Seats.ToDictionary(s => s, _ => 0m);

            while (contributions.Values.Any(v => v > 0))
            {
                decimal layer = contributions.Where(kv => kv.Value > 0).Min(kv => kv.Value);
                decimal pot = 0;
                var layerContributors = new List<PokerSeat>();
                foreach (var kv in contributions.ToList())
                {
                    if (kv.Value <= 0) continue;
                    pot += layer;
                    contributions[kv.Key] = kv.Value - layer;
                    layerContributors.Add(kv.Key);
                }

                // Winners of this layer: best live hand among contributors
                var eligible = layerContributors.Where(s => s.IsLive).ToList();
                if (!eligible.Any())
                {
                    // Everyone in this layer folded (dead money) — goes to overall best hand
                    eligible = live;
                }
                long best = eligible.Max(s => scores[s].score);
                var potWinners = eligible.Where(s => scores[s].score == best).ToList();

                decimal share = Math.Floor(pot / potWinners.Count * 100m) / 100m;
                decimal distributed = 0;
                foreach (var w in potWinners)
                {
                    winnings[w] += share;
                    distributed += share;
                }
                // Odd cents go to the first winner after the dealer
                if (pot - distributed > 0)
                    winnings[potWinners[0]] += pot - distributed;
            }

            foreach (var kv in winnings.Where(kv => kv.Value > 0))
            {
                kv.Key.Stack += kv.Value;
                Results.Add(new PotResult
                {
                    PlayerId = kv.Key.PlayerId,
                    Name = kv.Key.Name,
                    AmountWon = kv.Value,
                    Category = scores.TryGetValue(kv.Key, out var sc) ? sc.cat : null
                });
            }

            Street = PokerStreet.HandOver;
        }

        public bool IsOver => Street == PokerStreet.HandOver;
    }
}
