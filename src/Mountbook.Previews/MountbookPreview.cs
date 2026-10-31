using System;
using System.Collections.Generic;
using System.Numerics;
using Glasswork;
using Glasswork.Backend.Preview;
using Mountbook.Services;
using Mountbook.Views;

namespace Mountbook.Previews;

/// <summary>
/// Draws the main window's view with fixed sample data so it can be rendered without the game
/// and compared against <c>goldens/main.png</c>.
/// </summary>
/// <remarks>
/// The twelve mounts below are built once, when the preview is created, and never from the config,
/// the game or the wall clock (CONTRIBUTING T7): the same sample draws the same pixels on every run.
/// Three of them are unowned, so the grid's dimmed treatment is covered, and one is selected, so the
/// detail pane and the selected outline are covered too.
/// <para>
/// The community figures are the ones ffxivcollect reports for these mounts, copied in rather than
/// fetched: the preview never touches the network. They are picked to cover every branch of the
/// rarity line and both badges - the selected mount has its own music, one mount is tradeable, one
/// (<c>Midgardsormr</c>) has an unrecorded <c>tradeable</c> flag so no badge may appear for it, and
/// one (<c>Ronkan Emerald</c>) has no community data at all so the line must be absent entirely.
/// </para>
/// </remarks>
public sealed class MountbookPreview : IPreviewScreen
{
    private readonly MountbookView view = new();

    private readonly MountViewState state;

