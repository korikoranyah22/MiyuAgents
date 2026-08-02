namespace MishuAgents.Demo.Contracts;

/// <summary>
/// El "pizarrón" de la operación: estado compartido + registro de envelopes.
/// Reemplaza a un bus de mensajes real (Eventuous/Kafka) dentro del demo — los
/// agentes no se conocen entre sí: toda la comunicación pasa por acá (o por el
/// orquestador). Thread-safe: los especialistas corren en paralelo
/// (ParallelStrategy) y escriben en secciones propias.
/// </summary>
public sealed class OperationBoard
{
    public const string OperationId = "OP-TRIANGULO-1972";

    readonly object _gate = new();
    readonly List<OperationEnvelope> _messages = [];
    int _nextTaskId = 100;

    // ── Estado compartido de la operación ────────────────────────────────────
    public string? Plan { get; set; }
    public int ReplanCount { get; set; }
    public string? ReplanInstruction { get; set; }
    public bool PendingReplan { get; set; }
    public string? ArchiveEpigraph { get; set; }   // cita del portal WAR.GOV/UFO
    public string? ReportAnexo { get; set; }       // ANEXO-I (infiltración)
    public string? ReportFirma { get; set; }       // quién firma… (spoiler)
    public bool Revealed { get; set; }

    // ── Secciones de resultados (una por especialista) ───────────────────────
    public List<ExpedienteFinding> Findings { get; } = [];
    public List<TriangleSighting> Sightings { get; } = [];
    public List<ProfileVerdict> Verdicts { get; } = [];
    public SynthesisReport? Report { get; set; }

    public IReadOnlyList<OperationEnvelope> Messages
    {
        get { lock (_gate) return [.. _messages]; }
    }

    public int MessageCount
    {
        get { lock (_gate) return _messages.Count; }
    }

    /// <summary>Registra un envelope con id secuencial y devuelve ese id.</summary>
    public string Post(string from, string to, string kind, string summary, object? payload = null)
    {
        lock (_gate)
        {
            var id = $"T-{_nextTaskId++}";
            _messages.Add(new OperationEnvelope(id, from, to, kind, summary, payload));
            return id;
        }
    }
}
