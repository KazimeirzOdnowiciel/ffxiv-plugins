namespace Mountbook.Services;

/// <summary>
/// Whether a mount accepts barding, read from the four gear-slot columns the <c>Mount</c> sheet
/// carries (<c>EquipHead</c>/<c>EquipBody</c>/<c>EquipLeg</c>/<c>EquipFoot</c>).
/// </summary>
/// <remarks>
/// <para>
/// The columns are not booleans: a slot the mount accepts is written as <c>0x10000 | variant</c>
/// (65536, 65537, ...) and a slot it does not is zero. So "any of the four is non-zero" would call a
/// mount barding-capable on the strength of a small variant id, while testing bit 16 asks the question
/// the sheet is actually answering. Verified against the sheet on 2026-10-31: Company Chocobo is
/// 65536/65536/65537/0, Legacy Chocobo is 65537 four times, and a non-chocobo mount is all zeros.
/// </para>
/// <para>
/// Pure and Dalamud-free (T1): the catalog passes the four columns in.
/// </para>
/// </remarks>
public static class MountBarding
{
    /// <summary>The flag a gear-slot column sets when that slot takes barding.</summary>
    public const uint SlotFlag = 0x10000u;

    /// <summary>True when any of the four gear slots accepts barding.</summary>
    /// <param name="head">The <c>EquipHead</c> column.</param>
    /// <param name="body">The <c>EquipBody</c> column.</param>
    /// <param name="legs">The <c>EquipLeg</c> column.</param>
    /// <param name="feet">The <c>EquipFoot</c> column.</param>
    public static bool Supports(uint head, uint body, uint legs, uint feet) =>
        IsSlot(head) || IsSlot(body) || IsSlot(legs) || IsSlot(feet);

    /// <summary>Whether one gear-slot column names a barding slot.</summary>
    /// <param name="column">One of the four <c>Equip*</c> columns.</param>
    public static bool IsSlot(uint column) => (column & SlotFlag) != 0;
}
