using FluentAssertions;
using MiyuAgents.Core;
using MiyuAgents.Pipeline;
using Xunit;

namespace MiyuAgents.Tests.Unit.Core;

public class InMemoryAgentEventBus_PublishAsync_CapturesEvents : IAsyncLifetime
{
    private InMemoryAgentEventBus _bus = default!;

    public async Task InitializeAsync()
    {
        _bus = new InMemoryAgentEventBus();
        await _bus.PublishAsync(new SampleEvent("a"));
        await _bus.PublishAsync(new SampleEvent("b"));
        await _bus.PublishAsync(new SampleEvent("c"));
    }

    [Fact] public void CapturedEvents_HasThreeEntries()    => _bus.CapturedEvents.Should().HaveCount(3);
    [Fact] public void CapturedEvents_AreCorrectTypes()    => _bus.CapturedEvents.Should().AllBeOfType<SampleEvent>();
    [Fact] public void CapturedEvents_OrderPreserved()
        => _bus.CapturedEvents
               .Cast<SampleEvent>()
               .Select(e => e.Name)
               .Should().ContainInOrder("a", "b", "c");

    public Task DisposeAsync() => Task.CompletedTask;

    private record SampleEvent(string Name);
}

public class InMemoryAgentEventBus_ReplayToAsync : IAsyncLifetime
{
    private readonly List<object> _replayed = [];

    public async Task InitializeAsync()
    {
        var source = new InMemoryAgentEventBus();
        await source.PublishAsync(new SampleEvent("x"));
        await source.PublishAsync(new SampleEvent("y"));

        var target = new CapturingBus(_replayed);
        await source.ReplayToAsync(target);
    }

    [Fact] public void Replayed_TwoEvents() => _replayed.Should().HaveCount(2);

    public Task DisposeAsync() => Task.CompletedTask;

    private record SampleEvent(string Name);

    private sealed class CapturingBus(List<object> log) : IAgentEventBus
    {
        public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class
        {
            log.Add(evt!);
            return Task.CompletedTask;
        }
    }
}

public class InMemoryAgentEventBus_Empty_CapturesNothing
{
    [Fact]
    public void CapturedEvents_IsEmpty()
    {
        var bus = new InMemoryAgentEventBus();
        bus.CapturedEvents.Should().BeEmpty();
    }
}
