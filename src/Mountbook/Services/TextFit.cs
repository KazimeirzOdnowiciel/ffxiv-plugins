using System;

namespace Mountbook.Services;

/// <summary>
/// Trims a string to the widest prefix that fits a pixel width, ending in an ellipsis. Kept out of the view
/// because the rule is subtle in three places - the result must never be wider than the pane, must never be
/// empty, and must not end in the space it just cut at - and none of that is observable in a golden unless a
/// scene happens to hold the exact string.
/// </summary>
public static class TextFit
{
    /// <summary>The character a trimmed string ends in. ASCII-only goldens drop it, so previews show a `?`.</summary>
    public const string Ellipsis = "\u2026";

    /// <summary>
    /// <paramref name="text"/> unchanged when it measures within <paramref name="availableWidth"/>, otherwise
    /// its longest prefix plus an ellipsis that still fits. A width too narrow for even one character plus the
    /// ellipsis returns that, rather than an empty label.
    /// </summary>
    /// <param name="text">The string to fit.</param>
    /// <param name="availableWidth">The pixel width it has to fit in.</param>
    /// <param name="measure">How wide a string draws; the caller's, so the view can memoise it.</param>
    /// <param name="ellipsis">What a trimmed string ends in; ASCII dots where a preview draws the text.</param>
    public static string Fit(
        string text,
        float availableWidth,
        Func<string, float> measure,
        string ellipsis = Ellipsis)
    {
        if (text.Length == 0 || measure(text) <= availableWidth)
        {
            return text;
        }

        var length = text.Length;

        while (length > 1 && measure(string.Concat(text.AsSpan(0, length), ellipsis)) > availableWidth)
        {
            length--;
        }

        return string.Concat(text.AsSpan(0, length).TrimEnd(), ellipsis);
    }
}
