using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RouletteBingo.Core;

namespace RouletteBingo.UI;

public static class Theme
{
    public static readonly Vector4 Background = new(0.043f, 0.051f, 0.067f, 0.98f);
    public static readonly Vector4 Panel = new(0.082f, 0.094f, 0.118f, 1f);
    public static readonly Vector4 PanelHover = new(0.118f, 0.134f, 0.165f, 1f);
    public static readonly Vector4 Border = new(1f, 1f, 1f, 0.07f);
    public static readonly Vector4 Text = new(0.88f, 0.90f, 0.94f, 1f);
    public static readonly Vector4 TextDim = new(0.55f, 0.59f, 0.66f, 1f);
    public static readonly Vector4 TextFaint = new(0.38f, 0.41f, 0.47f, 1f);
    public static readonly Vector4 Gold = new(0.87f, 0.72f, 0.35f, 1f);
    public static readonly Vector4 Danger = new(0.90f, 0.35f, 0.40f, 1f);

    public static uint U32(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);

    public static uint WithAlpha(Vector4 color, float alpha) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, color.W * alpha));

    public static Vector4 Lerp(Vector4 a, Vector4 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector4(
            a.X + ((b.X - a.X) * t),
            a.Y + ((b.Y - a.Y) * t),
            a.Z + ((b.Z - a.Z) * t),
            a.W + ((b.W - a.W) * t));
    }

    public static Vector4 CategoryColor(BingoCategory category) => category switch
    {
        BingoCategory.Free => Gold,
        BingoCategory.Tank => new Vector4(0.38f, 0.58f, 0.85f, 1f),
        BingoCategory.Healer => new Vector4(0.36f, 0.76f, 0.55f, 1f),
        BingoCategory.Damage => new Vector4(0.86f, 0.42f, 0.42f, 1f),
        BingoCategory.Social => new Vector4(0.66f, 0.52f, 0.88f, 1f),
        BingoCategory.Loot => new Vector4(0.90f, 0.68f, 0.30f, 1f),
        BingoCategory.Sprout => new Vector4(0.62f, 0.82f, 0.38f, 1f),
        BingoCategory.Cursed => new Vector4(0.90f, 0.32f, 0.55f, 1f),
        BingoCategory.Pvp => new Vector4(0.88f, 0.52f, 0.26f, 1f),
        BingoCategory.Alliance => new Vector4(0.30f, 0.72f, 0.72f, 1f),
        _ => TextDim,
    };

    public static Vector4 RarityColor(BingoRarity rarity) => rarity switch
    {
        BingoRarity.Common => new Vector4(0.60f, 0.63f, 0.69f, 1f),
        BingoRarity.Uncommon => new Vector4(0.44f, 0.78f, 0.50f, 1f),
        BingoRarity.Rare => new Vector4(0.42f, 0.66f, 0.94f, 1f),
        _ => new Vector4(0.91f, 0.35f, 0.62f, 1f),
    };

    public static int RarityPips(BingoRarity rarity) => rarity switch
    {
        BingoRarity.Common => 1,
        BingoRarity.Uncommon => 2,
        BingoRarity.Rare => 3,
        _ => 4,
    };

    public static string RarityName(BingoRarity rarity) => rarity switch
    {
        BingoRarity.Common => "Common",
        BingoRarity.Uncommon => "Uncommon",
        BingoRarity.Rare => "Rare",
        _ => "Cursed",
    };
}
