namespace XADatabase.Core.Collection;

public enum SectionState
{
    Available,
    AuthoritativeEmpty,
    Unavailable,
    Failed,
}

public sealed record SectionResult<T>(SectionState State, T Value, string Detail = "")
{
    public bool CanReplacePersisted => State is SectionState.Available or SectionState.AuthoritativeEmpty;

    public bool RequiresWarning => State is SectionState.Unavailable or SectionState.Failed
        || !string.IsNullOrWhiteSpace(Detail);

    public static SectionResult<T> Available(T value, string detail = "") => new(SectionState.Available, value, detail);

    public static SectionResult<T> AuthoritativeEmpty(T value, string detail = "") => new(SectionState.AuthoritativeEmpty, value, detail);

    public static SectionResult<T> Unavailable(T value, string detail) => new(SectionState.Unavailable, value, detail);

    public static SectionResult<T> Failed(T value, string detail) => new(SectionState.Failed, value, detail);
}
