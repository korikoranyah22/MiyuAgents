using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using MiyuAgents.Core;
using MiyuAgents.GroupConversations;
using MiyuAgents.Llm;
using MiyuAgents.Orchestration;
using MiyuAgents.Orchestration.Strategies;
using MiyuAgents.Pipeline;

namespace MiyuAgents.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MiyuAgents core infrastructure: AgentRegistry, PipelineRunner,
    /// TurnOrchestrator, event bus stubs, and all agents found in the provided assemblies.
    /// </summary>
    public static IServiceCollection AddMiyuAgents(
        this IServiceCollection services,
        params Assembly[] agentAssemblies)
    {
        var registry = new AgentRegistry();
        foreach (var assembly in agentAssemblies)
            registry.RegisterFromAssembly(assembly, services);

        services.AddSingleton(registry);
        services.AddSingleton<MiyuAgents.Pipeline.PipelineRunner>();
        services.AddSingleton<ITurnOrchestrator, DefaultTurnOrchestrator>();

        // Default no-op buses (replace in production with real implementations)
        services.AddSingleton<IAgentEventBus, NullAgentEventBus>();
        services.AddSingleton<IRealtimeBroadcaster, NullBroadcaster>();

        // GroupOrchestrator strategies
        services.AddSingleton<LlmRoundDecisionStrategy>();
        services.AddSingleton<PriorityRoundStrategy>();
        services.AddSingleton<RoundRobinStrategy>();

        // Group conversation policies and routers (choose via overload or replace in DI)
        services.AddSingleton<BroadcastTurnPolicy>();
        services.AddSingleton<AddressedOnlyPolicy>();
        services.AddSingleton<HumanOnlyPassthroughPolicy>();
        services.AddSingleton<BroadcastRouter>();
        services.AddSingleton<DirectMessageRouter>();

        // Lorem gateway for testing/development
        services.AddSingleton<LoremGateway>();

        return services;
    }

    /// <summary>
    /// Adds a DefaultGroupConversationOrchestrator with the specified policy and router.
    /// </summary>
    public static IServiceCollection AddGroupConversations<TPolicy, TRouter>(
        this IServiceCollection services,
        int maxRoundsPerTurn = 3)
        where TPolicy : class, ITurnPolicy
        where TRouter : class, IParticipantRouter
    {
        services.AddSingleton<TPolicy>();
        services.AddSingleton<TRouter>();
        services.AddSingleton<ITurnPolicy>(sp => sp.GetRequiredService<TPolicy>());
        services.AddSingleton<IParticipantRouter>(sp => sp.GetRequiredService<TRouter>());
        services.AddSingleton<IGroupConversationOrchestrator>(sp =>
            new DefaultGroupConversationOrchestrator(
                sp.GetRequiredService<ITurnPolicy>(),
                sp.GetRequiredService<IParticipantRouter>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DefaultGroupConversationOrchestrator>>(),
                maxRoundsPerTurn));
        return services;
    }

    /// <summary>
    /// Registers a LoremGateway as the default ILlmGateway.
    /// Use for development and testing without API keys.
    /// </summary>
    public static IServiceCollection AddLoremGateway(this IServiceCollection services)
    {
        services.AddSingleton<ILlmGateway, LoremGateway>();
        return services;
    }
}
