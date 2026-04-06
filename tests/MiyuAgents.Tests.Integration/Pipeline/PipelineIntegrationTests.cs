using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Pipeline;
using MiyuAgents.Tests.Integration.Helpers;
using Xunit;

namespace MiyuAgents.Tests.Integration.Pipeline;

// ── Single agent stage → TurnResult ──────────────────────────────────────────

public class PipelineRunner_SingleAgentStage_PopulatesLlmResponse : IAsyncLifetime
{
    private TurnResult _result = default!;

    public async Task InitializeAsync()
    {
        var agent  = new EchoAgent("echo-1", "hello from pipeline");
        var stage  = new AgentPipelineStage("echo-stage", priority: 10, agent);
        var runner = new PipelineRunner([stage], NullLogger<PipelineRunner>.Instance);

        var ctx      = AgentContext.For("c-1", "m-1", "test message");
        var pipeline = new PipelineContext
        {
            EventBus    = NullAgentEventBus.Instance,
            Broadcaster = NullBroadcaster.Instance
        };

        _result = await runner.RunAsync(ctx, pipeline, CancellationToken.None);
    }

    [Fact] public void Response_HasExpectedText()   => _result.Response.Should().Be("hello from pipeline");
    [Fact] public void WasAborted_IsFalse()         => _result.WasAborted.Should().BeFalse();
    [Fact] public void StageHistory_HasOneEntry()   => _result.StageHistory.Should().HaveCount(1);
    [Fact] public void TotalLatency_IsPositive()    => _result.TotalLatency.Should().BeGreaterThan(TimeSpan.Zero);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── RetryStage retries a flaky stage ─────────────────────────────────────────

public class PipelineRunner_RetryStage_SucceedsAfterRetries : IAsyncLifetime
{
    private TurnResult _result = default!;
    private FlakyStage _flaky = default!;

    public async Task InitializeAsync()
    {
        _flaky = new FlakyStage("flaky", priority: 10, failCount: 2);
        var retry  = new RetryStage(_flaky, maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(1));
        var runner = new PipelineRunner([retry], NullLogger<PipelineRunner>.Instance);

        var ctx      = AgentContext.For("c", "m", "retry test");
        var pipeline = new PipelineContext
        {
            EventBus    = NullAgentEventBus.Instance,
            Broadcaster = NullBroadcaster.Instance
        };

        _result = await runner.RunAsync(ctx, pipeline, CancellationToken.None);
    }

    [Fact] public void Result_IsNotAborted()    => _result.WasAborted.Should().BeFalse();
    [Fact] public void Stage_WasCalledThrice()  => _flaky.CallCount.Should().Be(3);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── ParallelAgentStage — all agents run ───────────────────────────────────────

public class PipelineRunner_ParallelAgentStage_AllAgentsComplete : IAsyncLifetime
{
    private TurnResult _result = default!;
    private readonly List<string> _executed = [];

    public async Task InitializeAsync()
    {
        var agents = new IAgent[]
        {
            new WriterAgent("w-1", "key-1", "val-1"),
            new WriterAgent("w-2", "key-2", "val-2"),
            new WriterAgent("w-3", "key-3", "val-3"),
        };

        var stage  = new ParallelAgentStage("parallel-test", priority: 10, agents[0], agents[1], agents[2]);
        var runner = new PipelineRunner([stage], NullLogger<PipelineRunner>.Instance);

        var ctx      = AgentContext.For("c", "m", "parallel");
        var pipeline = new PipelineContext
        {
            EventBus    = NullAgentEventBus.Instance,
            Broadcaster = NullBroadcaster.Instance
        };

        _result = await runner.RunAsync(ctx, pipeline, CancellationToken.None);
    }

    [Fact] public void AllThreeKeys_AreInResults()
        => _result.Context.Results.Extra.Keys.Should().Contain(["key-1", "key-2", "key-3"]);

