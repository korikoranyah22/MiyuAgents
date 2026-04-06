using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Llm;
using Xunit;

namespace MiyuAgents.Tests.Unit.Core;

public class AgentContext_For_CreatesValidContext
{
    private readonly AgentContext _ctx = AgentContext.For("conv-1", "msg-1", "hello", "gpt-4o");

    [Fact] public void ConversationId_IsSet()  => _ctx.ConversationId.Should().Be("conv-1");
    [Fact] public void MessageId_IsSet()       => _ctx.MessageId.Should().Be("msg-1");
    [Fact] public void UserMessage_IsSet()     => _ctx.UserMessage.Should().Be("hello");
    [Fact] public void Model_IsSet()           => _ctx.Model.Should().Be("gpt-4o");
    [Fact] public void ProfileId_IsEmpty()     => _ctx.ProfileId.Should().BeEmpty();
    [Fact] public void CharacterId_IsEmpty()   => _ctx.CharacterId.Should().BeEmpty();
    [Fact] public void History_IsEmpty()       => _ctx.History.Should().BeEmpty();
    [Fact] public void IsFirstTurn_IsTrue()    => _ctx.IsFirstTurn.Should().BeTrue();
    [Fact] public void Results_IsNotNull()     => _ctx.Results.Should().NotBeNull();
    [Fact] public void Metadata_IsNotNull()    => _ctx.Metadata.Should().NotBeNull();
}

public class AgentContext_For_DefaultModel
{
    private readonly AgentContext _ctx = AgentContext.For("c", "m", "hi");

    [Fact] public void Model_DefaultsToDefault() => _ctx.Model.Should().Be("default");
}

public class AgentContext_Metadata_IsMutable
{
    private readonly AgentContext _ctx = AgentContext.For("c", "m", "hi");

    [Fact]
    public void Metadata_AcceptsArbitraryKeys()
    {
        _ctx.Metadata["key"] = "value";
        _ctx.Metadata["key"].Should().Be("value");
    }

    [Fact]
    public void Metadata_DoesNotAffectOtherFields()
    {
        _ctx.Metadata["x"] = 42;
        _ctx.ConversationId.Should().Be("c");
        _ctx.UserMessage.Should().Be("hi");
    }
}

public class AgentContext_Results_IsSharedAccumulator
{
    private readonly AgentContext _ctx = AgentContext.For("c", "m", "hi");

    [Fact]
    public void Results_SameInstanceAcrossReferences()
    {
        var ref1 = _ctx.Results;
        var ref2 = _ctx.Results;
        ref1.Should().BeSameAs(ref2);
    }

    [Fact]
    public void Results_LlmResponse_CanBeWritten()
    {
        _ctx.Results.LlmResponse = "hello world";
        _ctx.Results.LlmResponse.Should().Be("hello world");
    }

    [Fact]
    public void Results_EpisodicMemories_StartsEmpty()
        => _ctx.Results.EpisodicMemories.Should().BeEmpty();

    [Fact]
    public void Results_Extra_AcceptsArbitraryKeys()
    {
        _ctx.Results.Extra["custom"] = new object();
        _ctx.Results.Extra.Should().ContainKey("custom");
    }
}

public class AgentContext_WithExpression_PreservesImmutability
{
    private readonly AgentContext _original = AgentContext.For("conv-1", "msg-1", "hello");

    [Fact]
    public void With_CreatesNewInstance()
    {
        var copy = _original with { UserMessage = "world" };
        copy.Should().NotBeSameAs(_original);
    }

    [Fact]
    public void With_OriginalNotMutated()
    {
        _ = _original with { UserMessage = "world" };
        _original.UserMessage.Should().Be("hello");
    }

    [Fact]
    public void With_OnlyChangedFieldDiffers()
    {
        var copy = _original with { Model = "claude-3" };
        copy.ConversationId.Should().Be(_original.ConversationId);
        copy.MessageId.Should().Be(_original.MessageId);
        copy.Model.Should().Be("claude-3");
    }
}
