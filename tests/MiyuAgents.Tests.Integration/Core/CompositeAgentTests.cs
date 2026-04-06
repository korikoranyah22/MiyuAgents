using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Pipeline;
using MiyuAgents.Tests.Integration.Helpers;
using Xunit;

namespace MiyuAgents.Tests.Integration.Core;

// ── Local composite agent definitions ────────────────────────────────────────

/// <summary>
/// Orchestrates one sub-agent in a private context and builds a response
/// from whatever the sub-agent wrote to privateCtx.Results.Extra.
/// </summary>
file sealed class SingleSubAgentComposite(IAgent subAgent, bool debugMode = false)
    : CompositeAgentBase<string>(NullLogger<AgentBase<string>>.Instance, NullLoggerFactory.Instance)
{
    public override string    AgentId   => "single-composite";
    public override string    AgentName => "SingleComposite";
    public override AgentRole Role      => AgentRole.Conversation;

    protected override bool                  DebugMode => debugMode;
    public override IReadOnlyList<IAgent> SubAgents => [subAgent];

    protected override async Task OrchestrateSubAgentsAsync(AgentContext privateCtx, CancellationToken ct)
    {
        await subAgent.ProcessAsync(privateCtx, ct);

        // Also publish a marker event so DebugMode replay has something to verify
        var bus = PrivateAgentContext.GetPrivateBus(privateCtx);
        if (bus is not null)
            await bus.PublishAsync(new SubAgentRanMarker(subAgent.AgentId), ct);
    }

    protected override Task<string?> BuildResponseAsync(
        AgentContext privateCtx, AgentContext parentCtx, CancellationToken ct)
    {
        var keys   = privateCtx.Results.Extra.Keys.OrderBy(k => k);
        var result = $"built:{string.Join(",", keys)}";
        return Task.FromResult<string?>(result);
    }
}

file record SubAgentRanMarker(string AgentId);

// ── Sub-agent writes stay in private context ──────────────────────────────────

public class CompositeAgent_SubAgentWrites_DoNotReachParentContext : IAsyncLifetime
{
    private AgentResponse _response   = default!;
    private AgentContext  _parentCtx  = default!;

    public async Task InitializeAsync()
    {
        var subAgent  = new WriterAgent("sub-1", "sub-key", "sub-value");
        var composite = new SingleSubAgentComposite(subAgent);

        _parentCtx = AgentContext.For("conv-c", "msg-m", "orchestrate");
        _response  = await composite.ProcessAsync(_parentCtx, CancellationToken.None);
    }

    [Fact] public void Response_Status_IsOk()
        => _response.Status.Should().Be(AgentStatus.Ok);

    [Fact] public void Response_Contains_SubAgentKey()
        => _response.As<string>().Should().Be("built:sub-key");

    [Fact] public void ParentCtx_Extra_IsEmpty()
        => _parentCtx.Results.Extra.Should().BeEmpty();

    [Fact] public void ParentCtx_LlmResponse_IsNull()
        => _parentCtx.Results.LlmResponse.Should().BeNull();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── DebugMode replays private bus events to the caller bus ────────────────────

public class CompositeAgent_DebugMode_ReplaysPrivateBusEventsToCallerBus : IAsyncLifetime
{
    private InMemoryAgentEventBus _callerBus = default!;

    public async Task InitializeAsync()
    {
        _callerBus = new InMemoryAgentEventBus();

        var parentCtx = AgentContext.For("c", "m", "debug test");
        parentCtx.Metadata["__pipeline_event_bus"] = _callerBus;

        var subAgent  = new EchoAgent("sub-echo", "hi from sub");
        var composite = new SingleSubAgentComposite(subAgent, debugMode: true);

        await composite.ProcessAsync(parentCtx, CancellationToken.None);
    }

    [Fact] public void CallerBus_ReceivedEventsFromPrivateBus()
        => _callerBus.CapturedEvents.Should().NotBeEmpty();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Two sub-agents write separate keys ────────────────────────────────────────

file sealed class DualSubAgentComposite(IAgent agentA, IAgent agentB)
    : CompositeAgentBase<string>(NullLogger<AgentBase<string>>.Instance, NullLoggerFactory.Instance)
{
    public override string    AgentId   => "dual-composite";
    public override string    AgentName => "DualComposite";
    public override AgentRole Role      => AgentRole.Conversation;

    protected override bool                  DebugMode => false;
    public override IReadOnlyList<IAgent> SubAgents => [agentA, agentB];

    protected override async Task OrchestrateSubAgentsAsync(AgentContext privateCtx, CancellationToken ct)
    {
        await agentA.ProcessAsync(privateCtx, ct);
        await agentB.ProcessAsync(privateCtx, ct);
    }

    protected override Task<string?> BuildResponseAsync(
        AgentContext privateCtx, AgentContext parentCtx, CancellationToken ct)
    {
        var a = privateCtx.Results.Extra.TryGetValue("key-a", out var av) ? av?.ToString() : null;
        var b = privateCtx.Results.Extra.TryGetValue("key-b", out var bv) ? bv?.ToString() : null;
        return Task.FromResult<string?>($"{a}|{b}");
    }
}

public class CompositeAgent_TwoSubAgents_BothWriteToPrivateAccumulator : IAsyncLifetime
{
    private AgentResponse _response  = default!;
    private AgentContext  _parentCtx = default!;

    public async Task InitializeAsync()
    {
        var a = new WriterAgent("a", "key-a", "value-a");
        var b = new WriterAgent("b", "key-b", "value-b");

        var composite = new DualSubAgentComposite(a, b);
        _parentCtx    = AgentContext.For("c", "m", "dual");
        _response     = await composite.ProcessAsync(_parentCtx, CancellationToken.None);
    }

    [Fact] public void Response_CombinesBothValues()
        => _response.As<string>().Should().Be("value-a|value-b");

    [Fact] public void ParentCtx_Extra_IsNotPolluted()
        => _parentCtx.Results.Extra.Should().BeEmpty();

    public Task DisposeAsync() => Task.CompletedTask;
}