    public MountbookPreview()
    {
        var items = SampleMounts();
        var owned = 0;

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Owned)
            {
                owned++;
            }
        }

        state = new MountViewState
        {
            Items = items,
            Expansions = MountBreakdown.ByExpansion(items),
            OwnershipCounts = new MountQuery(string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name).OwnershipCounts(items, null),
            Owned = owned,
            Total = items.Count,
            SelectedId = SelectedMountId,
            ItemName = "Aithon Whistle",
            SourceLabels = ["Any", "Achievement", "Event", "FATE", "Grand Company", "Mog Station", "Quest", "Trial", "V&C Dungeon"],
            Ownership = MountOwnership.All,
            Expansion = MountExpansion.All,
            Order = MountOrder.Name,
        };
    }

    /// <summary>Also the golden's file name: <c>goldens/main.png</c>.</summary>
    public string Name => "main";

    /// <summary>The window size the plugin gives its main window.</summary>
    public Vector2 WindowSize => new(1040f, 660f);

    /// <summary>The Look the plugin sets on its host, so the golden shows what users see.</summary>
    public Look Look { get; } = Look.Eorzea();

    public ReadOnlySpan<PreviewInput> Script => [];

    public void Draw() => view.Draw(state);

    /// <summary>The mount the golden opens on: owned, with two sources and long flavour text.</summary>
    private const uint SelectedMountId = 420u;

    /// <summary>
    /// Twelve fixed mounts, in the name order the default <see cref="MountOrder.Name"/> produces, so
    /// the golden's grid, its dimmed unowned cards and its detail pane are all covered by one image.
    /// </summary>
    internal static IReadOnlyList<MountShelfItem> SampleMounts() =>
    [
        Item(
            58, "Aithon", 21, 1, "2.5", true,
            "A fire-maned steed out of the age of heroes.",
            "They say Aithon carried a hero through the Calamity and out the other side, and that the heat of that day never left its coat. It answers only to a rider who has already done something worth remembering.",
            string.Empty,
            Community(48.7, false, true),
            Source("Achievement", "A Legend for All Time")),
        Item(
            160, "Astrope", 44, 1, "3.5", true,
            "A pony that never quite grew up.",
            "Astrope is a Garlean rarity: a mount bred for the parades of the capital, small enough to be a child's first ride and proud enough that it will not admit as much.",
            string.Empty,
            Community(0.5, false, false),
            Source("Achievement", "Hand of the Empire")),
        Item(
            420, "Auspicious Kamuy", 88, 1, "6.1", true,
            "A white-maned beast out of the Far East.",
            "The Auspicious Kamuy is said to guard the gates of a shrine that appears on no map. Those who have seen it swear the air around it hums like a plucked string, and that snow will not settle on its back.",
            "Summoned by those who have earned the trust of the auspicious guardians, it runs without leaving a track in the snow.",
            Community(32.7, false, true),
            Source("Trial", "The Minstrel's Ballad: Byakko Unchained"),
            Source("Achievement", "Goodly Sea-Snake")),
        Item(
            26, "Coeurl", 14, 1, "2.1", true,
            "A whiskered predator of the Black Shroud.",
            "Tamed coeurls are rare, and rarer still are the riders who keep all their fingers. This one has decided that its rider, at least, is not food.",
            string.Empty,
            Community(32.6, false, false),
            Source("Achievement", "Mount Collector")),
        Item(
            1, "Company Chocobo", 10, 1, "2.0", true,
            "A friend from the first Grand Company.",
            "Every adventurer's first mount, and the only one that was ever a friend first. It remembers the road to every city-state and takes the corners it likes best.",
            string.Empty,
            Community(96.4, false, false),
            Source("Quest", "The Ultimate Weapon"),
            Source("Grand Company", "1,200 company seals")),
        Item(
            412, "Genie of the Lamp", 78, 1, "7.45", false,
            "A wish given a pair of arms and a stretch of open sky.",
            "Three wishes, and the third is always spent on keeping it. The lamp is a formality: the genie comes when it feels like it, and the ride is as much an argument as a favour.",
            string.Empty,
            Community(0.4, true, true),
            Source("V&C Dungeon", "Another Merchant's Tale"),
            Source("V&C Dungeon", "Trisassant - Old Sharlayan - 100 Corvosi Manuscripts")),
        Item(
            470, "Garlond GL-IS", 96, 2, "7.05", false,
            "A suspiciously fast machine with two seats.",
            "The Garlond Ironworks insists the GL-IS is a transport and not a racing machine. The passenger seat has a handle, which says more about the brochure than the brochure does.",
            "Garlond Ironworks' newest civilian chassis: quiet, quick, and entirely unsuitable for the battlefield.",
            Community(5.2, false, true),
            Source("Mog Station", "Mounts & Miscellany")),
        Item(
            244, "Juedi", 60, 1, "4.4", true,
            "A gentle beast with a stubborn streak.",
            "Juedi is a mountain of placid muscle that will follow a friend anywhere, and refuse to go anywhere else. It has been called both the safest and the slowest mount in Gyr Abania.",
            string.Empty,
            Community(2.3, false, true),
            Source("Achievement", "Chuckling Ananta")),
        Item(
            105, "Midgardsormr", 30, 1, "3.0", true,
            "A wyrm's shadow given a saddle.",
            "Even in a form this small, Midgardsormr's presence is enough to make chocobos refuse the road. He carries his rider not as a beast of burden, but as an old acquaintance repaying a debt.",
            string.Empty,
            Community(78.7, null, false),
            Source("Quest", "Heavensward")),
        Item(
            310, "Ronkan Emerald", 72, 1, "5.0", true,
            "A construct of the Ronkan empire.",
            "The Ronkan Emerald was never a living creature: it is a shrine guardian whose wards outlived the empire that bound them. It moves in perfect silence and stops without warning.",
            string.Empty,
            null,
            Source("Achievement", "Shadowbringers Treasure Hunter")),
        Item(
            43, "Unicorn", 17, 1, "2.4", true,
            "A healer's mount from the boughs of Gridania.",
            "The unicorn is not a beast of war and does not pretend to be one. It will carry the wounded, and it will not carry a rider it considers unkind.",
            string.Empty,
            Community(41.9, false, false),
            Source("Quest", "A Journey of Discovery")),
        Item(
            205, "Xanthos", 52, 1, "4.0", false,
            "A wind-bred stallion of the Fringes.",
            "Xanthos is a spirit of the open plains, and the plains do not hold still. It has been tamed exactly once, and only for as long as it wanted to be.",
            string.Empty,
            Community(51.3, false, true),
            Source("FATE", "Fringes Fieldwork"),
            Source("Achievement", "Ride of the Valkyrie")),
    ];

    private static MountShelfItem Item(
        uint id,
        string name,
        uint iconId,
        int seats,
        string patch,
        bool owned,
        string description,
        string enhancedDescription,
        string tooltip,
        MountCommunity? community,
        params MountSourceLine[] sources) =>
        new(
            id,
            name,
            iconId,
            64000u + iconId,
            seats,
            patch,
            owned,
            description,
            enhancedDescription,
            tooltip,
            sources,
            community,
            Barding(name),
            Flies(name));

    /// <summary>
    /// Barding is chocobo-only in the game, so the sample marks exactly one mount that could wear it
    /// (Astrope, the pony) - which is what puts the badge in the table and wanted goldens.
    /// </summary>
    private static bool Barding(string name) => name == "Astrope";

    /// <summary>
    /// Almost every mount in the current game flies, so the sample marks all but one: the badge then
    /// appears in the goldens that draw a detail pane, and the ground-only mount keeps the "no badge"
    /// case covered by the same image.
    /// </summary>
    private static bool Flies(string name) => name != "Astrope";

    private static MountCommunity Community(double? ownedPercent, bool? tradeable, bool? customMusic) =>
        new(ownedPercent, tradeable, customMusic);

    private static MountSourceLine Source(string category, string detail, string relatedKind = "", uint relatedId = 0) =>
        new(category, detail, relatedKind, relatedId);
}
