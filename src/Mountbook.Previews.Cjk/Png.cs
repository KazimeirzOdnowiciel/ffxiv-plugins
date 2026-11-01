using System;
using System.Collections.Generic;
using System.IO;

namespace Mountbook.Previews.Cjk;

/// <summary>
/// A minimal PNG writer: 8-bit RGBA, no filtering, one zlib stream per image.
/// </summary>
/// <remarks>
/// Hand-written rather than a package, because this renderer exists to be the smallest thing that can prove
/// a font draws CJK: a dependency with its own version to pin would be more to keep than the sixty lines it
/// saves. The output is deterministic - the same pixels produce the same bytes - which is what makes the
/// golden a byte comparison.
/// </remarks>
internal static class Png
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Writes <paramref name="pixels"/> (RGBA, row-major, top row first) to <paramref name="path"/>.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="width">The image's width in pixels.</param>
    /// <param name="height">The image's height in pixels.</param>
    /// <param name="pixels">The pixels, four bytes each.</param>
    public static void Write(string path, int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(path);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, Encode(width, height, pixels));
    }

    /// <summary>The PNG bytes for <paramref name="pixels"/>, for comparing with a committed golden.</summary>
    /// <param name="width">The image's width in pixels.</param>
    /// <param name="height">The image's height in pixels.</param>
    /// <param name="pixels">The pixels, four bytes each.</param>
    /// <returns>The encoded file.</returns>
    public static byte[] Encode(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentException("The pixel buffer does not match the image size.", nameof(pixels));
        }

        using var stream = new MemoryStream();

        stream.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // colour type: RGBA
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // no interlace
        WriteChunk(stream, "IHDR", header);

        // One filter byte per row (0 = none), then the row's bytes.
        var raw = new byte[height * ((width * 4) + 1)];
        var offset = 0;

        for (var row = 0; row < height; row++)
        {
            raw[offset++] = 0;
            Buffer.BlockCopy(pixels, row * width * 4, raw, offset, width * 4);
            offset += width * 4;
        }

        using var compressed = new MemoryStream();

        using (var deflate = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        WriteChunk(stream, "IDAT", compressed.ToArray());
        WriteChunk(stream, "IEND", []);

        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);

        var payload = new byte[4 + data.Length];
        for (var index = 0; index < 4; index++)
        {
            payload[index] = (byte)type[index];
        }

        Buffer.BlockCopy(data, 0, payload, 4, data.Length);
        stream.Write(payload);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc(payload));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint index = 0; index < 256; index++)
        {
            var value = index;

            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
