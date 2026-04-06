using MiyuAgents.Core;
using MiyuAgents.Memory;

namespace MiyuAgents.Tests.Unit.Fakes;

/// <summary>
/// Configurable IConsolidationHandler for unit testing.
/// Tracks calls and optionally throws to test chain fault-tolerance.
/// </summary>
public sealed class FakeConsolidationHandler : IConsolidationHandler
{
    private readonly Exception? _throwOnHandle;
    private int _callCount;

    public string HandlerName { get; }
    public int    CallCount   => _callCount;

    public FakeConsolidationHandler(string name, Exception? throwOnHandle = null)
    {
        HandlerName    = name;
        _throwOnHandle = throwOnHandle;
    }

    public Task<ConsolidationHandlerResult> HandleAsync(
        ExchangeRecord exchange, AgentContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);

        if (_throwOnHandle is not null)
            throw _throwOnHandle;

        return Task.FromResult(new ConsolidationHandlerResult(HandlerName, Succeeded: true));
    }

    // ── Factory helpers ──────────────────────────────────────────────────────

    public static FakeConsolidationHandler Ok(string name)
        => new(name);

    public static FakeConsolidationHandler Failing(string name)
        => new(name, new InvalidOperationException($"Simulated failure in {name}"));
}
