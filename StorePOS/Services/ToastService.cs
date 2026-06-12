namespace StorePOS.Services;

public enum ToastSeverity
{
    Info,
    Success,
    Error,
}

public sealed record ToastItem(Guid Id, string Message, ToastSeverity Severity, DateTimeOffset Created);

public sealed class ToastService
{
    private readonly List<ToastItem> _items = new();
    private readonly object _lock = new();

    public event Action? Changed;

    public IReadOnlyList<ToastItem> Snapshot
    {
        get
        {
            lock (_lock)
                return _items.ToList();
        }
    }

    public void Show(string message, ToastSeverity severity = ToastSeverity.Info) { }

    public void Success(string message) { }

    public void Error(string message) { }

    public void Info(string message) { }

    public void Dismiss(Guid id) { }
}
