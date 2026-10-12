using System.Text.Json;

namespace TermPoint.Data;

/// <summary>
/// Serializes and deserializes the entities stored in the database's <c>data</c> JSON columns,
/// using camelCase property names.
/// <para>
/// Every persisted class derives from <see cref="Models.PersistedRecord"/>, whose
/// <c>[JsonExtensionData]</c> property makes this round trip lossless for fields the running app
/// version does not declare: on load they are parked in <c>ExtensionData</c>, and on save they are
/// written back verbatim. No option here is needed for that. In particular the camelCase naming
/// policy does not rename extension keys (they are written exactly as they were read), and there
/// are no custom converters or source-generated contexts, so the browser build's reflection-based
/// serialization handles it the same way as the desktop build.
/// </para>
/// </summary>
public static class JsonHelpers
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serializes <paramref name="value"/> to the JSON stored in a <c>data</c> column, including any
    /// unknown fields carried in its <see cref="Models.PersistedRecord.ExtensionData"/>.
    /// </summary>
    /// <typeparam name="T">The type being serialized.</typeparam>
    /// <param name="value">The entity (or list of primitives) to serialize.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    /// <summary>
    /// Deserializes a JSON string into the specified type.
    /// </summary>
    /// <typeparam name="T">The target type to deserialize into.</typeparam>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="JsonException">
    /// Thrown if the JSON deserializes to null (e.g. the input is "null", an empty string, or corrupt data).
    /// The exception message includes the type name and a preview of the input to aid debugging.
    /// </exception>
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException(
            $"Deserialized null from JSON for type {typeof(T).Name}. Input: {json[..Math.Min(json.Length, 200)]}");
}
