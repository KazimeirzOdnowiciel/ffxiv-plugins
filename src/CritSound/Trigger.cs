namespace CritSound;

/// <summary>
/// The three hit types this plugin can react to.
/// </summary>
public enum CritTrigger
{
    /// <summary>A critical hit that was not a direct hit.</summary>
    Crit,

    /// <summary>A direct hit that was not a critical hit.</summary>
    DirectHit,

    /// <summary>A hit that was both critical and a direct hit.</summary>
    CritDirectHit,
}
