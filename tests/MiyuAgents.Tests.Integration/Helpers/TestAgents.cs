using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Core.Attributes;

namespace MiyuAgents.Tests.Integration.Helpers;

// ── Reusable test agents (real AgentBase<string> implementations) ─────────────
// These are NOT fakes — they are lightweight but real framework implementations.

/// <summary>
/// Returns a fixed text and writes it to ctx.Results.LlmResponse.
/// </summary>
internal class EchoAgent(string agentId, string text)
    : AgentBase<string>(NullLogger<AgentBase<string>>.Instance)
{
    public override string    AgentId   => agentId;
    public override string    AgentName => $"Echo-{agentId}";
    public override AgentRole Role      => AgentRole.Conversation;

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        ctx.Results.LlmResponse = text;
        return Task.FromResult<string?>(text);
    }
}

/// <summary>
/// Writes value to ctx.Results.Extra[key] so tests can verify execution.
/// </summary>
internal class WriterAgent(string agentId, string key, string value)
    : AgentBase<string>(NullLogger<AgentBase<string>>.Instance)
{
    public override string    AgentId   => agentId;
    public override string    AgentName => $"Writer-{agentId}";
    public override AgentRole Role      => AgentRole.Conversation;

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        ctx.Results.Extra[key] = value;
        return Task.FromResult<string?>(value);
    }
}

/// <summary>
/// Throws for the first <paramref name="failCount"/> invocations, then succeeds.
/// </summary>
internal class FlakyAgent(string agentId, int failCount, string successText)
    : AgentBase<string>(NullLogger<AgentBase<string>>.Instance)
{
    private int _calls;
    public int CallCount => Volatile.Read(ref _calls);

    public override string    AgentId   => agentId;
    public override string    AgentName => $"Flaky-{agentId}";
    public override AgentRole Role      => AgentRole.Conversation;

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _calls) <= failCount)
            throw new InvalidOperationException($"[{agentId}] planned failure #{_calls}");
        return Task.FromResult<string?>(successText);
    }
}

/// <summary>
/// Always returns null (empty response). Used to test AbortIfEmpty guards.
/// </summary>
internal class SilentAgent(string agentId)
    : AgentBase<string>(NullLogger<AgentBase<string>>.Instance)
{
    public override string    AgentId   => agentId;
    public override string    AgentName => $"Silent-{agentId}";
    public override AgentRole Role      => AgentRole.Conversation;

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}

/// <summary>
/// Appends its agentId to a shared log on each execution.
/// Useful for verifying execution order in orchestration tests.
/// </summary>
internal class LoggingAgent(string agentId, string agentName, IList<string> log)
    : AgentBase<string>(NullLogger<AgentBase<string>>.Instance)
{
    public override string    AgentId   => agentId;
    public override string    AgentName => agentName;
    public override AgentRole Role      => AgentRole.Conversation;

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        lock (log) log.Add(agentId);
        return Task.FromResult<string?>(agentName);
    }
}

// ── [AgentCapability]-decorated agents for DI assembly-scan tests ─────────────

[AgentCapability(Role = "it-conversation", Lifetime = AgentLifetime.Singleton)]
internal sealed class ItConversationAgent()
    : AgentBase<string>(NullLogger<AgentBase<string>>.Instance)
{
    public override string    AgentId   => "it-conversation";
    public override string    AgentName => "ItConversation";
    public override AgentRole Role      => AgentRole.Conversation;

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct) =>
        Task.FromResult<string?>("integration response");
}

[AgentCapability(Role = "it-analysis", MemoryAccess = [MemoryKind.Episodic], Lifetime = AgentLifetime.Scoped)]
internal sealed class ItAnalysisAgent()
    : AgentBase<string>(NullLogger<AgentBase<string>>.Instance)
{
    public override string    AgentId   => "it-analysis";
    public override string    AgentName => "ItAnalysis";
    public override AgentRole Role      => AgentRole.Analysis;

    protected override Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct) =>
        Task.FromResult<string?>("analysis done");
}
