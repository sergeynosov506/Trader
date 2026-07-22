using System;
using System.Collections.Generic;
using System.Linq;
using EconomicGame.Models;
using EconomicGame.Models.Poker;

namespace EconomicGame.Services
{
    /// <summary>
    /// Decision engine for poker bots. No neural nets — Chen formula preflop,
    /// Monte Carlo equity postflop, and the bot's PERSONALITY comes straight
    /// from its GeneticStrategy genes:
    ///   RiskTolerance   → loose/tight (how wide it plays hands)
    ///   AggressionLevel → passive/aggressive (bet/raise vs check/call)
    ///   TradeEntropy    → bluff frequency and unpredictability
    ///   TimeHorizon     → patience (folds more, waits for premium spots)
    ///   Sentiment       → tilt: negative sentiment loosens and enrages the bot
    /// Drunk bots (IntoxicationLevel) add decision noise.
    /// </summary>
    public static class PokerBotBrain
    {
        public class Decision
        {
            public PokerActionType Action { get; set; }
            /// <summary>Target TOTAL street commitment for Bet/Raise.</summary>
            public decimal BetTo { get; set; }
        }

        /// <summary>
        /// Chen formula for starting hand strength. Range roughly -1..20.
        /// </summary>
        public static double ChenScore(Card a, Card b)
        {
            int hi = Math.Max(a.Rank, b.Rank);
            int lo = Math.Min(a.Rank, b.Rank);

            double score = hi switch
            {
                14 => 10,
                13 => 8,
                12 => 7,
                11 => 6,
                _ => hi / 2.0
            };

            if (hi == lo) // pocket pair
            {
                score = Math.Max(5, score * 2);
                return score;
            }

            if (a.Suit == b.Suit) score += 2;

            int gap = hi - lo - 1;
            score -= gap switch
            {
                0 => 0,
                1 => 1,
                2 => 2,
                3 => 4,
                _ => 5
            };

            // Straight-bonus for connected low cards
            if (gap <= 1 && hi < 12) score += 1;

            return score;
        }

        /// <summary>
        /// Monte Carlo equity: probability that our hand wins (ties count as half)
        /// against opponentCount random hands, given the current board.
        /// </summary>
        public static double Equity(Card[] hole, IReadOnlyList<Card> board, int opponentCount, int iterations, Random rng)
        {
            if (opponentCount <= 0) return 1.0;

            var known = new HashSet<Card>(hole);
            foreach (var c in board) known.Add(c);

            // Remaining deck
            var pool = new List<Card>(52 - known.Count);
            for (int s = 0; s < 4; s++)
                for (int r = 2; r <= 14; r++)
                {
                    var c = new Card(r, (CardSuit)s);
                    if (!known.Contains(c)) pool.Add(c);
                }

            double wins = 0;
            int boardNeed = 5 - board.Count;
            var sample = new Card[pool.Count];

            for (int it = 0; it < iterations; it++)
            {
                // Partial Fisher-Yates: draw boardNeed + 2*opponentCount cards
                pool.CopyTo(sample);
                int need = boardNeed + 2 * opponentCount;
                for (int i = 0; i < need; i++)
                {
                    int j = rng.Next(i, sample.Length);
                    (sample[i], sample[j]) = (sample[j], sample[i]);
                }

                var fullBoard = new List<Card>(5);
                fullBoard.AddRange(board);
                for (int i = 0; i < boardNeed; i++) fullBoard.Add(sample[i]);

                var myCards = new List<Card>(7);
                myCards.AddRange(hole);
                myCards.AddRange(fullBoard);
                var (myScore, _) = HandEvaluator.Evaluate(myCards);

                bool lost = false;
                int ties = 0;
                int idx = boardNeed;
                for (int o = 0; o < opponentCount; o++)
                {
                    var oppCards = new List<Card>(7);
                    oppCards.Add(sample[idx++]);
                    oppCards.Add(sample[idx++]);
                    oppCards.AddRange(fullBoard);
                    var (oppScore, _) = HandEvaluator.Evaluate(oppCards);
                    if (oppScore > myScore) { lost = true; break; }
                    if (oppScore == myScore) ties++;
                }

                if (!lost)
                    wins += ties > 0 ? 1.0 / (ties + 1) : 1.0;
            }

            return wins / iterations;
        }

