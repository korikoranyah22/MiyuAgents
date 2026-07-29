using System.Collections.Concurrent;

namespace MiyuAgents.Workflows;

/// <summary>
/// Driver HUMANO (§3.5): un <see cref="NodeSignal.NeedsInput"/> queda ENCOLADO esperando que la UI
/// responda. <see cref="AnswerAsync"/> bloquea (async) sobre un <see cref="TaskCompletionSource{T}"/>
/// hasta que el host llama <see cref="Provide"/>. Es el "humano en el code-tab" — el mismo molde de
/// pause/resume de <c>DeliberationSessionRegistry</c>, pero como port del framework. Sobrevive
/// detached: el host puede responder mucho después (incluso tras un F5, si re-hidrata las OpenAsks).
/// </summary>
public sealed class HumanDriver : IDriver
{
    sealed record Pending(string Ask, TaskCompletionSource<string> Tcs);
    readonly ConcurrentDictionary<string, Pending> _open = new();

    /// <summary>(promptId, ask) de las preguntas abiertas, para que la UI las liste / re-hidrate.</summary>
    public IReadOnlyList<(string PromptId, string Ask)> OpenAsks
        => _open.Select(kv => (kv.Key, kv.Value.Ask)).ToList();

    /// <summary>Se dispara cuando sube una pregunta nueva (el host la muestra en la UI).</summary>
    public event Action<string, string>? OnAsk;   // (promptId, ask)

    public Task<string> AnswerAsync(string ask, NodeState state, CancellationToken ct = default)
    {
        var id  = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _open[id] = new Pending(ask, tcs);
        ct.Register(() => { if (_open.TryRemove(id, out var p)) p.Tcs.TrySetCanceled(); });
        OnAsk?.Invoke(id, ask);
        return tcs.Task;
    }

    /// <summary>El host responde una pregunta abierta. false si el promptId ya no existe (respondida/cancelada).</summary>
    public bool Provide(string promptId, string answer)
        => _open.TryRemove(promptId, out var p) && p.Tcs.TrySetResult(answer);
}
