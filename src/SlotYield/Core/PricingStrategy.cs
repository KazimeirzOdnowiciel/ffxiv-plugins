namespace SlotYield.Core;

/// <summary>Where in the price ladder you choose to sit.</summary>
public enum PricingStrategy
{
    /// <summary>List at the current cheapest price and queue behind everyone already there.</summary>
    MatchMinimum,

    /// <summary>Undercut the cheapest listing by one gil.</summary>
    UndercutMinimum,

    /// <summary>List at the average sale price, behind every cheaper seller.</summary>
    MatchAverageSale,
}
