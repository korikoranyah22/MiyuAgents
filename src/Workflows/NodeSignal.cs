namespace MiyuAgents.Workflows;

/// <summary>
/// Cómo un Node le pide al PADRE que reaccione a su resultado. El control-loop del padre
/// (§3.4 del spike WORKFLOW-FRAMEWORK-00) lee este signal y decide: seguir, re-rutear a
/// planning, pedir input al Driver, encolar un bid, reintentar o cortar.
/// </summary>
public enum NodeSignal
{
    /// <summary>Terminó su trabajo; el resultado es final.</summary>
    Done,

    /// <summary>Necesita una respuesta del Driver (humano o personaje) para continuar. Ver <see cref="NodeResult.Ask"/>.</summary>
    NeedsInput,

    /// <summary>No puede avanzar con el plan actual → el padre debería volver a planificar.</summary>
    NeedsReplanning,

    /// <summary>Falló; el padre aplica su política de resiliencia (retry / hand-back / abortar).</summary>
    Failed,

    /// <summary>Devuelve el control al padre sin terminar y sin fallar (p.ej. "esto no me toca a mí").</summary>
    HandBack,

    /// <summary>Hizo un paso pero quiere seguir en el próximo (p.ej. una iteración más del loop).</summary>
    Continue,

    /// <summary>"Quiero hablar/participar en el próximo turno" (bidding, §3.6). Se encola en <see cref="NodeState.Bids"/>.</summary>
    RequestTurn,
}
