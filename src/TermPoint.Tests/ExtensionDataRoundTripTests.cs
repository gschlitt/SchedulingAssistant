using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TermPoint.Data;
using TermPoint.Models;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Spec item 3 ("Preserve unknown JSON fields across app versions"), serialization layer.
/// <para>
/// Records are stored as JSON, and a client that does not declare a field used to drop it on load
/// and so delete it on the next save. Every persisted class now derives from
/// <see cref="PersistedRecord"/>, whose <c>[JsonExtensionData]</c> property parks unknown fields and
/// writes them back. These tests prove that for <b>every</b> persisted type, found by reflection so
/// that a new type is covered automatically, and guard the type list itself so that a new class
/// that forgets the base type fails here instead of silently losing data in production.
/// </para>
/// </summary>
public class ExtensionDataRoundTripTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // Round trip, every persisted type
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For each persisted type: take the JSON of a fully populated instance, add the unknown fields
    /// <c>futureField</c> (an object) and <c>futureScalar</c> (42) to the record <i>and to every nested
    /// record inside it</i>, load it and save it again with <see cref="JsonHelpers"/>, and require
    /// the result to equal the input exactly. Nested types are covered through their parents (a
    /// section's meetings, assignments and reserves).
    /// </summary>
    [Theory]
    [MemberData(nameof(PersistedTypeCatalog.AllAsTheoryData), MemberType = typeof(PersistedTypeCatalog))]
    public void UnknownFields_SurviveLoadAndSave_ForEveryPersistedType(Type type)
    {
        // The record's normal fields, as the current version would write them.
        var input = JsonNode.Parse(PersistedTypeCatalog.Serialize(type, PersistedTypeCatalog.Populate(type)))!;
        int injected = FutureFields.InjectEverywhere(input);
        var inputJson = input.ToJsonString();

        // Load (as a repository does) and save (as its Update does).
        var loaded = PersistedTypeCatalog.Deserialize(type, inputJson);
        var outputJson = PersistedTypeCatalog.Serialize(type, loaded);
        var output = JsonNode.Parse(outputJson)!;

        Assert.True(JsonNode.DeepEquals(input, output),
            $"{type.Name}: the saved JSON differs from the loaded JSON.{Environment.NewLine}" +
            $"loaded: {inputJson}{Environment.NewLine}saved:  {outputJson}");

        // DeepEquals alone could pass vacuously if the helper injected nothing; count the carriers.
        Assert.Equal(injected, FutureFields.CountCarriers(output));
        FutureFields.AssertPresent(output, type.Name);

        // A field must never be written twice (once from a property and once from extension data).
        FutureFields.AssertNoDuplicateKeys(outputJson);
    }

    /// <summary>
    /// Old databases contain no unknown fields. Loading such a record leaves
    /// <see cref="PersistedRecord.ExtensionData"/> null (no allocation) and saving it writes exactly
    /// the JSON that was read, so existing data is not touched. Also proves that derived, get-only
    /// members that are written but never read back (<c>endMinutes</c>, <c>isRemote</c>,
    /// <c>dayDisplay</c>, ...) are not mistaken for unknown fields and written twice.
    /// </summary>
    [Theory]
    [MemberData(nameof(PersistedTypeCatalog.AllAsTheoryData), MemberType = typeof(PersistedTypeCatalog))]
    public void RecordsWithoutUnknownFields_AreWrittenUnchanged(Type type)
    {
        var json = PersistedTypeCatalog.Serialize(type, PersistedTypeCatalog.Populate(type));

        var loaded = (PersistedRecord)PersistedTypeCatalog.Deserialize(type, json);

        Assert.Null(loaded.ExtensionData);
        var resaved = PersistedTypeCatalog.Serialize(type, loaded);
        Assert.Equal(json, resaved);
        FutureFields.AssertNoDuplicateKeys(resaved);
    }

    /// <summary>
    /// The specific example from the task: a meeting (nested record) inside a section keeps its own
    /// unknown fields, distinct from the section's, and values of every JSON kind survive.
    /// </summary>
    [Fact]
    public void NestedRecords_KeepTheirOwnUnknownFields_InsideASection()
    {
        const string json = """
            {
              "id": "s1", "semesterId": "sem", "courseId": "c1", "sectionCode": "A",
              "futureField": {"nested": [1, 2, 3], "s": "x"}, "futureScalar": 42,
              "futureNull": null, "futureBool": true, "futureText": "hello", "futureDecimal": 1.25,
              "schedule": [
                { "day": 1, "startMinutes": 510, "durationMinutes": 90,
                  "futureField": {"nested": [1, 2, 3], "s": "x"}, "futureScalar": 42, "meetingOnly": "m" }
              ],
              "instructorAssignments": [
                { "instructorId": "i1", "workload": 1, "assignmentOnly": ["a", "b"] }
              ],
              "reserves": [
                { "reserveId": "r1", "code": 5, "reserveOnly": {"k": "v"} }
              ]
            }
            """;

        var section = JsonHelpers.Deserialize<Section>(json);

        Assert.NotNull(section.ExtensionData);
        Assert.Contains("futureNull", section.ExtensionData!.Keys);
        Assert.Contains("meetingOnly", section.Schedule[0].ExtensionData!.Keys);
        Assert.DoesNotContain("meetingOnly", section.ExtensionData.Keys);
        Assert.Contains("assignmentOnly", section.InstructorAssignments[0].ExtensionData!.Keys);
        Assert.Contains("reserveOnly", section.Reserves[0].ExtensionData!.Keys);

        var saved = JsonNode.Parse(JsonHelpers.Serialize(section))!;
        Assert.Null(saved["futureNull"]);
        Assert.True(saved.AsObject().ContainsKey("futureNull"));       // an explicit null is preserved too
        Assert.True(saved["futureBool"]!.GetValue<bool>());
        Assert.Equal("hello", saved["futureText"]!.GetValue<string>());
        Assert.Equal(1.25m, saved["futureDecimal"]!.GetValue<decimal>());
        FutureFields.AssertPresent(saved["schedule"]![0], "schedule[0]");
        Assert.Equal("m", saved["schedule"]![0]!["meetingOnly"]!.GetValue<string>());
        Assert.Equal("b", saved["instructorAssignments"]![0]!["assignmentOnly"]![1]!.GetValue<string>());
        Assert.Equal("v", saved["reserves"]![0]!["reserveOnly"]!["k"]!.GetValue<string>());
        // Known fields were read normally.
        Assert.Equal(510, section.Schedule[0].StartMinutes);
        Assert.Equal("i1", section.InstructorAssignments[0].InstructorId);
    }

    /// <summary>
    /// Keys are kept exactly as stored. The camelCase naming policy neither renames them nor lets
    /// a stale PascalCase key from an old version collide with its camelCase successor (which would
    /// otherwise be a duplicate-key write).
    /// </summary>
    [Fact]
    public void StaleKeys_AreKeptVerbatim_AndNeverCollideWithDeclaredFields()
    {
        var campus = JsonHelpers.Deserialize<Campus>("""{"id":"c","name":"Main","Name":"stale","LegacyCode":7}""");

        Assert.Equal("Main", campus.Name);                                  // declared field still binds
        Assert.Equal(new[] { "LegacyCode", "Name" }, campus.ExtensionData!.Keys.OrderBy(k => k));

        var json = JsonHelpers.Serialize(campus);
        FutureFields.AssertNoDuplicateKeys(json);
        var node = JsonNode.Parse(json)!;
        Assert.Equal("Main", node["name"]!.GetValue<string>());
        Assert.Equal("stale", node["Name"]!.GetValue<string>());
        Assert.Equal(7, node["LegacyCode"]!.GetValue<int>());
    }

    /// <summary>
    /// <see cref="PersistedRecord.CopyExtensionDataFrom"/>: copies into a NEW dictionary (so the two
    /// records never share mutable state), maps null and empty sources to null, and replaces what the
    /// target had.
    /// </summary>
    [Fact]
    public void CopyExtensionDataFrom_CopiesIntoANewDictionary()
    {
        var source = new SectionReserve { ExtensionData = FutureFields.Make("src") };
        var target = new SectionReserve();

        target.CopyExtensionDataFrom(source);

        Assert.NotNull(target.ExtensionData);
        Assert.NotSame(source.ExtensionData, target.ExtensionData);
        Assert.Equal(source.ExtensionData!.Count, target.ExtensionData!.Count);
        target.ExtensionData["added"] = JsonDocument.Parse("1").RootElement.Clone();
        Assert.False(source.ExtensionData.ContainsKey("added"));            // independent

        target.CopyExtensionDataFrom(null);                                  // no original -> none
        Assert.Null(target.ExtensionData);

        target.ExtensionData = FutureFields.Make();
        target.CopyExtensionDataFrom(new SectionReserve { ExtensionData = new() });   // empty original -> none
        Assert.Null(target.ExtensionData);

        var fluent = new SectionReserve { ReserveId = "r" }.WithExtensionDataFrom(source);   // fluent form
        Assert.Equal("r", fluent.ReserveId);
        Assert.Equal(source.ExtensionData.Count, fluent.ExtensionData!.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Guards on the list of persisted types itself
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The catalog found by reflection must include every type known to be stored in a <c>data</c>
    /// column. If someone removes the base type from one, it drops out of the catalog and this
    /// fails (and the round-trip theory would otherwise quietly stop covering it).
    /// </summary>
    [Fact]
    public void Catalog_ContainsEveryKnownPersistedType()
    {
        Type[] known =
        [
            // top-level records (one per data-bearing table)
            typeof(AcademicUnit), typeof(AcademicYear), typeof(BlockPattern), typeof(Campus),
            typeof(Course), typeof(Instructor), typeof(InstructorCommitment), typeof(Meeting),
            typeof(ProgramWatch), typeof(Release), typeof(Room), typeof(SchedulingEnvironmentValue),
            typeof(SchedulingNote), typeof(Section), typeof(SectionCodePattern), typeof(Subject),
            typeof(Semester),
            // records nested inside them
            typeof(SectionDaySchedule), typeof(InstructorAssignment), typeof(SectionReserve),
        ];

        foreach (var t in known)
            Assert.True(PersistedTypeCatalog.All.Contains(t),
                $"{t.Name} must derive from PersistedRecord so unknown JSON fields survive a save.");

        // The semester payload type (internal) is covered by the catalog as well.
        Assert.Contains(PersistedTypeCatalog.All, t => t.Name == "SemesterData");
    }

    /// <summary>
    /// Every class-typed value reachable from a persisted type (a property, or the element of a
    /// list property) that lives in this app must itself be a <see cref="PersistedRecord"/>.
    /// This is what catches a NEW nested record type added to an existing entity: it would
    /// otherwise silently discard its own unknown fields even though its parent is protected.
    /// </summary>
    [Fact]
    public void EveryClassNestedInAPersistedType_IsAPersistedRecord()
    {
        foreach (var type in PersistedTypeCatalog.All)
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.SetMethod is not { IsPublic: true }) continue;
                if (p.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;

                var t = p.PropertyType;
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                    t = t.GetGenericArguments()[0];

                bool isAppClass = t.IsClass && t.Namespace is { } ns && ns.StartsWith("TermPoint", StringComparison.Ordinal);
                if (!isAppClass) continue;

                Assert.True(t.IsSubclassOf(typeof(PersistedRecord)),
                    $"{type.Name}.{p.Name} holds a {t.Name}, which is serialized with its parent but " +
                    $"does not derive from PersistedRecord. Make it derive, or its unknown JSON fields " +
                    $"will be dropped on save.");
            }
        }
    }

    /// <summary>
    /// Every type the data layer deserializes with <c>JsonHelpers.Deserialize&lt;T&gt;</c> or
    /// <c>JsonSerializer.Deserialize&lt;T&gt;</c> (found by scanning the repositories' source) must be a
    /// <see cref="PersistedRecord"/>, apart from plain lists of primitives (the legal-start-time
    /// column holds a list of ints). This catches a brand-new repository for a brand-new entity
    /// that forgot the base type, which the reflection-only guards cannot see.
    /// </summary>
    [Fact]
    public void EveryTypeTheRepositoriesDeserialize_IsAPersistedRecord()
    {
        var dataDir = LocateDataDirectory();
        if (dataDir is null) return;   // tests run away from the source tree (relocated binaries): nothing to scan

        var generic = new Regex(@"(?:JsonHelpers|JsonSerializer)\.Deserialize<(?<t>[A-Za-z0-9_]+)(?:<(?<arg>[A-Za-z0-9_]+)>)?>");
        var seen = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(dataDir, "*.cs", SearchOption.AllDirectories))
        {
            // JsonHelpers itself only mentions the generic parameter "T", not a real entity.
            if (Path.GetFileName(file) == "JsonHelpers.cs") continue;

            foreach (Match m in generic.Matches(File.ReadAllText(file)))
            {
                if (m.Groups["arg"].Success) continue;          // List<int> and the like: primitives only
                seen.Add(m.Groups["t"].Value);
            }
        }

        Assert.NotEmpty(seen);                                    // the scan actually found the repositories
        Assert.Contains("Section", seen);                         // sanity: a known one
        foreach (var name in seen)
        {
            var type = PersistedTypeCatalog.All.FirstOrDefault(t => t.Name == name);
            Assert.True(type is not null,
                $"The data layer deserializes '{name}', but no PersistedRecord with that name exists. " +
                $"Every type stored in a data column must derive from PersistedRecord.");
        }
    }

    /// <summary>
    /// Finds <c>src/TermPoint/Data</c> relative to this test source file, which is baked in at
    /// compile time and so works wherever the test binaries are written (including the temp output
    /// folder used for full-suite runs).
    /// </summary>
    /// <param name="thisFile">Filled in by the compiler with this file's path.</param>
    /// <returns>The directory, or null if the source tree is not present.</returns>
    private static string? LocateDataDirectory([CallerFilePath] string thisFile = "")
    {
        if (string.IsNullOrEmpty(thisFile)) return null;
        var dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "TermPoint", "Data"));
        return Directory.Exists(dir) ? dir : null;
    }
}
