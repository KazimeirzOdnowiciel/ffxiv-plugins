using System.Numerics;
using Dalamud.Configuration;

namespace EnemyListCastBars;

/// <summary>
/// Persisted settings for the plugin.
/// </summary>
/// <remarks>
/// Positions and sizes are expressed in the enemy list row's own coordinate space,
/// where the origin is the top-left corner of a single row.
/// </remarks>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Default position of the cast bar within a row.</summary>
    public static readonly Vector2 DefaultBarPosition = new(86f, 16f);

    /// <summary>Default size of the cast bar.</summary>
    public static readonly Vector2 DefaultBarSize = new(120f, 20f);

    /// <summary>Default position of the cast name text box, centred on the cast bar.</summary>
    public static readonly Vector2 DefaultTextPosition = new(66f, 16f);

    /// <summary>Default size of the cast name text box.</summary>
    public static readonly Vector2 DefaultTextSize = new(160f, 20f);

    public int Version { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether the cast bars are drawn.</summary>
    public bool Enabled { get; set; } = true;

    public Vector2 BarPosition { get; set; } = DefaultBarPosition;

    public Vector2 BarSize { get; set; } = DefaultBarSize;

    /// <summary>Gets or sets the space between the bar frame and the progress fill.</summary>
    public float BarFillPadding { get; set; } = 3f;

    /// <summary>Gets or sets the fill colour used while the cast can be interrupted.</summary>
    public Vector4 InterruptibleBarColor { get; set; } = new(0.95f, 0.24f, 0.18f, 1f);

    /// <summary>Gets or sets the fill colour used while the cast cannot be interrupted.</summary>
    public Vector4 UninterruptibleBarColor { get; set; } = new(0.32f, 0.62f, 0.98f, 1f);

    public Vector4 BarBackgroundColor { get; set; } = new(0f, 0f, 0f, 0.75f);

    public bool ShowActionName { get; set; } = true;

    public bool ShowRemainingTime { get; set; } = true;

    public Vector2 TextPosition { get; set; } = DefaultTextPosition;

    public Vector2 TextSize { get; set; } = DefaultTextSize;

    public byte FontSize { get; set; } = 10;

    public Vector4 TextColor { get; set; } = Vector4.One;

    public Vector4 TextOutlineColor { get; set; } = new(0f, 0f, 0f, 1f);

    /// <summary>Restores every visual setting to its default value.</summary>
    public void ResetToDefaults()
    {
        this.BarPosition = DefaultBarPosition;
        this.BarSize = DefaultBarSize;
        this.BarFillPadding = 3f;
        this.InterruptibleBarColor = new Vector4(0.95f, 0.24f, 0.18f, 1f);
        this.UninterruptibleBarColor = new Vector4(0.32f, 0.62f, 0.98f, 1f);
        this.BarBackgroundColor = new Vector4(0f, 0f, 0f, 0.75f);
        this.TextPosition = DefaultTextPosition;
        this.TextSize = DefaultTextSize;
        this.FontSize = 10;
        this.TextColor = Vector4.One;
        this.TextOutlineColor = new Vector4(0f, 0f, 0f, 1f);
    }
}
