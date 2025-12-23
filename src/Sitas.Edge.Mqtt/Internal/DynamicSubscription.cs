namespace Sitas.Edge.Mqtt.Internal;

/// <summary>
/// Represents a dynamic subscription that can be disposed to unsubscribe.
/// </summary>
internal sealed class DynamicSubscription : IAsyncDisposable
{
    private readonly string _topic;
    private readonly Func<string, Task> _unsubscribeAction;
    private int _disposed;

    public DynamicSubscription(string topic, Func<string, Task> unsubscribeAction)
    {
        _topic = topic;
        _unsubscribeAction = unsubscribeAction;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            await _unsubscribeAction(_topic).ConfigureAwait(false);
        }
    }
}

