using MiyuAgents.Llm;
using Xunit;

namespace MiyuAgents.Tests.Unit.Llm;

public sealed class CacheFirstPromptTests
{
    [Fact]
    public void ToMessages_LayersProvidedOutOfOrder_EmitsCanonicalOrder()
    {
        var prompt = new CacheFirstPrompt
        {
            Common = "common",
            Delta = [new("user", "delta")],
            Step = [new("user", "step")],
            Role = [new("user", "role")],
            Session = [new("user", "session")],
        };

        Assert.Equal(
            ["session", "role", "step", "delta"],
            prompt.ToMessages().Select(message => message.Content));
    }

    [Fact]
    public void ToRequest_WhenOnlyDeltaChanges_PreservesExactPrefix()
    {
        var first = Build("delta-1").ToRequest("any-model");
        var second = Build("delta-2").ToRequest("any-model");

        Assert.Equal(first.SystemPrompt, second.SystemPrompt);
        Assert.Equal(
            first.Messages.Take(3).Select(MessageWire),
            second.Messages.Take(3).Select(MessageWire));
        Assert.NotEqual(MessageWire(first.Messages[^1]), MessageWire(second.Messages[^1]));
    }

    [Fact]
    public void ToRequest_UnorderedTools_UsesStableOrdinalOrder()
    {
        var request = Build("delta").ToRequest(
            "any-model",
            tools:
            [
                new("zeta", "z", new { type = "object" }),
                new("Alpha", "a", new { type = "object" }),
                new("beta", "b", new { type = "object" }),
            ]);

        Assert.Equal(["Alpha", "beta", "zeta"], request.Tools!.Select(tool => tool.Name));
    }

    [Fact]
    public void ToMessages_EmptyOptionalLayer_DoesNotEmitPhantomMessage()
    {
        var prompt = Build("delta") with
        {
            Role = [new("user", "")],
        };

        Assert.Equal(["session", "step", "delta"], prompt.ToMessages().Select(message => message.Content));
    }

    [Fact]
    public void DiagnoseStablePrefix_WhenOnlyDeltaChanges_ReturnsSameHash()
    {
        var first = Build("delta-1").DiagnoseStablePrefix("v2", "canonical");
        var second = Build("delta-2").DiagnoseStablePrefix("v2", "canonical");

        Assert.Equal(first.StablePrefixHash, second.StablePrefixHash);
        Assert.Equal(first.StablePrefixChars, second.StablePrefixChars);
    }

    [Fact]
    public void DiagnoseStablePrefix_WhenSessionChanges_ReturnsDifferentHash()
    {
        var first = Build("delta").DiagnoseStablePrefix("v2", "canonical");
        var second = (Build("delta") with
        {
            Session = [new("user", "different-session")],
        }).DiagnoseStablePrefix("v2", "canonical");

        Assert.NotEqual(first.StablePrefixHash, second.StablePrefixHash);
    }

    [Fact]
    public void DiagnoseStablePrefix_UnorderedEquivalentTools_ReturnsSameHash()
    {
        ToolDefinition alpha = new("alpha", "a", new { type = "object" });
        ToolDefinition beta = new("beta", "b", new { type = "object" });

        var first = Build("delta").DiagnoseStablePrefix(
            "v2", "canonical", tools: [beta, alpha]);
        var second = Build("delta").DiagnoseStablePrefix(
            "v2", "canonical", tools: [alpha, beta]);

        Assert.Equal(first.StablePrefixHash, second.StablePrefixHash);
        Assert.Equal(first.StablePrefixChars, second.StablePrefixChars);
    }

    [Fact]
    public void DiagnoseStablePrefix_WhenToolsetChanges_ReturnsDifferentHash()
    {
        var withoutTools = Build("delta").DiagnoseStablePrefix("v2", "canonical");
        var withTools = Build("delta").DiagnoseStablePrefix(
            "v2", "canonical",
            tools: [new("search", "Search", new { type = "object" })]);

        Assert.NotEqual(withoutTools.StablePrefixHash, withTools.StablePrefixHash);
    }

    private static CacheFirstPrompt Build(string delta) => new()
    {
        Common = "common",
        Session = [new("user", "session")],
        Role = [new("user", "role")],
        Step = [new("user", "step")],
        Delta = [new("user", delta)],
    };

    private static string MessageWire(ConversationMessage message) =>
        $"{message.Role}\n{message.Name}\n{message.Content}";
}
