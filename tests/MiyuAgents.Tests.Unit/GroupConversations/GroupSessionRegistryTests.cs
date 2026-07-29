using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MiyuAgents.GroupConversations;
using MiyuAgents.Testing;
using Xunit;

namespace MiyuAgents.Tests.Unit.GroupConversations;

// ── Reloj falso: avanzamos el tiempo sin esperas reales ──────────────────────
file sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan d) => _now += d;
}

// ── Sesión de prueba que registra si fue liberada ────────────────────────────
file sealed class DisposableSession : IDisposable
{
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

public class GroupSessionRegistry_GetOrCreate
{
    [Fact]
    public void CreatesOnce_ReturnsSameInstance()
    {
        var reg     = new GroupSessionRegistry<object>(TimeSpan.FromMinutes(5), new FakeClock(DateTimeOffset.UnixEpoch));
        var created = 0;
        object Factory(string _) { created++; return new object(); }

        var a = reg.GetOrCreate("conv-1", Factory);
        var b = reg.GetOrCreate("conv-1", Factory);

        a.Should().BeSameAs(b);
        created.Should().Be(1);
        reg.Count.Should().Be(1);
    }

    [Fact]
    public void TryGet_ReturnsLiveSession_OrFalse()
    {
        var reg = new GroupSessionRegistry<object>(TimeSpan.FromMinutes(5), new FakeClock(DateTimeOffset.UnixEpoch));
        reg.GetOrCreate("conv-1", _ => new object());

        reg.TryGet("conv-1", out var hit).Should().BeTrue();
        hit.Should().NotBeNull();
        reg.TryGet("nope", out var miss).Should().BeFalse();
        miss.Should().BeNull();
    }
}

public class GroupSessionRegistry_Eviction
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task IdlePastTtl_IsSwept_AndOnEvictedFires()
    {
        var clock   = new FakeClock(DateTimeOffset.UnixEpoch);
        var evicted = new List<string>();
        var reg = new GroupSessionRegistry<object>(Ttl, clock,
            onEvicted: (id, _, _) => { evicted.Add(id); return ValueTask.CompletedTask; });

        reg.GetOrCreate("conv-1", _ => new object());
        clock.Advance(Ttl + TimeSpan.FromSeconds(1));

        var count = await reg.SweepExpiredAsync();

        count.Should().Be(1);
        evicted.Should().ContainSingle().Which.Should().Be("conv-1");
        reg.Count.Should().Be(0);
    }

    [Fact]
    public async Task RecentlyAccessed_SurvivesSweep()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var reg   = new GroupSessionRegistry<object>(Ttl, clock);
        reg.GetOrCreate("conv-1", _ => new object());

        clock.Advance(Ttl - TimeSpan.FromMinutes(1)); // todavía dentro del TTL
        var swept = await reg.SweepExpiredAsync();

        swept.Should().Be(0);
        reg.Count.Should().Be(1);
    }

    [Fact]
    public async Task TouchOnAccess_ResetsIdleClock()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var reg   = new GroupSessionRegistry<object>(Ttl, clock);
        reg.GetOrCreate("conv-1", _ => new object());

        clock.Advance(Ttl - TimeSpan.FromSeconds(1));
        reg.TryGet("conv-1", out _);                  // toca → resetea el idle
        clock.Advance(Ttl - TimeSpan.FromSeconds(1)); // otro casi-TTL desde el toque

        (await reg.SweepExpiredAsync()).Should().Be(0); // sigue viva por el toque
        reg.Count.Should().Be(1);
    }

    [Fact]
    public async Task DisposableSession_IsDisposedOnEvict()
    {
        var clock   = new FakeClock(DateTimeOffset.UnixEpoch);
        var session = new DisposableSession();
        var reg     = new GroupSessionRegistry<DisposableSession>(Ttl, clock);
        reg.GetOrCreate("conv-1", _ => session);

        clock.Advance(Ttl + TimeSpan.FromSeconds(1));
        await reg.SweepExpiredAsync();

        session.Disposed.Should().BeTrue();
    }
}

