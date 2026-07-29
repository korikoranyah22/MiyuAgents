namespace MiyuAgents.Workflows;

/// <summary>
/// Extensión OPCIONAL de <see cref="IControlStrategy"/>: reacciona a un signal terminal/routing de
/// un hijo (Failed / NeedsReplanning / HandBack) para INTERCEPTARLO — p.ej. loop-back a planning —
/// en vez de dejar que el nodo lo suba al padre. Es lo que hace posible la "programación recursiva
/// a nivel workflows" (§7 del spike): el ejecutor no puede seguir → el orquestador vuelve al planning.
/// <para>Contrato: devolver <c>null</c> = comportamiento por defecto (bubble-up del signal). Una
/// decisión TERMINAL = subir con su <c>Emit</c>. Una decisión con hijos = RE-RUTEAR (correr eso en el
/// próximo paso). Una strategy que NO implementa esto → el signal siempre sube (comportamiento base).</para>
/// </summary>
public interface ISignalReactiveStrategy : IControlStrategy
{
    Task<ControlDecision?> OnChildSignalAsync(
        NodeState state, string childId, NodeResult result, CancellationToken ct = default);
}
