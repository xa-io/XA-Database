namespace XADatabase.Services;

public enum AddonTriggerKind
{
    Open,
    Close,
}

public sealed class AddonTriggerEvent
{
    public AddonTriggerKind Kind { get; init; }
    public string Category { get; init; } = string.Empty;
    public string AddonName { get; init; } = string.Empty;
    public string AddonDetail { get; init; } = string.Empty;
    public nint AddonPtr { get; init; }
    public bool IsPersistent { get; init; }
    public bool TriggersSave { get; init; }
    public string TriggerDetail => $"{Category}:{(string.IsNullOrEmpty(AddonDetail) ? AddonName : AddonDetail)}";
}
