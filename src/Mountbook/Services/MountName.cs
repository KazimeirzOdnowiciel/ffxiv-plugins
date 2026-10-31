namespace Mountbook.Services;

/// <summary>
/// Turns a mount's sheet name into the casing the game's own UI shows it in.
/// </summary>
/// <remarks>
/// The Mount sheet's <c>Singular</c> column is written for mid-sentence use ("battle bear",
/// "reveling kamuy"), while the Mount Guide displays "Battle Bear". Title-casing every word is not
/// the answer either: it would turn "SDS Fenrir" into "Sds Fenrir" and "Demi-Ozma" into
/// "Demi-ozma". So only words that contain no capital letter at all are touched, and inside those
/// the first letter of each hyphen/slash/bracket segment is raised.
/// </remarks>
public static class MountName
{
    /// <summary>The display form of a mount's name; never lowercases anything the sheet already capitalised.</summary>
    public static string ToDisplay(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        string[] words = name.Split(' ');
        for (int index = 0; index < words.Length; index++)
        {
            words[index] = CapitaliseSegmentStarts(words[index]);
        }

        return string.Join(' ', words);
    }

    private static string CapitaliseSegmentStarts(string word)
    {
        if (word.Length == 0 || HasCapital(word))
        {
            return word;
        }

        char[] characters = word.ToCharArray();
        bool atSegmentStart = true;

        for (int index = 0; index < characters.Length; index++)
        {
            char character = characters[index];

            if (atSegmentStart && char.IsLetter(character))
            {
                characters[index] = char.ToUpperInvariant(character);
                atSegmentStart = false;
                continue;
            }

            // Not the apostrophe: "k'sante" reads as "K'sante", not "K'Sante".
            if (character is '-' or '/' or '(')
            {
                atSegmentStart = true;
            }
        }

        return new string(characters);
    }

    private static bool HasCapital(string word)
    {
        foreach (char character in word)
        {
            if (char.IsUpper(character))
            {
                return true;
            }
        }

        return false;
    }
}