        /// <summary>
        /// Make a decision for the bot seat. intoxicationLevel adds noise (drunk bots misplay).
        /// </summary>
        public static Decision Decide(
            PokerHand hand,
            PokerSeat seat,
            GeneticStrategy strat,
            int intoxicationLevel,
            Random rng,
            int mcIterations = 400)
        {
            var toCall = hand.AmountToCall(seat);
            var pot = hand.TotalPot;
            int liveOpponents = hand.Seats.Count(s => s.IsLive && s != seat);

            // --- Personality knobs from genes ---
            double loose = Clamp01((double)strat.RiskTolerance * 0.5);          // 0..1: how wide we play
            double aggr = Clamp01(strat.AggressionLevel * 1.5 + 0.15);          // base aggression
            double bluffFreq = Clamp01(strat.TradeEntropy * 0.18);              // 0..~0.18
            double patience = Clamp01((double)strat.TimeHorizon * 0.5);         // folds marginal spots
            double tilt = Math.Max(0, -strat.Sentiment);                        // 0..1 — bad mood = tilt

            // Tilt loosens and enrages
            loose = Clamp01(loose + tilt * 0.35);
            aggr = Clamp01(aggr + tilt * 0.35);
            bluffFreq = Clamp01(bluffFreq + tilt * 0.10);

            // --- Estimate hand strength ---
            double strength; // 0..1
            if (hand.Street == PokerStreet.Preflop)
            {
                strength = Clamp01(ChenScore(seat.HoleCards[0], seat.HoleCards[1]) / 20.0);
            }
            else
            {
                strength = Equity(seat.HoleCards, hand.Board, liveOpponents, mcIterations, rng);
            }

            // Drunk noise: misjudge strength
            if (intoxicationLevel > 0)
            {
                double noise = (rng.NextDouble() * 2 - 1) * 0.08 * intoxicationLevel;
                strength = Clamp01(strength + noise);
            }

            // --- Decision ---
            if (toCall <= 0)
            {
                // No bet to face: check or bet
                double betThreshold = 0.5 - loose * 0.12 + patience * 0.08;
                bool valueBet = strength > betThreshold && rng.NextDouble() < aggr + strength * 0.35;
                bool bluff = strength < 0.35 && rng.NextDouble() < bluffFreq;

                if (valueBet || bluff)
                {
                    decimal target = BetSize(hand, seat, pot, strength, aggr, rng);
                    if (target > 0)
                        return new Decision { Action = PokerActionType.Bet, BetTo = target };
                }
                return new Decision { Action = PokerActionType.Check };
            }
            else
            {
                // Facing a bet: pot odds
                double potOdds = (double)(toCall / (pot + toCall));
                // Required equity, adjusted by personality: loose bots call wider,
                // patient bots want more margin.
                double required = potOdds * (1.0 + patience * 0.35 - loose * 0.30);

                // Hopeless?
                if (strength < required * 0.75 && rng.NextDouble() > bluffFreq * 0.5)
                    return new Decision { Action = PokerActionType.Fold };

                // Strong hand or bluff-raise?
                double raiseThreshold = 0.62 - aggr * 0.10;
                bool valueRaise = strength > raiseThreshold && rng.NextDouble() < aggr;
                bool bluffRaise = strength < 0.35 && rng.NextDouble() < bluffFreq * 0.6;

                if ((valueRaise || bluffRaise) && seat.Stack > toCall)
                {
                    decimal target = Math.Max(hand.MinRaiseTo, BetSize(hand, seat, pot, strength, aggr, rng));
                    return new Decision { Action = PokerActionType.Raise, BetTo = target };
                }

                if (strength >= required || toCall < hand.BigBlind * 1.5m)
                    return new Decision { Action = PokerActionType.Call };

                return new Decision { Action = PokerActionType.Fold };
            }
        }

        private static decimal BetSize(PokerHand hand, PokerSeat seat, decimal pot, double strength, double aggr, Random rng)
        {
            // Bet between ~40% and ~110% of the pot depending on strength/aggression
            double frac = 0.4 + strength * 0.4 + aggr * 0.3 + (rng.NextDouble() - 0.5) * 0.2;
            decimal size = pot * (decimal)Math.Clamp(frac, 0.3, 1.2);
            size = Math.Max(hand.BigBlind, Math.Round(size));

            decimal target = seat.StreetCommitted + Math.Min(size, seat.Stack);
            // Very strong + very aggressive: sometimes shove
            if (strength > 0.85 && rng.NextDouble() < aggr * 0.5)
                target = seat.StreetCommitted + seat.Stack;
            return target;
        }

        private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);
    }
}
