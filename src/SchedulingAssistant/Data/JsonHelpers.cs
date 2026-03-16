using System.Text.Json;

namespace SchedulingAssistant.Data;

public static class JsonHelpers
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
