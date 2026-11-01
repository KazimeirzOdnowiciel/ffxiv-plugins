using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Mountbook.Previews.Cjk;

/// <summary>One texture of the font atlas, as the rasteriser needs it.</summary>
/// <param name="Pixels">RGBA pixels, row-major.</param>
/// <param name="Width">The texture's width.</param>
/// <param name="Height">The texture's height.</param>
internal sealed record AtlasTexture(byte[] Pixels, int Width, int Height);

/// <summary>
/// Draws an <see cref="ImDrawData"/> into an RGBA buffer on the CPU: triangles, the font atlas as the only
/// texture set, and ImGui's own blend.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the pinned golden renderer cannot draw CJK: <c>glasswork-preview</c> pins its UI
/// font's glyph range to U+0020..U+00FF, so a Japanese label comes out as question marks whatever face is
/// loaded (the probe is recorded in <c>docs/localisation.md</c>). Rather than re-baseline every golden onto
/// a face that depends on the machine's fonts, this renders one extra image with a face committed to the
/// repository and nothing but the CPU involved.
/// </para>
/// <para>
/// It is a rasteriser, not a second ImGui: layout, kerning and the glyph bitmaps all come from ImGui's own
/// font engine, which is the part the golden is meant to prove.
/// </para>
/// </remarks>
internal static unsafe class Software
{
    /// <summary>Renders <paramref name="data"/> over <paramref name="target"/>.</summary>
    /// <param name="data">The frame ImGui produced.</param>
    /// <param name="textures">The font atlas' textures, indexed by the draw command's texture id minus one.</param>
    /// <param name="target">The RGBA target, row-major, top row first.</param>
    /// <param name="width">The target's width.</param>
    /// <param name="height">The target's height.</param>
    public static void Render(
        ImDrawDataPtr data,
        IReadOnlyList<AtlasTexture> textures,
        byte[] target,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(target);

        if (textures.Count == 0)
        {
            throw new ArgumentException("The atlas has no textures.", nameof(textures));
        }

        if (target.Length != width * height * 4)
        {
            throw new ArgumentException("The target buffer does not match the image size.", nameof(target));
        }

        Vector2 origin = data.DisplayPos;

        for (var listIndex = 0; listIndex < data.CmdListsCount; listIndex++)
        {
            ImDrawListPtr list = data.CmdLists[listIndex];
            ImDrawVert* vertices = list.VtxBuffer.Data;
            ushort* indices = list.IdxBuffer.Data;
            ImDrawCmd* commands = list.CmdBuffer.Data;
            var commandCount = list.CmdBuffer.Size;

            for (var commandIndex = 0; commandIndex < commandCount; commandIndex++)
            {
                ImDrawCmd command = commands[commandIndex];

                // 1.92 gives every font its own texture; the command says which one its vertices sample.
                // A command with no texture is a solid shape drawn with the atlas' white pixel, and ImGui
                // still tags it with the current font's texture, so there is nothing to skip.
                var textureIndex = (int)command.GetTexID().Handle - 1;
                AtlasTexture texture = textureIndex >= 0 && textureIndex < textures.Count
                    ? textures[textureIndex]
                    : textures[0];

                Vector4 clip = command.ClipRect;
                var first = (int)command.IdxOffset;

                for (var element = 0; element < command.ElemCount; element += 3)
                {
                    ImDrawVert a = vertices[indices[first + element]];
                    ImDrawVert b = vertices[indices[first + element + 1]];
                    ImDrawVert c = vertices[indices[first + element + 2]];

                    Triangle(texture, clip, a.Pos - origin, a.Uv, a.Col, b.Pos - origin, b.Uv, b.Col, c.Pos - origin, c.Uv, c.Col);
                }
            }
        }

        void Triangle(
            AtlasTexture texture,
            Vector4 clip,
            Vector2 p0,
            Vector2 uv0,
            uint c0,
            Vector2 p1,
            Vector2 uv1,
            uint c1,
            Vector2 p2,
            Vector2 uv2,
            uint c2)
        {
            var minX = MathF.Max(MathF.Min(p0.X, MathF.Min(p1.X, p2.X)), 0f);
            var maxX = MathF.Min(MathF.Max(p0.X, MathF.Max(p1.X, p2.X)), width - 1f);
            var minY = MathF.Max(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)), 0f);
            var maxY = MathF.Min(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)), height - 1f);

            var area = ((p1.X - p0.X) * (p2.Y - p0.Y)) - ((p1.Y - p0.Y) * (p2.X - p0.X));

            if (MathF.Abs(area) < 1e-6f)
            {
                return;
            }

            // The clip rect is in display space too, and is applied per pixel: a glyph half inside a child
            // window must be cut where ImGui cut it, not skipped whole.
            var startX = (int)MathF.Floor(MathF.Max(minX, Limit(clip.X)));
            var endX = (int)MathF.Ceiling(MathF.Min(maxX, Limit(clip.Z)));
            var startY = (int)MathF.Floor(MathF.Max(minY, Limit(clip.Y)));
            var endY = (int)MathF.Ceiling(MathF.Min(maxY, Limit(clip.W)));

            if (endX <= startX || endY <= startY)
            {
                return;
            }

            var inverse = 1f / area;

            for (var y = startY; y < endY; y++)
            {
                for (var x = startX; x < endX; x++)
                {
                    // Pixel centre, so a 1 px glyph edge lands on one pixel rather than two.
                    var px = x + 0.5f;
                    var py = y + 0.5f;

                    var w0 = (((p1.X - px) * (p2.Y - py)) - ((p1.Y - py) * (p2.X - px))) * inverse;
                    var w1 = (((p2.X - px) * (p0.Y - py)) - ((p2.Y - py) * (p0.X - px))) * inverse;
                    var w2 = 1f - w0 - w1;

                    // A hair of tolerance: ImGui's own quads meet at exact pixel edges, and a seam through
                    // every glyph would be this test being one rounding step too strict.
                    if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f)
                    {
                        continue;
                    }

                    var u = (w0 * uv0.X) + (w1 * uv1.X) + (w2 * uv2.X);
                    var v = (w0 * uv0.Y) + (w1 * uv1.Y) + (w2 * uv2.Y);

                    var sx = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
                    var sy = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
                    var texel = ((sy * texture.Width) + sx) * 4;

                    var r = (byte)(((c0 & 0xFF) * w0) + ((c1 & 0xFF) * w1) + ((c2 & 0xFF) * w2));
                    var g = (byte)((((c0 >> 8) & 0xFF) * w0) + (((c1 >> 8) & 0xFF) * w1) + (((c2 >> 8) & 0xFF) * w2));
                    var bl = (byte)((((c0 >> 16) & 0xFF) * w0) + (((c1 >> 16) & 0xFF) * w1) + (((c2 >> 16) & 0xFF) * w2));
                    var al = (byte)((((c0 >> 24) & 0xFF) * w0) + (((c1 >> 24) & 0xFF) * w1) + (((c2 >> 24) & 0xFF) * w2));

                    // The atlas is white with the glyph's coverage in alpha, and ImGui blends the vertex
                    // colour through it: that product is the pixel's alpha.
                    var alpha = (al / 255f) * (texture.Pixels[texel + 3] / 255f);

                    if (alpha <= 0f)
                    {
                        continue;
                    }

                    var pixel = ((y * width) + x) * 4;
                    target[pixel + 0] = Blend(target[pixel + 0], r, alpha);
                    target[pixel + 1] = Blend(target[pixel + 1], g, alpha);
                    target[pixel + 2] = Blend(target[pixel + 2], bl, alpha);
                    target[pixel + 3] = Blend(target[pixel + 3], 255, alpha);
                }
            }
        }
    }

    /// <summary>ImGui writes an empty clip as <c>±FLT_MAX</c>; ints cannot hold that, so it is clamped first.</summary>
    private static float Limit(float value) => float.IsInfinity(value) || MathF.Abs(value) > 1e7f
        ? MathF.Sign(value) * 1e7f
        : value;

    private static byte Blend(byte destination, byte source, float alpha) =>
        (byte)Math.Clamp((int)MathF.Round((source * alpha) + (destination * (1f - alpha))), 0, 255);
}
