using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.Core;
using MiyuAgents.Core.Attributes;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Registry;

// ── Decorated agents for test assembly scanning ───────────────────────────────

[AgentCapability(Role = "test-conversation", CanInitiateLlmCalls = true)]
file sealed class TestConversationAgent : FakeAgent
{
    public TestConversationAgent() : base("test-conversation", "TestConversation", AgentRole.Conversation) { }
}

[AgentCapability(
    Role         = "test-episodic",
    MemoryAccess = [MemoryKind.Episodic],
    Lifetime     = AgentLifetime.Scoped)]
file sealed class TestEpisodicAgent : FakeAgent
{
    public TestEpisodicAgent() : base("test-episodic", "TestEpisodic", AgentRole.Memory) { }
}

[AgentCapability(
    Role         = "test-declarative",
    MemoryAccess = [MemoryKind.Declarative])]
file sealed class TestDeclarativeAgent : FakeAgent
{
    public TestDeclarativeAgent() : base("test-declarative", "TestDeclarative", AgentRole.Memory) { }
}

// ── No attribute (should be ignored by registry) ─────────────────────────────
file sealed class UnregisteredAgent : FakeAgent
{
    public UnregisteredAgent() : base("unregistered") { }
}

// ── RegisterFromAssembly ──────────────────────────────────────────────────────

public class AgentRegistry_RegisterFromAssembly_FindsDecoratedAgents
{
    private readonly AgentRegistry _registry;

    public AgentRegistry_RegisterFromAssembly_FindsDecoratedAgents()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _registry = new AgentRegistry();
        _registry.RegisterFromAssembly(typeof(AgentRegistryTests).Assembly, services);
    }

    [Fact]
    public void GetAll_ContainsConversationAgent()
        => _registry.GetAll().Should().Contain(r => r.Capability.Role == "test-conversation");

    [Fact]
    public void GetAll_ContainsEpisodicAgent()
        => _registry.GetAll().Should().Contain(r => r.Capability.Role == "test-episodic");

    [Fact]
    public void GetAll_DoesNotContainUndecorated()
        => _registry.GetAll().Should().NotContain(r => r.Type == typeof(UnregisteredAgent));
}

// ── GetByRole ─────────────────────────────────────────────────────────────────

public class AgentRegistry_GetByRole_ReturnsCorrectRegistration
{
    private readonly AgentRegistry _registry;

    public AgentRegistry_GetByRole_ReturnsCorrectRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _registry = new AgentRegistry();
        _registry.RegisterFromAssembly(typeof(AgentRegistryTests).Assembly, services);
    }

    [Fact]
    public void GetByRole_KnownRole_ReturnsRegistration()
    {
        var reg = _registry.GetByRole("test-episodic");
        reg.Should().NotBeNull();
        reg!.Capability.Role.Should().Be("test-episodic");
    }

    [Fact]
    public void GetByRole_UnknownRole_ReturnsNull()
    {
        var reg = _registry.GetByRole("does-not-exist");
        reg.Should().BeNull();
    }
}

// ── GetByMemoryKind ───────────────────────────────────────────────────────────

public class AgentRegistry_GetByMemoryKind_FiltersCorrectly
{
    private readonly AgentRegistry _registry;

    public AgentRegistry_GetByMemoryKind_FiltersCorrectly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _registry = new AgentRegistry();
        _registry.RegisterFromAssembly(typeof(AgentRegistryTests).Assembly, services);
    }

    [Fact]
    public void GetByMemoryKind_Episodic_ReturnsEpisodicAgent()
    {
        var results = _registry.GetByMemoryKind(MemoryKind.Episodic);
        results.Should().Contain(r => r.Capability.Role == "test-episodic");
    }

    [Fact]
    public void GetByMemoryKind_Declarative_ReturnsDeclarativeAgent()
    {
        var results = _registry.GetByMemoryKind(MemoryKind.Declarative);
        results.Should().Contain(r => r.Capability.Role == "test-declarative");
    }

    [Fact]
    public void GetByMemoryKind_WorkingMemory_ReturnsEmpty()
    {
        var results = _registry.GetByMemoryKind(MemoryKind.WorkingMemory);
        results.Should().NotContain(r => r.Capability.Role == "test-conversation");
    }
}

// ── AgentCapabilityAttribute defaults ────────────────────────────────────────

public class AgentCapabilityAttribute_DefaultValues
{
    [Fact]
    public void Lifetime_DefaultsToSingleton()
    {
        var attr = new AgentCapabilityAttribute { Role = "test" };
        attr.Lifetime.Should().Be(AgentLifetime.Singleton);
    }

    [Fact]
    public void MemoryAccess_DefaultsToEmptyArray()
    {
        var attr = new AgentCapabilityAttribute { Role = "test" };
        attr.MemoryAccess.Should().BeEmpty();
    }

    [Fact]
    public void DependsOn_DefaultsToEmptyArray()
    {
        var attr = new AgentCapabilityAttribute { Role = "test" };
        attr.DependsOn.Should().BeEmpty();
    }

    [Fact]
    public void CanInitiateLlmCalls_DefaultsToFalse()
    {
        var attr = new AgentCapabilityAttribute { Role = "test" };
        attr.CanInitiateLlmCalls.Should().BeFalse();
    }
}

// ── CognitiveSystemAttribute ──────────────────────────────────────────────────

public class CognitiveSystemAttribute_Properties
{
    [Fact]
    public void BrainRegion_DefaultsToEmpty()
    {
        var attr = new CognitiveSystemAttribute();
        attr.BrainRegion.Should().BeEmpty();
    }

    [Fact]
    public void Reference_DefaultsToEmpty()
    {
        var attr = new CognitiveSystemAttribute();
        attr.Reference.Should().BeEmpty();
    }

    [Fact]
    public void System_CanBeSet()
    {
        var attr = new CognitiveSystemAttribute { System = CognitiveSystem.Hippocampus };
        attr.System.Should().Be(CognitiveSystem.Hippocampus);
    }
}

// Alias for assembly reference
file static class AgentRegistryTests { }
