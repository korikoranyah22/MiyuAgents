using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Core.Events;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Core;

// ── Successful execution ─────────────────────────────────────────────────────

public class AgentBase_ProcessAsync_WhenSucceeds : IAsyncLifetime
{
    private FakeAgent     _agent    = default!;
    private AgentContext  _ctx      = default!;
    private AgentResponse _response = default!;

    public async Task InitializeAsync()
    {
        _agent    = FakeAgent.Returns("the answer");
        _ctx      = TestBuilders.Context();
        _response = await _agent.ProcessAsync(_ctx, CancellationToken.None);
    }

    [Fact] public void Response_Status_IsOk()        => _response.Status.Should().Be(AgentStatus.Ok);
    [Fact] public void Response_Data_IsValue()        => _response.As<string>().Should().Be("the answer");
    [Fact] public void Response_IsEmpty_IsFalse()     => _response.IsEmpty.Should().BeFalse();
    [Fact] public void Response_AgentId_Matches()     => _response.AgentId.Should().Be(_agent.AgentId);
    [Fact] public void Response_Latency_IsPositive()  => _response.Latency.Should().BeGreaterThan(TimeSpan.Zero);
    [Fact] public void Response_ErrorMessage_IsNull() => _response.ErrorMessage.Should().BeNull();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Null return (empty response) ────────────────────────────────────────────

public class AgentBase_ProcessAsync_WhenReturnsNull : IAsyncLifetime
{
    private AgentResponse _response = default!;

    public async Task InitializeAsync()
    {
        var agent = FakeAgent.Returns(null);
        _response = await agent.ProcessAsync(TestBuilders.Context(), CancellationToken.None);
    }

    [Fact] public void Response_IsEmpty_IsTrue() => _response.IsEmpty.Should().BeTrue();
    [Fact] public void Response_Status_IsOk()    => _response.Status.Should().Be(AgentStatus.Ok);
    [Fact] public void Response_Data_IsNull()    => _response.Data.Should().BeNull();

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Exception handling ───────────────────────────────────────────────────────

public class AgentBase_ProcessAsync_WhenThrows : IAsyncLifetime
{
    private AgentResponse _response = default!;
    private readonly InvalidOperationException _ex = new("boom");

    public async Task InitializeAsync()
    {
        var agent = FakeAgent.Throws(_ex);
        _response = await agent.ProcessAsync(TestBuilders.Context(), CancellationToken.None);
    }

    [Fact] public void Response_Status_IsError()     => _response.Status.Should().Be(AgentStatus.Error);
    [Fact] public void Response_ErrorMessage_IsSet() => _response.ErrorMessage.Should().Be("boom");
    [Fact] public void Response_Data_IsNull()        => _response.Data.Should().BeNull();
    [Fact] public void Response_Latency_IsPositive() => _response.Latency.Should().BeGreaterThan(TimeSpan.Zero);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Cancellation propagates ──────────────────────────────────────────────────

public class AgentBase_ProcessAsync_WhenCancelled
{
    [Fact]
    public async Task CancellationToken_Propagates_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var agent = new FakeAgent(executor: async (_, ct) =>
        {
            await Task.Delay(1000, ct); // will throw
            return "unreachable";
        });

        Func<Task> act = () => agent.ProcessAsync(TestBuilders.Context(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

// ── Events are fired ─────────────────────────────────────────────────────────

public class AgentBase_ProcessAsync_FiresEvents : IAsyncLifetime
{
    private FakeAgent _agent = default!;
    private AgentContext _ctx = default!;

    private MessageReceivedEventArgs?       _messageReceived;
    private AgentResponseProducedEventArgs? _responseProduced;

    public async Task InitializeAsync()
    {
        _agent = FakeAgent.Returns("hi");
        _ctx   = TestBuilders.Context(conversationId: "c1", messageId: "m1", userMessage: "test");

        _agent.OnMessageReceived  += (_, e) => { _messageReceived  = e; return Task.CompletedTask; };
        _agent.OnResponseProduced += (_, e) => { _responseProduced = e; return Task.CompletedTask; };

        await _agent.ProcessAsync(_ctx, CancellationToken.None);
    }

    [Fact] public void OnMessageReceived_IsFired()             => _messageReceived.Should().NotBeNull();
    [Fact] public void OnMessageReceived_ConversationId()      => _messageReceived!.ConversationId.Should().Be("c1");
    [Fact] public void OnMessageReceived_MessageId()           => _messageReceived!.MessageId.Should().Be("m1");
    [Fact] public void OnMessageReceived_Content()             => _messageReceived!.Content.Should().Be("test");
    [Fact] public void OnResponseProduced_IsFired()            => _responseProduced.Should().NotBeNull();
    [Fact] public void OnResponseProduced_Response_Status_Ok() => _responseProduced!.Response.Status.Should().Be(AgentStatus.Ok);

    public Task DisposeAsync() => Task.CompletedTask;
}

public class AgentBase_ProcessAsync_OnError_FiresWhenThrows : IAsyncLifetime
{
    private AgentErrorEventArgs? _errorArgs;

    public async Task InitializeAsync()
    {
        var agent = FakeAgent.Throws(new InvalidOperationException("oops"));
        agent.OnError += (_, e) => { _errorArgs = e; return Task.CompletedTask; };
        await agent.ProcessAsync(TestBuilders.Context(), CancellationToken.None);
    }

    [Fact] public void OnError_IsFired()                  => _errorArgs.Should().NotBeNull();
    [Fact] public void OnError_Exception_MessageMatches() => _errorArgs!.Exception.Message.Should().Be("oops");

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Multiple event subscribers ────────────────────────────────────────────────

public class AgentBase_ProcessAsync_MultipleSubscribers : IAsyncLifetime
{
    private readonly List<string> _log = [];

    public async Task InitializeAsync()
    {
        var agent = FakeAgent.Returns("x");

        agent.OnMessageReceived += (_, _) => { _log.Add("sub1"); return Task.CompletedTask; };
        agent.OnMessageReceived += (_, _) => { _log.Add("sub2"); return Task.CompletedTask; };
        agent.OnMessageReceived += (_, _) => { _log.Add("sub3"); return Task.CompletedTask; };

        await agent.ProcessAsync(TestBuilders.Context(), CancellationToken.None);
    }

    [Fact] public void AllThreeSubscribers_AreInvoked() => _log.Should().Contain(["sub1", "sub2", "sub3"]);
    [Fact] public void SubscriberCount_IsThree()         => _log.Where(l => l.StartsWith("sub")).Should().HaveCount(3);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Theory: different return values ──────────────────────────────────────────

public class AgentBase_ProcessAsync_VariousReturnValues
{
    public static TheoryData<string?, AgentStatus, bool> Cases => new()
    {
        { "non-empty",  AgentStatus.Ok, false },
        { "",           AgentStatus.Ok, false },  // empty string is not null, so not "empty"
        { null,         AgentStatus.Ok, true  },
    };

    [Theory, MemberData(nameof(Cases))]
    public async Task Status_And_IsEmpty_MatchExpected(string? value, AgentStatus expectedStatus, bool expectedEmpty)
    {
        var agent    = FakeAgent.Returns(value);
        var response = await agent.ProcessAsync(TestBuilders.Context(), CancellationToken.None);

        response.Status.Should().Be(expectedStatus);
        response.IsEmpty.Should().Be(expectedEmpty);
    }
}
