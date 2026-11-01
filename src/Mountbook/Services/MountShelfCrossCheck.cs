using System;
using System.Collections.Generic;

namespace Mountbook.Services;

/// <summary>
/// What a snapshot says about its own cross-check against the game's <c>Mount</c> sheet: which sheet
/// columns it compared, how many site rows it compared, and how many of those agreed on each fact.
/// </summary>
/// <remarks>
/// <para>
/// Two of the site's fields are not shipped as data - the seat count and Airborne/Terrestrial - because
/// the game's own sheet already answers both for every row (<c>ExtraSeats + 1</c> and <c>IsAirborne</c>)
/// and a copy in the snapshot could contradict a patched client. They are still checked at build time,
/// and this record is the evidence: the generator refuses to write when a value disagrees, and the
/// validator refuses a file whose counts do not match the number of rows it carries.
/// </para>
/// <para>
/// The record is null in a snapshot built offline, or by a run that had no sheet facts to compare
/// against; <see cref="MountShelf.HasCrossCheck"/> is the single question a caller has to ask, and "no
/// check ran" must never be read as "everything agreed".
/// </para>
/// </remarks>
/// <param name="Sheet">The sheet the comparison used, normally <c>Mount</c>.</param>
/// <param name="Fields">The sheet columns it used: <c>ExtraSeats</c> and <c>IsAirborne</c>.</param>
/// <param name="RowsChecked">How many site rows were compared.</param>
/// <param name="SeatsAgreed">How many of those rows agreed on the seat count.</param>
/// <param name="MovementAgreed">How many of those rows agreed on Airborne/Terrestrial.</param>
public sealed record MountShelfCrossCheck(
    string Sheet,
    IReadOnlyList<string> Fields,
    int RowsChecked,
    int SeatsAgreed,
    int MovementAgreed)
{
    /// <summary>The sheet column a seat count comes from; the value shipped is one more than it.</summary>
    public const string SeatsField = "ExtraSeats";

    /// <summary>The sheet column that decides Airborne (true) or Terrestrial (false).</summary>
    public const string MovementField = "IsAirborne";

    /// <summary>Every row compared agreed on both facts.</summary>
    public bool Agrees => SeatsAgreed >= RowsChecked && MovementAgreed >= RowsChecked;

    /// <summary>
    /// The comparison used the two columns it is supposed to use. False means the file names some other
    /// pairing, so its counts cannot be talking about seats and movement at all.
    /// </summary>
    public bool NamesSheetFacts => ContainsField(SeatsField) && ContainsField(MovementField);

    /// <summary>
    /// One line for the diagnostics block: what was compared and how it came out, e.g.
    /// <c>356/356 seats, 356/356 movement (Mount.ExtraSeats, Mount.IsAirborne)</c>.
    /// </summary>
    public string Describe()
    {
        string suffix = Agrees ? string.Empty : " - DISAGREEMENT";
        return $"{SeatsAgreed}/{RowsChecked} seats, {MovementAgreed}/{RowsChecked} movement (Mount.{SeatsField}, Mount.{MovementField}){suffix}";
    }

    private bool ContainsField(string name)
    {
        foreach (string field in Fields)
        {
            if (string.Equals(field, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
