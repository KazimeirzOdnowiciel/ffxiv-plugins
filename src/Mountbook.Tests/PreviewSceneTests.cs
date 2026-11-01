using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// Guards the one rule the preview scenes have to keep: a scene draws the window's own panel before its
/// body, because the preview's window is created with <c>NoBackground</c> and paints nothing itself.
/// </summary>
/// <remarks>
/// <para>
/// This is a source-text test rather than a rendering one, and on purpose. The previews assembly needs
/// Dalamud, so this project - which compiles without it (T1) - cannot load it or reflect over its scenes.
/// The sources are embedded as text instead (see the csproj's <c>Previews</c> resource), which is the same
/// mechanism the localisation guard uses for the views.
/// </para>
/// <para>
/// Missing the panel is not a subtle defect, it is the one that shipped: with no background behind them the
/// 23 scenes that skipped it left the frame buffer's alpha inside the window rect, the previewer composited
/// its own backdrop there, and at the bottom right of that backdrop sits the grey checker it draws as a
/// reference pattern for the glass blur. Below the flagship size the card stops covering that corner, so
/// <c>narrow.png</c> and <c>minimum.png</c> carried what read as a checkerboard strip of unpainted window -
/// and both were re-baselined with the fix. What makes it not come back is <c>PreviewScene</c>, whose
/// <c>Draw</c> is not virtual and paints the panel before calling the scene's body; these assertions keep a
/// new scene from sidestepping it.
/// </para>
/// </remarks>
public sealed class PreviewSceneTests
{
    /// <summary>The resource prefix the previews sources are embedded under.</summary>
    private const string SourcePrefix = "Mountbook.Tests.Previews.";

    /// <summary>A scene declared as an <c>IPreviewScreen</c> itself, which is the way round the base class.</summary>
    private static readonly Regex DirectScreen = new(@"class\s+\w+\s*:\s*IPreviewScreen\b", RegexOptions.Compiled);

    /// <summary>A scene declared through the base class.</summary>
    private static readonly Regex BaseScreen = new(@"class\s+\w+\s*:\s*PreviewScene\b", RegexOptions.Compiled);

    /// <summary>
    /// No preview scene may implement <c>IPreviewScreen</c> directly: the surface paint lives in
    /// <c>PreviewScene.Draw</c>, and a scene outside that base renders on transparency.
    /// </summary>
    [Fact]
    public void EveryPreviewSceneGoesThroughPreviewScene()
    {
        List<string> direct = [];

        foreach ((string file, string text) in Sources())
        {
            // The base class is the one place allowed to name the interface.
            if (file == "PreviewScene.cs")
            {
                continue;
            }

            foreach (Match match in DirectScreen.Matches(WithoutComments(text)))
            {
                direct.Add($"{file}: {match.Value}");
            }
        }

        Assert.True(
            direct.Count == 0,
            "a preview scene must derive from PreviewScene, which paints the window's panel before the body "
            + "- otherwise its golden shows the previewer's backdrop where the window's background belongs:\n  "
            + string.Join("\n  ", direct));
    }

    /// <summary>
    /// The scan has to be looking at the scenes: if the embedding or the pattern drifts, the test above would
    /// pass by finding nothing at all, which is worse than failing.
    /// </summary>
    [Fact]
    public void TheScanFindsTheScenes()
    {
        var screens = 0;

        foreach ((_, string text) in Sources())
        {
            screens += BaseScreen.Matches(WithoutComments(text)).Count;
        }

        Assert.True(screens >= 20, $"only {screens} preview scene(s) were found in the embedded sources; the scan is broken, not the scenes.");
    }

    /// <summary>
    /// And the base class has to keep doing the paint: the two assertions above are only worth anything while
    /// <c>PreviewScene.Draw</c> paints the surface before it calls the scene's body.
    /// </summary>
    [Fact]
    public void PreviewScenePaintstheSurfaceBeforeTheBody()
    {
        string? source = null;

        foreach ((string file, string text) in Sources())
        {
            if (file == "PreviewScene.cs")
            {
                source = WithoutComments(text);
            }
        }

        Assert.NotNull(source);

        int paint = source.IndexOf("PreviewSurface.Draw(SurfaceRadius);", StringComparison.Ordinal);
        int body = source.IndexOf("DrawScene();", StringComparison.Ordinal);

        Assert.True(paint >= 0, "PreviewScene.Draw no longer paints PreviewSurface: every golden loses the window's background.");
        Assert.True(body >= 0 && paint < body, "PreviewScene.Draw must paint the panel before it draws the scene body.");
    }

    /// <summary>The embedded previews sources, as (file name, text).</summary>
    private static IEnumerable<(string File, string Text)> Sources()
    {
        Assembly assembly = typeof(PreviewSceneTests).Assembly;

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(SourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            string file = name[(name.LastIndexOf('.', name.Length - 4) + 1)..];
            yield return (file, reader.ReadToEnd());
        }
    }

    /// <summary>The source with block and line comments blanked out, so a comment cannot satisfy a scan.</summary>
    private static string WithoutComments(string source)
    {
        string text = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\n]*", string.Empty);
    }
}
