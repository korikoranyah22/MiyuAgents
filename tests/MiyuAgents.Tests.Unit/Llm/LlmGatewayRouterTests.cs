using FluentAssertions;
using MiyuAgents.Llm;
using MiyuAgents.Tests.Unit.Fakes;
using Xunit;

namespace MiyuAgents.Tests.Unit.Llm;

// ── Exact model match ────────────────────────────────────────────────────────

public class LlmGatewayRouter_Resolve_ExactMatch
{
    public static TheoryData<string, string> ExactMatchCases => new()
    {
        { "gpt-4o",          "openai"    },
        { "claude-3-5",      "anthropic" },
        { "deepseek-chat",   "deepseek"  },
        { "lorem",           "lorem"     },
    };

    [Theory, MemberData(nameof(ExactMatchCases))]
    public void ReturnsGateway_WithMatchingProvider(string model, string expectedProvider)
    {
        var gateways = new[]
        {
            FakeGateway.ForProvider("openai",    "gpt-4o", "gpt-4-turbo"),
            FakeGateway.ForProvider("anthropic", "claude-3-5", "claude-3-opus"),
            FakeGateway.ForProvider("deepseek",  "deepseek-chat", "deepseek-coder"),
            FakeGateway.ForProvider("lorem",     "lorem", "default"),
        };
        var router = new LlmGatewayRouter(gateways);

        var resolved = router.Resolve(model);

        resolved.ProviderName.Should().Be(expectedProvider);
    }
}

// ── Partial provider-name match ──────────────────────────────────────────────

public class LlmGatewayRouter_Resolve_PartialMatch
{
    [Fact]
    public void ModelContainingProviderName_ResolvesToThatGateway()
    {
        var gateways = new[]
        {
            FakeGateway.ForProvider("deepseek", "deepseek-v2"),
            FakeGateway.ForProvider("lorem",    "lorem"),
        };
        var router = new LlmGatewayRouter(gateways);

        // "deepseek-r1" not in SupportedModels but contains "deepseek" (provider name)
        var resolved = router.Resolve("deepseek-r1");

        resolved.ProviderName.Should().Be("deepseek");
    }
}

// ── No match falls back to default ──────────────────────────────────────────

public class LlmGatewayRouter_Resolve_UnknownModel_FallsBackToDefault
{
    [Fact]
    public void UnknownModel_ResolvesToFirstRegisteredGateway()
    {
        var first  = FakeGateway.ForProvider("primary",   "model-a");
        var second = FakeGateway.ForProvider("secondary", "model-b");
        var router = new LlmGatewayRouter([first, second]);

        var resolved = router.Resolve("completely-unknown-model");

        resolved.ProviderName.Should().Be("primary");
    }
}

// ── No gateways registered ───────────────────────────────────────────────────

public class LlmGatewayRouter_NoGateways_Throws
{
    [Fact]
    public void EmptyGateways_ConstructorThrows()
    {
        var act = () => new LlmGatewayRouter([]);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*No ILlmGateway*");
    }
}

// ── AggregatedStats ──────────────────────────────────────────────────────────

public class LlmGatewayRouter_AggregatedStats : IAsyncLifetime
{
    private LlmGatewayRouter _router   = default!;
    private FakeGateway      _gateway1 = default!;
    private FakeGateway      _gateway2 = default!;

    public async Task InitializeAsync()
    {
        _gateway1 = FakeGateway.ForProvider("provider-a", "model-a");
        _gateway2 = FakeGateway.ForProvider("provider-b", "model-b");
        _router   = new LlmGatewayRouter([_gateway1, _gateway2]);

        // Trigger some calls to accumulate stats
        await _gateway1.CompleteAsync(new LlmRequest
        {
            Model    = "model-a",
            Messages = [new ConversationMessage("user", "hi")]
        });
    }

    [Fact] public void Stats_ContainsBothProviders()
        => _router.AggregatedStats().Keys.Should().Contain("provider-a").And.Contain("provider-b");

    [Fact] public void Stats_ProviderA_HasCallCount()
        => _router.AggregatedStats()["provider-a"].CallCount.Should().Be(1);

    [Fact] public void Stats_ProviderB_HasZeroCalls()
        => _router.AggregatedStats()["provider-b"].CallCount.Should().Be(0);

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── Case-insensitive resolution ──────────────────────────────────────────────

public class LlmGatewayRouter_Resolve_CaseInsensitive
{
    [Fact]
    public void ModelLookup_IsCaseInsensitive()
    {
        var gateway = FakeGateway.ForProvider("openai", "GPT-4O");
        var router  = new LlmGatewayRouter([gateway]);

        router.Resolve("gpt-4o").ProviderName.Should().Be("openai");
        router.Resolve("GPT-4O").ProviderName.Should().Be("openai");
        router.Resolve("Gpt-4O").ProviderName.Should().Be("openai");
    }
}
