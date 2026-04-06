using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Memory;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Memory;

// ── All handlers are invoked in order ────────────────────────────────────────

public class ConsolidationChain_ConsolidateAsync_InvokesAllHandlers : IAsyncLifetime
{
    private FakeConsolidationHandler _h1 = default!;
    private FakeConsolidationHandler _h2 = default!;
    private FakeConsolidationHandler _h3 = default!;

    public async Task InitializeAsync()
    {
        _h1 = FakeConsolidationHandler.Ok("handler-1");
        _h2 = FakeConsolidationHandler.Ok("handler-2");
        _h3 = FakeConsolidationHandler.Ok("handler-3");

        var chain = new ConsolidationChain(
            [_h1, _h2, _h3],
            NullLogger<ConsolidationChain>.Instance);

        await chain.ConsolidateAsync(
            TestBuilders.Exchange(),
            TestBuilders.Context(),
            CancellationToken.None);
    }

    [Fact] public void Handler1_WasCalled() => _h1.CallCount.Should().Be(1);
    [Fact] public void Handler2_WasCalled() => _h2.CallCount.Should().Be(1);
    [Fact] public void Handler3_WasCalled() => _h3.CallCount.Should().Be(1);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── A failing handler does not stop the chain ────────────────────────────────

public class ConsolidationChain_ConsolidateAsync_WhenHandlerThrows_ChainContinues : IAsyncLifetime
{
    private FakeConsolidationHandler _beforeFail = default!;
    private FakeConsolidationHandler _afterFail  = default!;

    public async Task InitializeAsync()
    {
        _beforeFail = FakeConsolidationHandler.Ok("before");
        var failing = FakeConsolidationHandler.Failing("failing");
        _afterFail  = FakeConsolidationHandler.Ok("after");

        var chain = new ConsolidationChain(
            [_beforeFail, failing, _afterFail],
            NullLogger<ConsolidationChain>.Instance);

        // Should not throw
        await chain.ConsolidateAsync(
            TestBuilders.Exchange(),
            TestBuilders.Context(),
            CancellationToken.None);
    }

    [Fact] public void BeforeFail_WasCalled()  => _beforeFail.CallCount.Should().Be(1);
    [Fact] public void AfterFail_WasCalled()   => _afterFail.CallCount.Should().Be(1);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Empty chain ───────────────────────────────────────────────────────────────

public class ConsolidationChain_EmptyChain_NoThrow
{
    [Fact]
    public async Task EmptyHandlerList_CompletesWithoutException()
    {
        var chain = new ConsolidationChain(
            [],
            NullLogger<ConsolidationChain>.Instance);

        Func<Task> act = () => chain.ConsolidateAsync(
            TestBuilders.Exchange(),
            TestBuilders.Context(),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}

// ── ProcessAsync wraps ConsolidateAsync ──────────────────────────────────────

public class ConsolidationChain_ProcessAsync_ReturnsEmptyResponse : IAsyncLifetime
{
    private MiyuAgents.Core.AgentResponse _response = default!;

    public async Task InitializeAsync()
    {
        var chain = new ConsolidationChain(
            [FakeConsolidationHandler.Ok("h")],
            NullLogger<ConsolidationChain>.Instance);

        _response = await chain.ProcessAsync(TestBuilders.Context(), CancellationToken.None);
    }

    [Fact] public void Response_IsEmpty()         => _response.IsEmpty.Should().BeTrue();
    [Fact] public void Response_AgentId_IsChain() => _response.AgentId.Should().Be("consolidation-chain");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── All handlers fail gracefully ─────────────────────────────────────────────

public class ConsolidationChain_AllHandlersFail_DoesNotThrow
{
    [Fact]
    public async Task AllFailing_CompletesWithoutException()
    {
        var chain = new ConsolidationChain(
            [
                FakeConsolidationHandler.Failing("f1"),
                FakeConsolidationHandler.Failing("f2"),
            ],
            NullLogger<ConsolidationChain>.Instance);

        Func<Task> act = () => chain.ConsolidateAsync(
            TestBuilders.Exchange(),
            TestBuilders.Context(),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
