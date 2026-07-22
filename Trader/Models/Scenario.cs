using System;

namespace EconomicGame.Models
{
    /// <summary>
    /// Win/loss status of a scenario the player is currently running.
    /// </summary>
    public enum ScenarioStatus
    {
        None,      // No scenario active / freeplay
        Active,    // Scenario running, not yet resolved
        Won,       // Goal reached within time limit
        Lost       // Time limit expired before goal was reached, or fail condition hit
    }

    /// <summary>
    /// Kinds of goals a scenario can set. Extend here when adding new objectives.
    /// </summary>
    public enum ScenarioGoalType
    {
        /// <summary>
        /// Stay alive (NetWorth &gt;= Threshold) for the full <see cref="Scenario.TimeLimitDays"/>.
        /// Typical use: "Survive 7 days with $50K debt" — Threshold=0 means don't go negative.
        /// </summary>
        SurviveWithNetWorthAbove,

        /// <summary>
        /// Reach a target NetWorth within the time limit.
        /// Typical use: "Millionaire in 30 days" — Threshold=1,000,000.
        /// </summary>
        ReachNetWorth,

        /// <summary>
        /// Reach a target cash balance within the time limit.
        /// </summary>
        ReachMoney,

        /// <summary>
        /// Reach a target quantity of a specific item in inventory within the time limit.
        /// </summary>
        ReachItemStock
    }

    /// <summary>
    /// Single win condition tied to a scenario.
    /// </summary>
    public class ScenarioGoal
    {
        public ScenarioGoalType Type { get; set; }
        public decimal Threshold { get; set; }
        public string? ItemName { get; set; } // Target item name (e.g. "Sugar")
    }

    /// <summary>
    /// Configurable game scenario loaded from appsettings.json ("Scenarios" section).
    /// Identified by <see cref="Id"/>; all human-readable fields are translation keys
    /// so scenarios localize through LocalizationService like the rest of the UI.
    /// </summary>
    public class Scenario
    {
        /// <summary>Stable id, used to reference the scenario from Player.ActiveScenarioId.</summary>
        public required string Id { get; set; }

        /// <summary>Translation key for the scenario name, e.g. "scenario.survive_debt.name".</summary>
        public required string NameKey { get; set; }

        /// <summary>Translation key for the scenario description / flavor text.</summary>
        public required string DescriptionKey { get; set; }

        /// <summary>Starting cash override. If null, use GameConstants.InitialPlayerMoney.</summary>
        public decimal? StartingMoney { get; set; }

        /// <summary>Size of the starting loan (0 = no loan).</summary>
        public decimal StartingLoan { get; set; } = 0;

        /// <summary>Months until the starting loan is due.</summary>
        public int StartingLoanMonths { get; set; } = 1;

        /// <summary>Scenario time limit in game days.</summary>
        public int TimeLimitDays { get; set; } = 7;

        /// <summary>Win condition.</summary>
        public required ScenarioGoal Goal { get; set; }
    }
}
