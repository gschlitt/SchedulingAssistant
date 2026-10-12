using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TermPoint.Data;
using TermPoint.Models;
using TermPoint.Services;
using Xunit;

namespace TermPoint.Tests;

/// <summary>
/// Shared helpers for the "preserve unknown JSON fields" tests (spec_version_1.2.3.md, item 3):
/// the standard "future" fields a newer app version might have stored, reflection helpers that
/// enumerate every persisted type, and a fixture that stamps those fields into real database rows.
/// </summary>
internal static class FutureFields
{
    /// <summary>Name of the unknown object-valued field a "newer version" stored.</summary>
    public const string ObjectFieldName = "futureField";

    /// <summary>Name of the unknown scalar field a "newer version" stored.</summary>
    public const string ScalarFieldName = "futureScalar";

    /// <summary>Name of the optional marker field that records which record carries these fields.</summary>
    public const string OriginFieldName = "origin";

    /// <summary>The JSON text of the object-valued future field.</summary>
    public const string ObjectFieldJson = "{\"nested\":[1,2,3],\"s\":\"x\"}";

    /// <summary>The value of the scalar future field.</summary>
    public const int ScalarFieldValue = 42;

    /// <summary>
    /// Builds an extension-data dictionary holding the standard future fields, plus an
    /// <c>origin</c> marker when one is given. The marker lets a test tell which original record's
    /// unknown fields ended up on which rebuilt record.
    /// </summary>
    /// <param name="origin">Optional marker value identifying the record these fields belong to.</param>
    /// <returns>A new dictionary of <see cref="JsonElement"/> snapshots.</returns>
    public static Dictionary<string, JsonElement> Make(string? origin = null)
    {
        var d = new Dictionary<string, JsonElement>
        {
            [ObjectFieldName] = JsonDocument.Parse(ObjectFieldJson).RootElement.Clone(),
            [ScalarFieldName] = JsonDocument.Parse(ScalarFieldValue.ToString()).RootElement.Clone(),
        };
        if (origin is not null)
            d[OriginFieldName] = JsonDocument.Parse(JsonSerializer.Serialize(origin)).RootElement.Clone();
        return d;
    }

    /// <summary>
    /// Asserts that <paramref name="node"/> is a JSON object carrying both standard future fields
    /// with their exact values, and (when given) the expected <c>origin</c> marker.
    /// </summary>
    /// <param name="node">The JSON object to inspect (a persisted record or a nested element).</param>
    /// <param name="where">A description of the record, used in failure messages.</param>
    /// <param name="origin">The expected marker, or null to skip that check.</param>
    public static void AssertPresent(JsonNode? node, string where, string? origin = null)
    {
        var obj = Assert.IsType<JsonObject>(node);
        Assert.True(obj.ContainsKey(ObjectFieldName), $"{where}: '{ObjectFieldName}' was dropped. JSON: {obj.ToJsonString()}");
        Assert.True(obj.ContainsKey(ScalarFieldName), $"{where}: '{ScalarFieldName}' was dropped. JSON: {obj.ToJsonString()}");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ObjectFieldJson), obj[ObjectFieldName]),
            $"{where}: '{ObjectFieldName}' changed value. JSON: {obj.ToJsonString()}");
        Assert.Equal(ScalarFieldValue, obj[ScalarFieldName]!.GetValue<int>());
        if (origin is not null)
            Assert.Equal(origin, obj[OriginFieldName]?.GetValue<string>());
    }

    /// <summary>
    /// Asserts that <paramref name="node"/> carries NO future fields (a row that never had any, or a
    /// brand-new record, must not acquire them).
    /// </summary>
    /// <param name="node">The JSON object to inspect.</param>
    /// <param name="where">A description of the record, used in failure messages.</param>
    public static void AssertAbsent(JsonNode? node, string where)
    {
        var obj = Assert.IsType<JsonObject>(node);
        Assert.False(obj.ContainsKey(ObjectFieldName) || obj.ContainsKey(ScalarFieldName),
            $"{where}: unexpectedly carries future fields. JSON: {obj.ToJsonString()}");
    }

    /// <summary>
    /// Adds both standard future fields to <paramref name="node"/> and, recursively, to every JSON
    /// object nested inside it (including objects inside arrays).
    /// </summary>
    /// <param name="node">The JSON tree to modify in place.</param>
    /// <returns>How many objects received the fields.</returns>
    public static int InjectEverywhere(JsonNode? node)
    {
        int count = 0;
        switch (node)
        {
            case JsonObject obj:
                // Recurse into the existing children first so the injected object is not itself
                // injected into.
                foreach (var child in obj.Select(kv => kv.Value).ToList())
                    count += InjectEverywhere(child);
                obj[ObjectFieldName] = JsonNode.Parse(ObjectFieldJson);
                obj[ScalarFieldName] = ScalarFieldValue;
                count++;
                break;
            case JsonArray arr:
                foreach (var child in arr.ToList())
                    count += InjectEverywhere(child);
                break;
        }
        return count;
    }

    /// <summary>
    /// Counts the JSON objects, anywhere in <paramref name="node"/>, that carry the scalar future
    /// field (used to prove every injected object survived).
    /// </summary>
    /// <param name="node">The JSON tree to search.</param>
    /// <returns>The number of objects with a <c>futureScalar</c> property.</returns>
    public static int CountCarriers(JsonNode? node) => node switch
    {
        JsonObject obj => (obj.ContainsKey(ScalarFieldName) ? 1 : 0)
                          + obj.Select(kv => CountCarriers(kv.Value)).Sum(),
        JsonArray arr  => arr.Select(CountCarriers).Sum(),
        _              => 0,
    };

    /// <summary>
    /// Fails if any JSON object in <paramref name="json"/> contains the same property name twice.
    /// <c>JsonNode</c> silently collapses duplicates, so the raw token stream is walked instead.
    /// A duplicate would mean a field was written both from a declared property and from the
    /// extension data.
    /// </summary>
    /// <param name="json">The JSON text to scan.</param>
    public static void AssertNoDuplicateKeys(string json)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        var scopes = new Stack<HashSet<string>?>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject: scopes.Push(new HashSet<string>()); break;
                case JsonTokenType.StartArray:  scopes.Push(null); break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:    scopes.Pop(); break;
                case JsonTokenType.PropertyName:
                    var name = reader.GetString()!;
                    Assert.True(scopes.Peek()!.Add(name), $"Duplicate JSON property '{name}' in: {json}");
                    break;
            }
        }
    }
}

