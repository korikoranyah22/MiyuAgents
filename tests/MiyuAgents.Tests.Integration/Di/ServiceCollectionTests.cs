using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Extensions;
using MiyuAgents.GroupConversations;
using MiyuAgents.Llm;
using MiyuAgents.Orchestration;
using MiyuAgents.Pipeline;
using MiyuAgents.Core.Attributes;
using MiyuAgents.Tests.Integration.Helpers;
using Xunit;

namespace MiyuAgents.Tests.Integration.Di;

// ── Core service registration ─────────────────────────────────────────────────

public class ServiceCollection_AddMiyuAgents_CanResolveCoreDependencies
{
    private readonly IServiceProvider _provider;

    public ServiceCollection_AddMiyuAgents_CanResolveCoreDependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMiyuAgents();
        _provider = services.BuildServiceProvider();
    }

    [Fact] public void AgentRegistry_IsResolvable()        => _provider.GetService<AgentRegistry>().Should().NotBeNull();
    [Fact] public void PipelineRunner_IsResolvable()       => _provider.GetService<PipelineRunner>().Should().NotBeNull();
    [Fact] public void ITurnOrchestrator_IsResolvable()    => _provider.GetService<ITurnOrchestrator>().Should().NotBeNull();
    [Fact] public void IAgentEventBus_IsResolvable()       => _provider.GetService<IAgentEventBus>().Should().NotBeNull();
    [Fact] public void IRealtimeBroadcaster_IsResolvable() => _provider.GetService<IRealtimeBroadcaster>().Should().NotBeNull();
}

// ── LoremGateway registration ─────────────────────────────────────────────────

public class ServiceCollection_AddLoremGateway_RegistersGatewayAsInterface
{
    [Fact]
    public void ILlmGateway_ResolvesAsLoremGateway()
    {
        var services = new ServiceCollection();
        services.AddMiyuAgents();
        services.AddLoremGateway();
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ILlmGateway>().Should().BeOfType<LoremGateway>();
    }
}

// ── Group conversation registration ──────────────────────────────────────────

public class ServiceCollection_AddGroupConversations_RegistersOrchestrator
{
    [Fact]
    public void IGroupConversationOrchestrator_IsResolvable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMiyuAgents();
        services.AddGroupConversations<BroadcastTurnPolicy, BroadcastRouter>();
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IGroupConversationOrchestrator>().Should().NotBeNull();
    }

    [Fact]
    public void ITurnPolicy_ResolvesAsBroadcastTurnPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMiyuAgents();
        services.AddGroupConversations<BroadcastTurnPolicy, BroadcastRouter>();
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITurnPolicy>().Should().BeOfType<BroadcastTurnPolicy>();
    }
}

// ── Assembly scanning ─────────────────────────────────────────────────────────

public class ServiceCollection_AddMiyuAgents_ScansCapabilityAgentsFromAssembly
{
    private readonly AgentRegistry _registry;

    public ServiceCollection_AddMiyuAgents_ScansCapabilityAgentsFromAssembly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMiyuAgents(typeof(ItConversationAgent).Assembly);
        _registry = services.BuildServiceProvider().GetRequiredService<AgentRegistry>();
    }

    [Fact] public void ConversationAgent_IsInRegistry()
        => _registry.GetAll().Should().Contain(r => r.Capability.Role == "it-conversation");

    [Fact] public void AnalysisAgent_IsInRegistry()
        => _registry.GetAll().Should().Contain(r => r.Capability.Role == "it-analysis");

    [Fact] public void AnalysisAgent_HasEpisodicMemoryAccess()
        => _registry.GetByMemoryKind(MemoryKind.Episodic)
            .Should().Contain(r => r.Capability.Role == "it-analysis");

    [Fact] public void ConversationAgent_IsSingleton()
        => _registry.GetAll()
            .First(r => r.Capability.Role == "it-conversation")
            .Capability.Lifetime.Should().Be(AgentLifetime.Singleton);
}
