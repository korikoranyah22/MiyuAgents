namespace MiyuAgents.Llm;

/// <summary>
/// Ambient, provider-neutral attribution for legacy completion abstractions
/// that cannot yet carry <see cref="LlmCallMetadata"/> explicitly.
/// Nested scopes restore the previous value and concurrent async flows remain
/// isolated through <see cref="AsyncLocal{T}"/>.
/// </summary>
public static class LlmCallScope
{
    private static readonly AsyncLocal<LlmCallMetadata?> CurrentSlot = new();

    public static LlmCallMetadata? Current => CurrentSlot.Value;

    public static IDisposable Push(LlmCallMetadata metadata)
    {
        var previous = CurrentSlot.Value;
        CurrentSlot.Value = metadata;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(LlmCallMetadata? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            CurrentSlot.Value = previous;
            _disposed = true;
        }
    }
}
