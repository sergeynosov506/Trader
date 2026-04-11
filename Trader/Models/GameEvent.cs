using System;
using System.Collections.Generic;

namespace EconomicGame
{
    /// <summary>
    /// Interactive game event that requires player choice.
    /// </summary>
    public class GameEvent
    {
        public Guid EventId { get; set; } = Guid.NewGuid();
        public required string Title { get; set; }
        public required string Description { get; set; }
        public List<EventChoice> Choices { get; set; } = new List<EventChoice>();
        public DateTime ExpiresAt { get; set; }
        public Guid? TargetPlayerId { get; set; }  // null = global event
        public bool IsExpired { get; set; }
        public string? OutcomeMessage { get; set; }
    }

    /// <summary>
    /// A choice option for a GameEvent.
    /// </summary>
    public class EventChoice
    {
        public int ChoiceId { get; set; }
        public required string Text { get; set; }
        public decimal? Cost { get; set; }
        public required string OutcomeDescription { get; set; }
        
        // Effects when this choice is selected
        public decimal? MoneyChange { get; set; }
        public int? ReputationChange { get; set; }
        public string? ItemReward { get; set; }
        public int? ItemQuantity { get; set; }
    }
}
