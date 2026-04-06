using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Pipeline;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Core;

public class AgentResponse_From_WithData
{
    private readonly AgentResponse _response;

    public AgentResponse_From_WithData()
    {
        var agent = new FakeAgent("agent-1", "MyAgent", AgentRole.Conversation);
        _response = AgentResponse.From(agent, "result text", TimeSpan.FromMilliseconds(42));
    }

    [Fact] public void AgentId_Matches()        => _response.AgentId.Should().Be("agent-1");
    [Fact] public void AgentName_Matches()      => _response.AgentName.Should().Be("MyAgent");
    [Fact] public void Role_Matches()           => _response.Role.Should().Be(AgentRole.Conversation);
    [Fact] public void Data_Matches()           => _response.Data.Should().Be("result text");
    [Fact] public void IsEmpty_IsFalse()        => _response.IsEmpty.Should().BeFalse();
    [Fact] public void Status_IsOk()            => _response.Status.Should().Be(AgentStatus.Ok);
    [Fact] public void ErrorMessage_IsNull()    => _response.ErrorMessage.Should().BeNull();
    [Fact] public void Latency_IsSet()          => _response.Latency.Should().Be(TimeSpan.FromMilliseconds(42));
}

public class AgentResponse_From_WithNullData
{
    private readonly AgentResponse _response;

    public AgentResponse_From_WithNullData()
    {
        var agent = new FakeAgent("agent-1");
        _response = AgentResponse.From(agent, null, TimeSpan.Zero);
    }

    [Fact] public void IsEmpty_IsTrue()    => _response.IsEmpty.Should().BeTrue();
    [Fact] public void Data_IsNull()       => _response.Data.Should().BeNull();
    [Fact] public void Status_IsOk()       => _response.Status.Should().Be(AgentStatus.Ok);
}

public class AgentResponse_Error_CapturesException
{
    private readonly AgentResponse _response;

    public AgentResponse_Error_CapturesException()
    {
        var agent = new FakeAgent("agent-err");
        var ex    = new InvalidOperationException("something went wrong");
        _response = AgentResponse.Error(agent, ex, TimeSpan.FromMilliseconds(5));
    }

    [Fact] public void Status_IsError()              => _response.Status.Should().Be(AgentStatus.Error);
    [Fact] public void ErrorMessage_ContainsMessage() => _response.ErrorMessage.Should().Be("something went wrong");
    [Fact] public void Data_IsNull()                 => _response.Data.Should().BeNull();
    [Fact] public void IsEmpty_IsTrue()              => _response.IsEmpty.Should().BeTrue();
    [Fact] public void Latency_IsSet()               => _response.Latency.Should().Be(TimeSpan.FromMilliseconds(5));
}

public class AgentResponse_As_TypedCast
{
    [Fact]
    public void As_CorrectType_ReturnsData()
    {
        var agent = new FakeAgent();
        var response = AgentResponse.From(agent, "hello", TimeSpan.Zero);

        response.As<string>().Should().Be("hello");
    }

    [Fact]
    public void As_WrongType_ReturnsNull()
    {
        var agent = new FakeAgent();
        var response = AgentResponse.From(agent, "hello", TimeSpan.Zero);

        response.As<List<int>>().Should().BeNull();
    }

    [Fact]
    public void As_WhenDataIsNull_ReturnsNull()
    {
        var agent = new FakeAgent();
        var response = AgentResponse.From(agent, null, TimeSpan.Zero);

        response.As<string>().Should().BeNull();
    }
}

public class AgentResponse_PipelineStageResult_FactoryMethods
{
    [Fact]
    public void Continue_ShouldContinue_IsTrue()
    {
        var result = PipelineStageResult.Continue("my-stage");
        result.ShouldContinue.Should().BeTrue();
        result.StageName.Should().Be("my-stage");
        result.AbortReason.Should().BeNull();
    }

    [Fact]
    public void Abort_ShouldContinue_IsFalse()
    {
        var result = PipelineStageResult.Abort("my-stage", "test reason");
        result.ShouldContinue.Should().BeFalse();
        result.AbortReason.Should().Be("test reason");
    }

    [Fact]
    public void Continue_WithNote_StoresNote()
    {
        var result = PipelineStageResult.Continue("s", "some note");
        result.AbortReason.Should().Be("some note");
    }
}
