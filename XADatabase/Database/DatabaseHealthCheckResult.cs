namespace XADatabase.Database;

public sealed class DatabaseHealthCheckResult
{
    public string DbPath { get; init; } = string.Empty;
    public string CheckedAtUtc { get; set; } = string.Empty;
    public bool ReadOk { get; set; }
    public bool WriteOk { get; set; }
    public bool IntegrityOk { get; set; }
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
