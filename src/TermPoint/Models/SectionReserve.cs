namespace TermPoint.Models;

/// <summary>
/// A reserved-seat block on a section: which reserve (a scheduling-environment value) and how many
/// seats. A record nested inside <see cref="Section.Reserves"/>; it derives from
/// <see cref="PersistedRecord"/> so unknown JSON fields survive a load/save round trip. Editors that
/// rebuild the reserve list must carry the original's unknown fields across (see
/// <see cref="PersistedRecord"/>).
/// </summary>
public class SectionReserve : PersistedRecord
{
    public string ReserveId { get; set; } = string.Empty;
    public int Code { get; set; }
}
