using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermPoint.Models;

/// <summary>
/// Abstract base for every class that is serialized into a database <c>data</c> JSON column,
/// whether it is a top-level record (<see cref="Section"/>, <see cref="Instructor"/>, ...) or a
/// record nested inside one (<see cref="SectionDaySchedule"/>, <see cref="InstructorAssignment"/>,
/// <see cref="SectionReserve"/>). Its only job is to carry <see cref="ExtensionData"/>.
///
/// <para><b>Why this exists (spec_version_1.2.3.md, item 3).</b> Records are stored as JSON, and
/// System.Text.Json silently discards any JSON field that the running class does not declare. Two
/// versions of the app often work on one shared database: Microsoft Store updates install whenever
/// Windows gets round to it, so an older client can be editing the same file as a newer one. If a
/// newer release adds a field (as Capacity was added), an older client that loads and re-saves a
/// record would drop that field with no error and no log entry. <see cref="ExtensionData"/> is a
/// catch-all: on load, every JSON property the class does not declare is parked in the dictionary
/// as an immutable <see cref="JsonElement"/>; on save, the entries are written back verbatim. The
/// older client therefore round-trips fields it does not understand.</para>
///
/// <para><b>The rule for code that rebuilds a record.</b> The dictionary lives on the object that
/// was deserialized. Any code that builds a <i>fresh</i> object to replace an existing record (an
/// editor that copies fields into a clone, a Save that rebuilds a child list from view-model rows,
/// ...) and then writes it back with an <c>Update</c> must carry the original's unknown fields
/// across, or the data loss this class exists to prevent happens anyway. Use
/// <see cref="CopyExtensionDataFrom"/> (statement form) or
/// <see cref="PersistedRecordExtensions.WithExtensionDataFrom{T}"/> (fluent form, convenient inside
/// a LINQ <c>Select</c>). Code that merely mutates the loaded object needs to do nothing. Brand new
/// records (Add, Copy, Copy Semester, imports) start with no unknown fields, which is correct.
/// <c>SectionCloneTests</c> enforces this for sections by reflection, and
/// <c>ExtensionDataRoundTripTests</c> checks the type list itself, so a new persisted class that
/// does not derive from this one fails a test.</para>
///
/// <para><b>Serialization notes.</b></para>
/// <list type="bullet">
///   <item>The attribute makes the property <i>the</i> extension data: it is not written as a
///         member called "extensionData"; its entries are written inline as sibling properties of
///         the record. That is why it must <b>not</b> carry <c>[JsonIgnore]</c> (the usual
///         treatment for display-only properties), and why reflection helpers that treat "public
///         setter, not <c>[JsonIgnore]</c>" as "persisted" (such as the one in
///         <c>SectionCloneTests</c>) correctly see it as a persisted member.</item>
///   <item>When a record has no unknown fields the property stays <c>null</c>, allocates nothing,
///         and adds nothing to the JSON, so existing databases are written exactly as before.</item>
///   <item>The dictionary key is the field name exactly as it appeared in the JSON. The camelCase
///         naming policy in <see cref="Data.JsonHelpers"/> does not rename extension keys.</item>
///   <item>It is a plain public property because System.Text.Json requires that. It must not take
///         part in UI bindings or equality: none of the persisted models override
///         <c>Equals</c>/<c>GetHashCode</c>, none is a record type, and every grid in the app uses
///         explicit columns.</item>
///   <item>The browser (WASM) build uses in-memory repositories and never serializes these types,
///         and its project already enables reflection-based serialization, which this attribute
///         also relies on.</item>
/// </list>
/// </summary>
public abstract class PersistedRecord
{
    /// <summary>
    /// JSON properties found in the stored data that this version of the class does not declare,
    /// preserved so that saving the record does not delete them. <c>null</c> when there are none
    /// (the normal case). Written back verbatim by <see cref="Data.JsonHelpers.Serialize{T}"/>.
    /// See the class remarks for the rule that applies to code that rebuilds records.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>
    /// Replaces this record's <see cref="ExtensionData"/> with a copy of <paramref name="source"/>'s,
    /// so that a rebuilt object carries the unknown fields of the object it replaces. A <c>null</c>
    /// <paramref name="source"/>, or one with no unknown fields, leaves this record with none.
    /// <para>
    /// The entries are copied into a <b>new</b> dictionary so the two records never share mutable
    /// state (adding a key to one cannot show up in the other). The <see cref="JsonElement"/> values
    /// themselves are immutable snapshots of parsed JSON, so copying the entries by value is a
    /// complete, safe copy; they need no deep clone.
    /// </para>
    /// </summary>
    /// <param name="source">
    /// The record whose unknown fields should be carried over: the original that is being replaced.
    /// Normally the same type as this record; <c>null</c> for a record with no original (a new row).
    /// </param>
    public void CopyExtensionDataFrom(PersistedRecord? source)
    {
        ExtensionData = source?.ExtensionData is { Count: > 0 } entries
            ? new Dictionary<string, JsonElement>(entries)
            : null;
    }
}

/// <summary>
/// Fluent helper for <see cref="PersistedRecord.CopyExtensionDataFrom"/>, so a rebuilt object can
/// pick up its original's unknown fields inside an object-initializer expression or a LINQ
/// <c>Select</c> without needing a temporary variable.
/// </summary>
public static class PersistedRecordExtensions
{
    /// <summary>
    /// Copies <paramref name="source"/>'s unknown JSON fields onto <paramref name="target"/> (see
    /// <see cref="PersistedRecord.CopyExtensionDataFrom"/>) and returns <paramref name="target"/>.
    /// </summary>
    /// <typeparam name="T">The type of the rebuilt record.</typeparam>
    /// <param name="target">The freshly built record that replaces <paramref name="source"/>.</param>
    /// <param name="source">The original record being replaced, or <c>null</c> for a new row.</param>
    /// <returns><paramref name="target"/>, for chaining.</returns>
    public static T WithExtensionDataFrom<T>(this T target, PersistedRecord? source)
        where T : PersistedRecord
    {
        target.CopyExtensionDataFrom(source);
        return target;
    }
}
