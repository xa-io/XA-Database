namespace XADatabase.Core.Policies;

public static class IpcInvocationPolicy
{
    public static void RunSafe(
        string channel,
        Action invocation,
        Action<string, Exception> reportFailure)
    {
        try
        {
            invocation();
        }
        catch (Exception ex)
        {
            reportFailure(channel, ex);
        }
    }

    public static T RunSafe<T>(
        string channel,
        Func<T> invocation,
        T fallback,
        Action<string, Exception> reportFailure)
    {
        try
        {
            return invocation();
        }
        catch (Exception ex)
        {
            reportFailure(channel, ex);
            return fallback;
        }
    }
}

public sealed class IpcRegistrationCleanup
{
    private readonly object gate = new();
    private readonly List<(string Channel, Action Unregister)> registrations = new();
    private bool disposed;

    public bool IsDisposed
    {
        get
        {
            lock (gate)
                return disposed;
        }
    }

    public void Add(string channel, Action unregister)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(unregister);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            registrations.Add((channel, unregister));
        }
    }

    public void Dispose(Action<string, Exception> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(reportFailure);
        List<(string Channel, Action Unregister)> pending;
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            pending = registrations.ToList();
            registrations.Clear();
        }

        for (var index = pending.Count - 1; index >= 0; index--)
        {
            var registration = pending[index];
            try
            {
                registration.Unregister();
            }
            catch (Exception ex)
            {
                try { reportFailure(registration.Channel, ex); }
                catch { }
            }
        }
    }
}