/// <summary>
/// Reflection over the persisted model types, so tests cover every class that is serialized into a
/// database <c>data</c> column, including ones added in future, without a hand-maintained list.
/// </summary>
internal static class PersistedTypeCatalog
{
    /// <summary>
    /// Every concrete class in the app that derives from <see cref="PersistedRecord"/>: the top-level
    /// records, the records nested inside them, and the semester payload.
    /// </summary>
    public static IReadOnlyList<Type> All { get; } =
        typeof(PersistedRecord).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(PersistedRecord)))
            .OrderBy(t => t.Name)
            .ToList();

    /// <summary>Theory data: one row per persisted type.</summary>
    public static IEnumerable<object[]> AllAsTheoryData() => All.Select(t => new object[] { t });

    /// <summary>
    /// Builds an instance of <paramref name="type"/> in which every settable, non-ignored property
    /// holds a non-default value, and every list holds one populated element (recursively), so its
    /// JSON exercises each declared field and each nested record type.
    /// </summary>
    /// <param name="type">The persisted type to populate.</param>
    /// <returns>A populated instance.</returns>
    public static object Populate(Type type)
    {
        var instance = Activator.CreateInstance(type)!;
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.SetMethod is not { IsPublic: true }) continue;
            if (p.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;
            if (p.Name == nameof(PersistedRecord.ExtensionData)) continue;
            var value = ValueFor(p.PropertyType, p.Name);
            if (value is not null) p.SetValue(instance, value);
        }
        return instance;
    }

    /// <summary>Makes a non-default value of the given property type (null if unsupported).</summary>
    private static object? ValueFor(Type t, string name)
    {
        if (t == typeof(string)) return "v-" + name;
        if (t == typeof(int) || t == typeof(int?)) return 7;
        if (t == typeof(decimal) || t == typeof(decimal?)) return 1.5m;
        if (t == typeof(double) || t == typeof(double?)) return 2.5;
        if (t == typeof(bool)) return true;
        if (t == typeof(char)) return 'Q';
        if (t.IsEnum) return Enum.GetValues(t).Cast<object>().Last();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var element = t.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(t)!;
            if (element == typeof(string)) { list.Add("a-" + name); list.Add("b-" + name); }
            else if (element == typeof(int)) { list.Add(1); list.Add(2); }
            else if (element.IsClass) list.Add(Populate(element));
            return list;
        }
        return null;
    }

    /// <summary>Calls <see cref="JsonHelpers.Serialize{T}"/> for a runtime type.</summary>
    public static string Serialize(Type type, object value) =>
        (string)typeof(JsonHelpers).GetMethod(nameof(JsonHelpers.Serialize))!
            .MakeGenericMethod(type).Invoke(null, new[] { value })!;

    /// <summary>Calls <see cref="JsonHelpers.Deserialize{T}"/> for a runtime type.</summary>
    public static object Deserialize(Type type, string json) =>
        typeof(JsonHelpers).GetMethod(nameof(JsonHelpers.Deserialize))!
            .MakeGenericMethod(type).Invoke(null, new object[] { json })!;
}

