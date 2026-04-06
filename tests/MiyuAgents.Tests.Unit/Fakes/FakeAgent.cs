using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Core.Events;

namespace MiyuAgents.Tests.Unit.Fakes;

/// <summary>
/// Configurable AgentBase&lt;string&gt; for unit testing.
/// The executor function controls what ExecuteCoreAsync returns or throws.
/// </summary>
public class FakeAgent : AgentBase<string>
{
    private readonly Func<AgentContext, CancellationToken, Task<string?>> _executor;

    public FakeAgent(
        string agentId   = "fake-agent",
        string agentName = "FakeAgent",
        AgentRole role   = AgentRole.Custom,
        Func<AgentContext, CancellationToken, Task<string?>>? executor = null)
        : base(NullLogger<AgentBase<string>>.Instance)
    {
        AgentId   = agentId;
        AgentName = agentName;
        Role      = role;
        _executor = executor ?? ((_, _) => Task.FromResult<string?>("ok"));
    }

    public override string    AgentId   { get; }
    public override string    AgentName { get; }
    public override AgentRole Role      { get; }

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
        => _executor(ctx, ct);

    // ── Factory helpers ──────────────────────────────────────────────────────

    /// <summary>Always returns the given value.</summary>
    public static FakeAgent Returns(string? value, string id = "fake-agent")
        => new(id, executor: (_, _) => Task.FromResult(value));

    /// <summary>Always throws the given exception.</summary>
    public static FakeAgent Throws(Exception ex, string id = "fake-agent")
        => new(id, executor: (_, _) => throw ex);

    /// <summary>Delays then returns a value (useful for concurrency tests).</summary>
    public static FakeAgent DelayedReturns(string value, TimeSpan delay, string id = "fake-agent")
        => new(id, executor: async (_, ct) =>
        {
            await Task.Delay(delay, ct);
            return value;
        });

    /// <summary>Writes a value into ctx.Results.LlmResponse before returning.</summary>
    public static FakeAgent WritesToAccumulator(string response, string id = "fake-agent")
        => new(id, executor: (ctx, _) =>
        {
            ctx.Results.LlmResponse = response;
            return Task.FromResult<string?>(response);
        });
}
