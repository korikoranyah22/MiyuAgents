using MiyuAgents.Core.Attributes;
namespace MiyuAgents.Core;

public sealed record AgentRegistration(
    Type Type,
    AgentCapabilityAttribute Capability,
    CognitiveSystemAttribute? CognitiveSystem);