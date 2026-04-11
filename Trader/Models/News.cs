using System;

namespace EconomicGame
{
    public enum NewsType
    {
        Rumor,      // Delayed effect, may or may not happen
        Breaking,   // Immediate effect
        Confirmed   // Confirmation of a previous rumor
    }

    public class News
    {
        public Guid NewsId { get; set; } = Guid.NewGuid();
        public required string Title { get; set; }
        public required string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal MarketImpact { get; set; }
        public string? AffectedItemName { get; set; }
        
        // New fields for rumor system
        public NewsType Type { get; set; } = NewsType.Breaking;
        public DateTime? EffectTime { get; set; }     // null = immediate effect
        public bool IsApplied { get; set; }           // Has the effect been applied?
        public double ConfirmationChance { get; set; } = 1.0; // Probability rumor comes true
    }
}