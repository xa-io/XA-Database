namespace XADatabase.Collectors;

/// <summary>
/// Common interface for all data collectors.
/// Enables uniform error handling, timing, and future extensibility.
/// </summary>
public interface ICollector<T>
{
    /// <summary>
    /// Collect data from the game state. Returns null if data is unavailable.
    /// </summary>
    T? Collect();

    /// <summary>
    /// Human-readable name for logging and diagnostics.
    /// </summary>
    string Name { get; }
}
