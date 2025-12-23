namespace Sitas.Edge.EdgePlcDriver.Internal;

/// <summary>
/// Represents a dynamic tag subscription that can be disposed to unsubscribe.
/// </summary>
internal sealed class TagSubscription : IAsyncDisposable
{
    private readonly string _tagName;
    private readonly Func<string, Task> _unsubscribeAction;
    private bool _disposed;

    public TagSubscription(string tagName, Func<string, Task> unsubscribeAction)
    {
        _tagName = tagName;
        _unsubscribeAction = unsubscribeAction;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _unsubscribeAction(_tagName).ConfigureAwait(false);
    }
}

/// <summary>
/// Internal representation of a tag subscription handler.
/// </summary>
internal sealed class TagHandler
{
    public required string TagName { get; init; }
    public required Type MessageType { get; init; }
    public required Delegate Handler { get; init; }
    public required int PollingIntervalMs { get; init; }
    public bool OnChangeOnly { get; init; } = true;
    public double Deadband { get; init; } = 0.0;
    public object? LastValue { get; set; }
    public Attributes.TagSubscriptionMode Mode { get; init; } = Attributes.TagSubscriptionMode.Polling;
}

/// <summary>
/// Registration info for attribute-discovered handlers.
/// </summary>
internal sealed class TagHandlerRegistration
{
    public required string TagName { get; init; }
    public required Type HandlerType { get; init; }
    public required Type MessageType { get; init; }
    public required int PollingIntervalMs { get; init; }
    public bool OnChangeOnly { get; init; } = true;
    public double Deadband { get; init; } = 0.0;
    public Attributes.TagSubscriptionMode Mode { get; init; } = Attributes.TagSubscriptionMode.Polling;
}
