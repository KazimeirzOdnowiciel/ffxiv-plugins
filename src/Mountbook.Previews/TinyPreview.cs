using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// The smallest window the plugin allows (820x520, its own <c>SizeConstraints.MinimumSize</c>). Dragging
/// the window down stops here, so this - not an arbitrary small size - is the state that has to look
/// right; it is also the only screen where the horizontal and vertical squeezes happen together.
/// </summary>
public sealed class MinimumPreview : IPreviewScreen
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public MinimumPreview()
    {
        IReadOnlyList<MountShelfItem> items = MountbookPreview.SampleMounts();
        var owned = 0;

        foreach (MountShelfItem item in items)
        {
            if (item.Owned)
            {
                owned++;
            }
        }

        state = new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name)
                .OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            SelectedId = items[0].Id,
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/minimum.png</c>.</summary>
    public string Name => "minimum";

    public Look Look { get; } = Look.Eorzea();

    public ReadOnlySpan<PreviewInput> Script => [];

    /// <summary>Exactly the window's enforced minimum.</summary>
    public Vector2 WindowSize => new(820f, 520f);

    public void Draw() => view.Draw(state);
}