    [Fact] public void WasAborted_IsFalse() => _result.WasAborted.Should().BeFalse();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── ParallelAgentStage — one agent error propagates ──────────────────────────

public class PipelineRunner_ParallelAgentStage_OneAgentFails_StageThrows
{
    [Fact]
    public async Task AggregateException_IsThrownFromRunner()
    {
        var good = new EchoAgent("good", "ok");
        var bad  = new FlakyAgent("bad", failCount: 999, successText: "unreachable");

        var stage  = new ParallelAgentStage("parallel-fail", 10, good, bad);
        var runner = new PipelineRunner([stage], NullLogger<PipelineRunner>.Instance);

        var ctx      = AgentContext.For("c", "m", "fail test");
        var pipeline = new PipelineContext
        {
            EventBus    = NullAgentEventBus.Instance,
            Broadcaster = NullBroadcaster.Instance
        };

        Func<Task> act = () => runner.RunAsync(ctx, pipeline, CancellationToken.None);
        await act.Should().ThrowAsync<AggregateException>()
            .WithMessage("*One or more agents failed*");
    }
}

// ── AbortIfEmptyStage halts the pipeline ─────────────────────────────────────

public class PipelineRunner_AbortIfEmptyStage_WhenLlmResponseNull_AbortsEarly : IAsyncLifetime
{
    private TurnResult _result = default!;
    private TrackingStage _downstream = default!;

    public async Task InitializeAsync()
    {
        // Stage 1 (priority 10): silent agent — never writes LlmResponse
        var silentStage = new AgentPipelineStage("silent", priority: 10, new SilentAgent("s-1"));

        // Stage 2 (priority 20): abort if LlmResponse is null
        var guard = new AbortIfEmptyStage(
            "guard", priority: 20,
            isEmpty:     r => string.IsNullOrEmpty(r.LlmResponse),
            abortReason: "no LLM response");

        // Stage 3 (priority 30): should NOT execute because guard aborts
        _downstream = new TrackingStage("downstream", priority: 30, []);

        var runner = new PipelineRunner([silentStage, guard, _downstream], NullLogger<PipelineRunner>.Instance);

        var ctx      = AgentContext.For("c", "m", "abort test");
        var pipeline = new PipelineContext
        {
            EventBus    = NullAgentEventBus.Instance,
            Broadcaster = NullBroadcaster.Instance
        };

        _result = await runner.RunAsync(ctx, pipeline, CancellationToken.None);
    }

    [Fact] public void WasAborted_IsTrue()          => _result.WasAborted.Should().BeTrue();
    [Fact] public void Downstream_NeverExecuted()   => _downstream.CallCount.Should().Be(0);
    [Fact] public void StageHistory_HasTwoEntries() => _result.StageHistory.Should().HaveCount(2);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Multiple stages execute in priority order ─────────────────────────────────

public class PipelineRunner_MultipleStages_ExecuteInPriorityOrder : IAsyncLifetime
{
    private TurnResult _result = default!;
    private readonly List<string> _order = [];

    public async Task InitializeAsync()
    {
        var s1 = new TrackingStage("first",  priority: 10, _order);
        var s2 = new TrackingStage("second", priority: 30, _order);
        var s3 = new TrackingStage("third",  priority: 20, _order);  // registered last but priority 20

        var runner = new PipelineRunner([s1, s2, s3], NullLogger<PipelineRunner>.Instance);

        var ctx      = AgentContext.For("c", "m", "order test");
        var pipeline = new PipelineContext
        {
            EventBus    = NullAgentEventBus.Instance,
            Broadcaster = NullBroadcaster.Instance
        };

        _result = await runner.RunAsync(ctx, pipeline, CancellationToken.None);
    }

    [Fact] public void Stages_ExecuteInPriorityOrder() => _order.Should().Equal(["first", "third", "second"]);
    [Fact] public void AllThreeStages_Ran()            => _order.Should().HaveCount(3);

    public Task DisposeAsync() => Task.CompletedTask;
}
