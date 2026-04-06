using FluentAssertions;
using MiyuAgents.Llm;
using Xunit;

namespace MiyuAgents.Tests.Integration.Llm;

// ── CompleteAsync ─────────────────────────────────────────────────────────────

public class LoremGateway_CompleteAsync_ReturnsExpectedResponse : IAsyncLifetime
{
    private LlmResponse _response = default!;

    public async Task InitializeAsync()
    {
        var gw = new LoremGateway();
        _response = await gw.CompleteAsync(new LlmRequest
        {
            Model    = "lorem",
            Messages = []
        });
    }

    [Fact] public void Content_IsNotEmpty()             => _response.Content.Should().NotBeNullOrWhiteSpace();
    [Fact] public void FinishReason_IsStop()            => _response.FinishReason.Should().Be("stop");
    [Fact] public void Usage_InputTokens_Are100()       => _response.Usage.InputTokens.Should().Be(100);
    [Fact] public void Usage_OutputTokens_ArePositive() => _response.Usage.OutputTokens.Should().BeGreaterThan(0);
    [Fact] public void ToolCalls_IsEmpty()              => _response.ToolCalls.Should().BeEmpty();

    public Task DisposeAsync() => Task.CompletedTask;
}

public class LoremGateway_CompleteAsync_CyclesThroughResponses
{
    [Fact]
    public async Task EightConsecutiveCalls_ReturnVariedContent()
    {
        var gw = new LoremGateway();
        var contents = new List<string>();

        for (var i = 0; i < 8; i++)
        {
            var r = await gw.CompleteAsync(new LlmRequest { Model = "lorem", Messages = [] });
            contents.Add(r.Content);
        }

        // Cycles through 8 lorem phrases — all 8 calls should be distinct
        contents.Distinct().Should().HaveCount(8);
    }
}

// ── StreamAsync ───────────────────────────────────────────────────────────────

public class LoremGateway_StreamAsync_EmitsChunksThenFinalUsage : IAsyncLifetime
{
    private readonly List<LlmChunk> _chunks = [];

    public async Task InitializeAsync()
    {
        var gw = new LoremGateway();
        await foreach (var chunk in gw.StreamAsync(new LlmRequest { Model = "lorem", Messages = [] }))
            _chunks.Add(chunk);
    }

    [Fact] public void AtLeastOneChunk_WasReceived()
        => _chunks.Should().HaveCountGreaterThan(0);

    [Fact] public void LastChunk_IsMarkedComplete()
        => _chunks.Last().IsComplete.Should().BeTrue();

    [Fact] public void LastChunk_HasFinalUsage()
        => _chunks.Last().FinalUsage.Should().NotBeNull();

    [Fact] public void NonFinalChunks_AreNotMarkedComplete()
        => _chunks.Where(c => !c.IsComplete).Should().AllSatisfy(c => c.IsComplete.Should().BeFalse());

    [Fact] public void DeltaChunks_ContainText()
        => _chunks.Where(c => !c.IsComplete).Should().AllSatisfy(c => c.Delta.Should().NotBeEmpty());

    public Task DisposeAsync() => Task.CompletedTask;
}

// ── EmbedAsync ────────────────────────────────────────────────────────────────

public class LoremGateway_EmbedAsync_ReturnsDeterministicNormalizedVector
{
    [Fact]
    public async Task Vector_Has768Dimensions()
    {
        var vec = await new LoremGateway().EmbedAsync("hello world");
        vec.Should().HaveCount(768);
    }

    [Fact]
    public async Task Vector_IsNormalized_MagnitudeNearOne()
    {
        var vec  = await new LoremGateway().EmbedAsync("some text");
        var norm = MathF.Sqrt(vec.Sum(x => x * x));
        norm.Should().BeApproximately(1.0f, precision: 0.0001f);
    }

    [Fact]
    public async Task SameText_ProducesSameVector()
    {
        var gw   = new LoremGateway();
        var vec1 = await gw.EmbedAsync("deterministic");
        var vec2 = await gw.EmbedAsync("deterministic");
        vec1.Should().BeEquivalentTo(vec2);
    }

    [Fact]
    public async Task DifferentTexts_ProduceDifferentVectors()
    {
        var gw   = new LoremGateway();
        var vec1 = await gw.EmbedAsync("cat");
        var vec2 = await gw.EmbedAsync("dog");
        vec1.Should().NotBeEquivalentTo(vec2);
    }
}

// ── Stats accumulation ────────────────────────────────────────────────────────

public class LoremGateway_Stats_AccumulateAcrossCompletions
{
    [Fact]
    public async Task FiveCompletions_StatsReflectAllCalls()
    {
        var gw = new LoremGateway();

        for (var i = 0; i < 5; i++)
            await gw.CompleteAsync(new LlmRequest { Model = "lorem", Messages = [] });

        var stats = gw.GetStats();
        stats.InputTokens.Should().Be(500);   // 5 × 100
        stats.OutputTokens.Should().Be(250);  // 5 × 50
        stats.CallCount.Should().Be(5);
    }
}
