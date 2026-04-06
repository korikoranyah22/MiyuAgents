using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Pipeline;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Pipeline;

// ── All agents execute ────────────────────────────────────────────────────────

public class ParallelAgentStage_ExecuteAsync_RunsAllAgents : IAsyncLifetime
{
    private readonly List<string> _executed = [];
    private PipelineStageResult   _result   = default!;

    public async Task InitializeAsync()
    {
        var agent1 = new FakeAgent("a-1", executor: (_, _) => { lock (_executed) _executed.Add("a-1"); return Task.FromResult<string?>("a1"); });
        var agent2 = new FakeAgent("a-2", executor: (_, _) => { lock (_executed) _executed.Add("a-2"); return Task.FromResult<string?>("a2"); });
        var agent3 = new FakeAgent("a-3", executor: (_, _) => { lock (_executed) _executed.Add("a-3"); return Task.FromResult<string?>("a3"); });

        var stage = new ParallelAgentStage("parallel-test", priority: 10, agent1, agent2, agent3);
        _result   = await stage.ExecuteAsync(TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);
    }

    [Fact] public void Result_ShouldContinue_IsTrue()  => _result.ShouldContinue.Should().BeTrue();
    [Fact] public void AllAgents_WereExecuted()         => _executed.Should().HaveCount(3);
    [Fact] public void AllAgentIds_ArePresent()         => _executed.Should().Contain(["a-1", "a-2", "a-3"]);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── One agent fails, others still complete ────────────────────────────────────

public class ParallelAgentStage_OneAgentThrows_ThrowsAggregate : IAsyncLifetime
{
    private Exception? _thrownException;

    public async Task InitializeAsync()
    {
        var good = FakeAgent.Returns("ok",   "good-agent");
        var bad  = FakeAgent.Throws(new InvalidOperationException("agent boom"), "bad-agent");

        var stage = new ParallelAgentStage("parallel-fail", 10, good, bad);

        try
        {
            await stage.ExecuteAsync(TestBuilders.Context(), TestBuilders.Pipeline(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _thrownException = ex;
        }
    }

    // Task.WhenAll wraps individual exceptions in AggregateException
    [Fact] public void Exception_IsThrownFromWhenAll()
        => _thrownException.Should().NotBeNull();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Publishes aggregate event ─────────────────────────────────────────────────

public class ParallelAgentStage_PublishesAllAgentsCompleted : IAsyncLifetime
{
    private AllAgentsInStageCompleted? _event;

    public async Task InitializeAsync()
    {
        var capturingBus = new CapturingBus<AllAgentsInStageCompleted>(e => _event = e);
        var pipeline     = new PipelineContext
        {
            EventBus    = capturingBus,
            Broadcaster = NullBroadcaster.Instance
        };

        var agent1 = FakeAgent.Returns("r1", "ag-1");
        var agent2 = FakeAgent.Returns("r2", "ag-2");
        var stage  = new ParallelAgentStage("test-parallel", 10, agent1, agent2);

        await stage.ExecuteAsync(TestBuilders.Context(), pipeline, CancellationToken.None);
    }

    [Fact] public void Event_WasPublished()           => _event.Should().NotBeNull();
    [Fact] public void Event_ContainsBothAgentIds()   => _event!.AgentIds.Should().Contain(["ag-1", "ag-2"]);
    [Fact] public void Event_StageName_IsSet()        => _event!.StageName.Should().Be("test-parallel");

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class CapturingBus<TTarget>(Action<TTarget> onCapture) : IAgentEventBus
    {
        public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class
        {
            if (evt is TTarget target) onCapture(target);
            return Task.CompletedTask;
        }
    }
}

// ── Stage metadata ────────────────────────────────────────────────────────────

public class ParallelAgentStage_NameAndPriority
{
    [Fact]
    public void StageName_And_Priority_AreSetFromConstructor()
    {
        var stage = new ParallelAgentStage("mem-retrieval", priority: 200);
        stage.StageName.Should().Be("mem-retrieval");
        stage.Priority.Should().Be(200);
    }
}