/// <summary>
/// A real SQLite database (full app schema, in a temp folder) plus a write lock held as writer, for
/// tests that drive repositories and list view models end to end. Provides helpers to stamp the
/// standard future fields into a row's <c>data</c> column (simulating a newer app version) and to
/// read the raw JSON back (to see exactly what was persisted).
/// </summary>
internal sealed class ExtensionDataDbFixture : IDisposable
{
    private readonly string _tempDir;

    /// <summary>The database context (real schema, real repositories can be built over it).</summary>
    public DatabaseContext Db { get; }

    /// <summary>A write lock that has been acquired, so write commands report CanExecute = true.</summary>
    public WriteLockService Lock { get; }

    /// <summary>Creates the temp database and acquires the write lock.</summary>
    public ExtensionDataDbFixture()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"extdata_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "test.db");
        Db = new DatabaseContext(dbPath);
        Lock = new WriteLockService();
        Lock.TryAcquire(dbPath);
        Assert.True(Lock.IsWriter, "The fixture needs to hold the write lock.");
    }

    /// <summary>
    /// Adds the standard future fields (and an <c>origin</c> marker) to the JSON at
    /// <paramref name="path"/> inside the <c>data</c> column of one row, the way a newer app
    /// version would have stored them.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="id">The row's id.</param>
    /// <param name="path">The SQLite JSON path of the object to stamp: "$" for the record itself, or
    /// e.g. "$.schedule[0]" for a nested element.</param>
    /// <param name="origin">The marker value stored in the object's <c>origin</c> field.</param>
    public void Stamp(string table, string id, string path = "$", string origin = "root")
    {
        using var cmd = Db.Connection.CreateCommand();
        cmd.CommandText =
            $"UPDATE {table} SET data = json_set(data, " +
            $"'{path}.{FutureFields.ObjectFieldName}', json($ff), " +
            $"'{path}.{FutureFields.ScalarFieldName}', {FutureFields.ScalarFieldValue}, " +
            $"'{path}.{FutureFields.OriginFieldName}', $origin) WHERE id = $id";
        var ff = cmd.CreateParameter(); ff.ParameterName = "$ff"; ff.Value = FutureFields.ObjectFieldJson; cmd.Parameters.Add(ff);
        var o = cmd.CreateParameter(); o.ParameterName = "$origin"; o.Value = origin; cmd.Parameters.Add(o);
        var i = cmd.CreateParameter(); i.ParameterName = "$id"; i.Value = id; cmd.Parameters.Add(i);
        Assert.Equal(1, cmd.ExecuteNonQuery());
    }

    /// <summary>Reads the raw <c>data</c> JSON text of one row, exactly as persisted.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="id">The row's id.</param>
    /// <returns>The JSON text.</returns>
    public string ReadDataText(string table, string id)
    {
        using var cmd = Db.Connection.CreateCommand();
        cmd.CommandText = $"SELECT data FROM {table} WHERE id = $id";
        var p = cmd.CreateParameter(); p.ParameterName = "$id"; p.Value = id; cmd.Parameters.Add(p);
        return cmd.ExecuteScalar() as string
               ?? throw new InvalidOperationException($"No row {id} in {table}.");
    }

    /// <summary>Reads the raw <c>data</c> JSON of one row and parses it.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="id">The row's id.</param>
    /// <returns>The parsed JSON tree.</returns>
    public JsonNode ReadData(string table, string id) => JsonNode.Parse(ReadDataText(table, id))!;

    /// <summary>Releases the lock and database and deletes the temp folder (best effort).</summary>
    public void Dispose()
    {
        Lock.Dispose();
        Db.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }
}
