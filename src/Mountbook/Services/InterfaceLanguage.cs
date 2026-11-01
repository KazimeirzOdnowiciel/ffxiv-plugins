using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// The interface languages Mountbook can be read in. This is the plugin's own list, not Dalamud's: the
/// Dalamud enum lives in an assembly the Dalamud-free tests cannot reference, and the mapping between the
/// two is one switch in the plugin (see <c>StringResources</c>).
/// </summary>
/// <remarks>
/// The order is Dalamud's <c>ClientLanguage</c> order so the two read side by side, and the codes are the
/// suffixes of the table files under <c>Data/Strings</c> (<c>en.json</c>, <c>ja.json</c>, ...).
/// </remarks>
public enum InterfaceLanguage
{
    /// <summary>Japanese.</summary>
    Japanese,

    /// <summary>English. The table that ships complete, and the fallback for every other language.</summary>
    English,

    /// <summary>German.</summary>
    German,

    /// <summary>French.</summary>
    French,

    /// <summary>Simplified Chinese.</summary>
    ChineseSimplified,

    /// <summary>Traditional Chinese.</summary>
    ChineseTraditional,

    /// <summary>Korean.</summary>
    Korean,
}

/// <summary>The table-file codes, and the fallback language.</summary>
public static class InterfaceLanguageRules
{
    /// <summary>
    /// The language every other table falls back to: a key a translation has not reached yet reads in
    /// English rather than as its own name.
    /// </summary>
    public const InterfaceLanguage Fallback = InterfaceLanguage.English;

    private static readonly (InterfaceLanguage Language, string Code)[] Table =
    [
        (InterfaceLanguage.Japanese, "ja"),
        (InterfaceLanguage.English, "en"),
        (InterfaceLanguage.German, "de"),
        (InterfaceLanguage.French, "fr"),
        (InterfaceLanguage.ChineseSimplified, "zh-Hans"),
        (InterfaceLanguage.ChineseTraditional, "zh-Hant"),
        (InterfaceLanguage.Korean, "ko"),
    ];

    /// <summary>Every code a table file may carry, in the order they are listed above.</summary>
    public static IReadOnlyList<string> Codes
    {
        get
        {
            var codes = new string[Table.Length];

            for (var index = 0; index < Table.Length; index++)
            {
                codes[index] = Table[index].Code;
            }

            return codes;
        }
    }

    /// <summary>The file code for a language, e.g. <c>ja</c>.</summary>
    /// <param name="language">The language.</param>
    /// <returns>The code that names its table file.</returns>
    public static string CodeOf(InterfaceLanguage language)
    {
        foreach ((InterfaceLanguage candidate, string code) in Table)
        {
            if (candidate == language)
            {
                return code;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(language), language, "No table code for that language.");
    }

    /// <summary>
    /// The language a Dalamud UI language code names. Dalamud reports two letters (<c>ja</c>, <c>de</c>), so
    /// <c>zh</c> alone means Simplified Chinese and anything unrecognised reads English.
    /// </summary>
    /// <param name="code">A code from Dalamud's <c>IPluginInterface.UiLanguage</c>.</param>
    /// <returns>The language to draw in.</returns>
    public static InterfaceLanguage FromUiLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Fallback;
        }

        return FromCode(code) ?? (string.Equals(code.Trim(), "zh", StringComparison.OrdinalIgnoreCase)
            ? InterfaceLanguage.ChineseSimplified
            : Fallback);
    }

    /// <summary>The language a table file code names, or null when the code is not one of ours.</summary>
    /// <param name="code">A code such as <c>ja</c> or <c>zh-Hans</c>, matched case-insensitively.</param>
    /// <returns>The language, or null.</returns>
    public static InterfaceLanguage? FromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        foreach ((InterfaceLanguage language, string candidate) in Table)
        {
            if (string.Equals(candidate, code.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        return null;
    }
}
