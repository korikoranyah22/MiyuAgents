using System.Reflection;
using MiyuAgents.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace MiyuAgents.Core;

/// <summary>
/// Scans assemblies for classes annotated with [AgentCapability] and registers
/// them in the DI container with the correct lifetime.
/// Also builds the dependency graph for the PipelineRunner.
/// </summary>
public sealed class AgentRegistry
{
    private readonly Dictionary<string, AgentRegistration> _byRole = new();

    public void RegisterFromAssembly(Assembly assembly, IServiceCollection services)
    {
        var agentTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetCustomAttribute<AgentCapabilityAttribute>() is not null)
            .ToList();

        foreach (var type in agentTypes)
        {
            var cap  = type.GetCustomAttribute<AgentCapabilityAttribute>()!;
            var cogn = type.GetCustomAttribute<CognitiveSystemAttribute>();

            var registration = new AgentRegistration(type, cap, cogn);
            _byRole[cap.Role] = registration;

            var serviceType = typeof(IAgent);
            switch (cap.Lifetime)
            {
                case AgentLifetime.Singleton:  services.AddSingleton(type);  break;
                case AgentLifetime.Scoped:     services.AddScoped(type);     break;
                case AgentLifetime.Transient:  services.AddTransient(type);  break;
            }

            // Also register as IAgent for resolution by role
            services.AddSingleton<IAgent>(sp => (IAgent)sp.GetRequiredService(type));
        }
    }

    public AgentRegistration? GetByRole(string role)         => _byRole.GetValueOrDefault(role);
    public IReadOnlyList<AgentRegistration> GetAll()         => [.. _byRole.Values];
    public IReadOnlyList<AgentRegistration> GetByMemoryKind(MemoryKind kind) =>
        [.. _byRole.Values.Where(r => r.Capability.MemoryAccess.Contains(kind))];
}