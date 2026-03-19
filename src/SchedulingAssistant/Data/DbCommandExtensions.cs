using System.Data.Common;

namespace SchedulingAssistant.Data;

/// <summary>
/// Extension methods for <see cref="DbCommand"/> to provide a convenient parameter-adding API
/// that works across all ADO.NET providers.
/// </summary>
/// <remarks>
/// <see cref="DbParameterCollection"/> (returned by <see cref="DbCommand.Parameters"/>) does not
/// define an <c>AddWithValue</c> method — that method exists only on provider-specific subclasses
/// such as <c>SqliteParameterCollection</c>. This extension bridges the gap so repositories can
/// work against the abstract <see cref="DbCommand"/> type without casting to a concrete provider.
/// <para>
/// Null values are automatically converted to <see cref="DBNull.Value"/> so callers do not need
/// to perform the <c>(object?)x ?? DBNull.Value</c> pattern at every call site.
/// </para>
/// </remarks>
internal static class DbCommandExtensions
{
    /// <summary>
    /// Creates a named parameter, sets its value, and adds it to the command's parameter collection.
    /// </summary>
    /// <param name="cmd">The command to add the parameter to.</param>
    /// <param name="name">The parameter name, including the provider prefix (e.g. <c>"$id"</c>).</param>
    /// <param name="value">
    ///   The parameter value. <c>null</c> is automatically converted to <see cref="DBNull.Value"/>.
    /// </param>
    internal static void AddParam(this DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