public class GroupSessionRegistry_CleanClose
{
    [Fact]
    public async Task Remove_DoesNotFireOnEvicted()
    {
        var evicted = new List<string>();
        var reg = new GroupSessionRegistry<object>(TimeSpan.FromMinutes(10), new FakeClock(DateTimeOffset.UnixEpoch),
            onEvicted: (id, _, _) => { evicted.Add(id); return ValueTask.CompletedTask; });

        reg.GetOrCreate("conv-1", _ => new object());
        reg.Remove("conv-1").Should().BeTrue();

        evicted.Should().BeEmpty();          // cierre limpio ≠ huérfano
        reg.Count.Should().Be(0);
        (await reg.SweepExpiredAsync()).Should().Be(0);
    }

    [Fact]
    public void Remove_DisposesSession_ButReturnsFalseWhenAbsent()
    {
        var session = new DisposableSession();
        var reg = new GroupSessionRegistry<DisposableSession>(TimeSpan.FromMinutes(10), new FakeClock(DateTimeOffset.UnixEpoch));
        reg.GetOrCreate("conv-1", _ => session);

        reg.Remove("conv-1").Should().BeTrue();
        session.Disposed.Should().BeTrue();
        reg.Remove("conv-1").Should().BeFalse();
    }
}

// ── Prueba de que sostiene orquestadores REALES y FUNCIONALES ────────────────
// Tie-in con el harness de #7: la sesión es un DefaultGroupConversationOrchestrator
// con ScriptedAgents. El registry lo crea una vez, lo recupera, y el orquestador
// recuperado sigue corriendo turnos. Así es como #2 (finalizar sueños huérfanos)
// se va a testear: sesión = loop de sueño, eviction = finalización.
public class GroupSessionRegistry_HoldsRealOrchestrators
{
    [Fact]
    public async Task GetOrCreate_HoldsLiveOrchestrator_ThatStillRunsTurns()
    {
        var reg = new GroupSessionRegistry<DefaultGroupConversationOrchestrator>(TimeSpan.FromMinutes(5));

        var orch = reg.GetOrCreate("conv-1", _ => BuildOrchestrator());
        reg.GetOrCreate("conv-1", _ => BuildOrchestrator()).Should().BeSameAs(orch); // no recrea
        reg.ActiveSessionIds.Should().ContainSingle().Which.Should().Be("conv-1");

        // El orquestador recuperado del registry sigue siendo funcional.
        await orch.AddParticipantAsync(new HumanParticipant("human", "Usuario"), default);
        await orch.AddParticipantAsync(
            new AgentParticipant("bot", "Bot", ScriptedAgent.Constant("bot", "Bot", "hola")), default);

        var seed   = GroupConversationMessage.FromUser("conv-1", "human", "Usuario", "ping");
        var result = await orch.SendMessageAsync(seed, default);

        result.ProducedMessages.Should().ContainSingle().Which.Content.Should().Be("hola");
    }

    private static DefaultGroupConversationOrchestrator BuildOrchestrator() =>
        new(new AnswerOncePolicy(), new BroadcastRouter(),
            NullLogger<DefaultGroupConversationOrchestrator>.Instance, maxRoundsPerTurn: 3);

    // El agente responde una vez (cuando el último mensaje es del humano), luego corta.
    private sealed class AnswerOncePolicy : ITurnPolicy
    {
        public string PolicyName => "answer-once";
        public Task<TurnSelection> SelectRespondersAsync(
            GroupConversationMessage m, IReadOnlyList<IParticipant> participants,
            IReadOnlyList<GroupConversationMessage> history, GroupConversationContext c, CancellationToken ct)
        {
            var last  = history.LastOrDefault();
            var agent = participants.OfType<AgentParticipant>().FirstOrDefault();
            return Task.FromResult(
                last?.SenderKind == ParticipantKind.Human && agent is not null
                    ? new TurnSelection([agent], "responde el bot")
                    : TurnSelection.None("ya respondió"));
        }
    }
}
