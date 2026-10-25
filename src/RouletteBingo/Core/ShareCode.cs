using System;
using System.Text;

namespace RouletteBingo.Core;

/// <summary>
/// Compact, copy-pasteable card code: RB1.&lt;base64url&gt;
/// Layout: version(1) | seed(8) | marks(4) | kind(1)
/// </summary>
public static class ShareCode
{
    public const string Prefix = "RB1.";
    private const byte Version = 1;
    private const int PayloadLength = 14;

    public static string Encode(ulong seed, uint marks, DutyKind kind)
    {
        var bytes = new byte[PayloadLength];
        bytes[0] = Version;
        BitConverter.TryWriteBytes(bytes.AsSpan(1, 8), seed);
        BitConverter.TryWriteBytes(bytes.AsSpan(9, 4), marks);
        bytes[13] = (byte)kind;

        return Prefix + ToBase64Url(bytes);
    }

    public static bool TryDecode(string? text, out ulong seed, out uint marks, out DutyKind kind)
    {
        seed = 0;
        marks = 0;
        kind = DutyKind.Other;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var payload = trimmed[Prefix.Length..];
        byte[] bytes;
        try
        {
            bytes = FromBase64Url(payload);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != PayloadLength || bytes[0] != Version)
            return false;

        seed = BitConverter.ToUInt64(bytes, 1);
        marks = BitConverter.ToUInt32(bytes, 9);
        kind = (DutyKind)bytes[13];
        return true;
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
