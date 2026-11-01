using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Mountbook.Services;

namespace Mountbook;

/// <summary>
/// Chooses the interface language once, at load, and installs the table the rest of the plugin reads
/// through <see cref="Loc"/>.
/// </summary>
/// <remarks>
/// <para>
/// The language is Dalamud's own UI language (<see cref="IDalamudPluginInterface.UiLanguage"/>), not the
/// game client's: a player on an English client can run the interface in Japanese, and that is the setting
/// they expect a plugin to follow. It is read once, at construction - changing it in Dalamud's settings
/// takes effect when the plugin is reloaded, which is the same reload a language switch already needs for
/// every cached text measurement in the views.
/// </para>
/// <para>
/// A language with no table is not an error: <see cref="Loc"/> falls back to English, and the log says which
/// language was asked for and how many strings it found, so a translator can see what is missing without a
/// debug build.
/// </para>
/// </remarks>
internal static class Localisation
{
    /// <summary>Reads the UI language, installs the tables and selects the one to draw in.</summary>
    /// <param name="pluginInterface">Dalamud's plugin interface, for <c>UiLanguage</c>.</param>
    /// <param name="log">Where the outcome is reported.</param>
    public static void Apply(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        ArgumentNullException.ThrowIfNull(pluginInterface);
        ArgumentNullException.ThrowIfNull(log);

        Loc.Scan((name, ex) => log.Warning(ex, "Skipped the string table '{Name}': it did not parse.", name));

        InterfaceLanguage language = InterfaceLanguageRules.FromUiLanguage(pluginInterface.UiLanguage);

        Loc.Use(language);

        // One line at load, at Information: it makes "which table is this build drawing, and how complete
        // is it" checkable from a user's log without a debug build or a language switch to reproduce.
        if (Loc.Count == 0)
        {
            log.Information(
                "Interface language '{Code}' has no string table yet; drawing English ({Total} strings).",
                InterfaceLanguageRules.CodeOf(language),
                Loc.FallbackCount);
            return;
        }

        log.Information(
            "Interface language {Code}: {Count} of {Total} strings.",
            InterfaceLanguageRules.CodeOf(language),
            Loc.Count,
            Loc.FallbackCount);
    }
}
