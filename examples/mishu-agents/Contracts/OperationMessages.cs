namespace MishuAgents.Demo.Contracts;

// ─────────────────────────────────────────────────────────────────────────────
// Contratos de mensajes del enjambre Mishu.
//
// Cada record ES un contrato entre agentes. En una integración real estos mismos
// tipos viajan por un bus (Eventuous, MassTransit, Kafka…) serializados; acá el
// OperationBoard hace de bus en memoria y cada envelope queda registrado para que
// se VEA toda la comunicación. Ver README.md → "Contratos de mensajes".
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Envelope: todo mensaje entre agentes pasa por acá. Es el registro visible de la operación.</summary>
public sealed record OperationEnvelope(
    string TaskId,
    string From,
    string To,
    string Kind,      // "delegación" | "hallazgos" | "triangulación" | "veredicto" | "informe"
    string Summary,
    object? Payload = null);

/// <summary>Orden de delegación emitida por el coordinador (Mishu).</summary>
public sealed record DelegationOrder(
    string AgentId,
    string Instruction,
    int Priority);

/// <summary>Hallazgo del Analista de Expedientes sobre un fragmento del archivo PURSUE.</summary>
public sealed record ExpedienteFinding(
    string FragmentId,
    string Source,
    string Classification,
    string[] Entities,
    bool Redacted,
    bool Reconstructed = false);

/// <summary>Avistamiento triangular trazado (la firma geométrica de tres luces).</summary>
public sealed record TriangleSighting(
    string IncidentId,
    string When,
    string Where,
    int Lights,
    string Quadrant,
    string Verdict,
    double Confidence);

/// <summary>Veredicto del Detector de Infiltrados sobre un perfil de "persona normal".</summary>
public sealed record ProfileVerdict(
    string ProfileId,
    string Person,
    double AndroidScore,
    bool Flagged,
    string Reason);

/// <summary>Informe final: el expediente desclasificado que fusiona los tres análisis.</summary>
public sealed record SynthesisReport(
    string Title,
    string Classification,
    string[] Sources,
    string[] Hallazgos,
    TriangleSighting[] Triangulaciones,
    ProfileVerdict[] Infiltracion,
    string Conclusion,
    string Firma);
