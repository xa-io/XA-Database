using Microsoft.Data.Sqlite;
using XADatabase.Core.Storage;

namespace XADatabase.Database;

internal static class SqliteParameterCollectionExtensions
{
    public static SqliteParameter AddTypedValue(this SqliteParameterCollection parameters, string name, string? value)
        => Add(parameters, name, SqliteType.Text, value ?? string.Empty);

    public static SqliteParameter AddTypedValue(this SqliteParameterCollection parameters, string name, int value)
        => Add(parameters, name, SqliteType.Integer, value);

    public static SqliteParameter AddTypedValue(this SqliteParameterCollection parameters, string name, long value)
        => Add(parameters, name, SqliteType.Integer, value);

    public static SqliteParameter AddTypedValue(this SqliteParameterCollection parameters, string name, uint value)
        => Add(parameters, name, SqliteType.Integer, (long)value);

    public static SqliteParameter AddTypedValue(this SqliteParameterCollection parameters, string name, ulong value)
        => Add(parameters, name, SqliteType.Integer, SqliteIdentity.Encode(value));

    public static SqliteParameter AddTypedValue(this SqliteParameterCollection parameters, string name, bool value)
        => Add(parameters, name, SqliteType.Integer, value ? 1 : 0);

    private static SqliteParameter Add(
        SqliteParameterCollection parameters,
        string name,
        SqliteType type,
        object value)
    {
        var parameter = parameters.Add(name, type);
        parameter.Value = value;
        return parameter;
    }
}
