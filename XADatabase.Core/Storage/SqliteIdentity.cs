namespace XADatabase.Core.Storage;

public static class SqliteIdentity
{
    public static long Encode(ulong value) => unchecked((long)value);

    public static ulong Decode(long value) => unchecked((ulong)value);
}
