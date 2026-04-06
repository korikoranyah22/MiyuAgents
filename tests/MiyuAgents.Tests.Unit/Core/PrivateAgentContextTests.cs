using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Pipeline;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Core;

public class PrivateAgentContext_Create_IsolatesAccumulator
{
    [Fact]
    public void PrivateCtx_Results_IsNewInstance()
    {
        var parent     = TestBuilders.Context();
        var (priv, _)  = PrivateAgentContext.Create(parent);

        priv.Results.Should().NotBeSameAs(parent.Results);
    }

    [Fact]
    public void PrivateCtx_SharedImmutableFields_ArePreserved()
    {
        var parent    = AgentContext.For("conv-99", "msg-99", "test msg", "gpt-4");
        var (priv, _) = PrivateAgentContext.Create(parent);

        priv.ConversationId.Should().Be("conv-99");
        priv.MessageId.Should().Be("msg-99");
        priv.UserMessage.Should().Be("test msg");
        priv.Model.Should().Be("gpt-4");
    }

    [Fact]
    public void WritingToPrivateResults_DoesNotAffectParent()
    {
        var parent    = TestBuilders.Context();
        var (priv, _) = PrivateAgentContext.Create(parent);

        priv.Results.LlmResponse = "private response";

        parent.Results.LlmResponse.Should().BeNull();
    }
}

public class PrivateAgentContext_Create_ReturnsPrivateBus
{
    [Fact]
    public void PrivateBus_IsNotNull()
    {
        var (_, bus) = PrivateAgentContext.Create(TestBuilders.Context());
        bus.Should().NotBeNull();
    }

    [Fact]
    public void PrivateBus_IsStoredInMetadata()
    {
        var parent    = TestBuilders.Context();
        var (priv, _) = PrivateAgentContext.Create(parent);

        var retrieved = PrivateAgentContext.GetPrivateBus(priv);
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public void GetPrivateBus_OnContextWithoutBus_ReturnsNull()
    {
        var ctx = TestBuilders.Context();
        PrivateAgentContext.GetPrivateBus(ctx).Should().BeNull();
    }
}

public class PrivateAgentContext_PrivateBus_CapturesEvents : IAsyncLifetime
{
    private InMemoryAgentEventBus _privateBus = default!;
    private record TestEvent(string Data);

    public async Task InitializeAsync()
    {
        var (priv, bus) = PrivateAgentContext.Create(TestBuilders.Context());
        _privateBus = (InMemoryAgentEventBus)bus;
        await bus.PublishAsync(new TestEvent("hello"));
        await bus.PublishAsync(new TestEvent("world"));
    }

    [Fact] public void Events_AreCaptured_NotLost() => _privateBus.CapturedEvents.Should().HaveCount(2);

    public Task DisposeAsync() => Task.CompletedTask;
}

