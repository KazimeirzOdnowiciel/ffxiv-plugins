using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// Shortens a long path for a single text line by keeping its tail - the part that says which folder it is -
/// instead of cutting the end off. Kept out of the view because the loop has three edge cases a golden would
/// only catch by accident: the whole path fitting, a tail that fits with no room for a prefix, and one
/// segment longer than the line.
/// </summary>
public static class PathLabel
{
    /// <summary>What a shortened path starts with. ASCII: the headless preview's atlas has no U+2026.</summary>
    public const string Prefix = "...";

    /// <summary>
    /// <paramref name="path"/> unchanged when it measures within <paramref name="availableWidth"/>, otherwise
    /// <see cref="Prefix"/> plus as many trailing segments as still fit. When not even the last segment fits,
    /// that segment is trimmed from the end instead - an over-long folder name still has to draw something.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <param name="availableWidth">The pixel width it has to fit in.</param>
    /// <param name="measure">How wide a string draws, so the caller can memoise it.</param>
    public static string Shorten(string path, float availableWidth, Func<string, float> measure)
    {
        if (path.Length == 0 || measure(path) <= availableWidth)
        {
            return path;
        }

        string[] segments = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return path;
        }

        // One segment is the floor: "...\themes" says where it is, "..." alone says nothing.
        string best = string.Concat(Prefix, "\\", segments[^1]);
        var shortest = best;

        for (var count = 2; count <= segments.Length; count++)
        {
            string candidate = string.Concat(Prefix, "\\", string.Join('\\', Tail(segments, count)));

            if (measure(candidate) > availableWidth)
            {
                break;
            }

            best = candidate;
        }

        if (measure(shortest) <= availableWidth)
        {
            return best;
        }

        // Even the last segment is too wide: trim it from the end, which is the one case where losing the
        // tail beats losing the head. Same prefix, so nothing in a preview shows a missing glyph.
        return TextFit.Fit(path, availableWidth, measure, Prefix);
    }

    /// <summary>The last <paramref name="count"/> segments of <paramref name="segments"/>.</summary>
    private static IEnumerable<string> Tail(string[] segments, int count)
    {
        for (var index = segments.Length - count; index < segments.Length; index++)
        {
            yield return segments[index];
        }
    }
}
