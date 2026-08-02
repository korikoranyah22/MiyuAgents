using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Core.Attributes;
using MiyuAgents.Llm;
using MiyuAgents.Workflows;
using MishuAgents.Demo.Contracts;
using MishuAgents.Demo.Data;
using MishuAgents.Demo.Output;

namespace MishuAgents.Demo.Agents;

/// <summary>
/// Cruza los perfiles de "personas normales" contra la firma N7. Primero se deja
/// engañar por la "perfección burocrática" (falso positivo → exoneración) y
/// después encuentra el perfil fantasma: el androide no está en la nómina,
/// coordina la operación desde arriba de la nómina.
/// </summary>
[AgentCapability(Role = "detección de infiltrados", CanInitiateLlmCalls = true)]
public sealed class InfiltratorDetectorAgent : NodeAgentBase<string>
{
    readonly OperationBoard _board;
    readonly ILlmGateway _gateway;

    public InfiltratorDetectorAgent(OperationBoard board, ILlmGateway gateway, ILogger<AgentBase<string>> logger)
        : base(logger)
    {
        _board = board;
        _gateway = gateway;
    }

    public override string AgentId => "infiltrados";
    public override string AgentName => "Detector de Infiltrados";
    public override AgentRole Role => AgentRole.Analysis;
    protected override ILlmGateway Gateway => _gateway;

    protected override async Task<string?> ExecuteCoreAsync(AgentContext ctx, CancellationToken ct)
    {
        _board.Verdicts.Clear(); // idempotente ante replan

        var quote = await ConsultArchiveAsync(ctx, "registro N7 · mantenimiento · perfiles sin legajo", ct);
        ConsoleWriter.Agent("🕵️", ConsoleWriter.Dim, "infiltrados", $"archivo: «{ConsoleWriter.Snippet(quote)}»");
        ConsoleWriter.Beat();

        var profiles = PersonnelProfiles.Build();
        var flagged = new List<PersonProfile>();

        foreach (var p in profiles)
        {
            var score = PersonnelProfiles.AndroidScore(p);
            var isFlag = score >= 0.65;
            if (isFlag) flagged.Add(p);

            ConsoleWriter.Agent("🕵️", isFlag ? ConsoleWriter.Magenta : ConsoleWriter.Dim, "infiltrados",
                $"{p.ProfileId} {p.Name} · {p.Role} · score {score:F2} {(isFlag ? "▲ FLAG" : "· limpio")}");
            _board.Verdicts.Add(new ProfileVerdict(p.ProfileId, p.Name, score, isFlag,
                isFlag ? "patrón N7 parcial: cero licencias + perfección sostenida" : "dentro del rango humano"));
            ConsoleWriter.Beat(18);
        }

        // Falso positivo: la "perfección burocrática" engaña al modelo.
        foreach (var p in flagged)
        {
            if (PersonnelProfiles.IsHardAndroidMatch(p)) continue; // (con humanos nunca pasa)

            ConsoleWriter.Agent("🕵️", ConsoleWriter.Magenta, "infiltrados",
                $"{p.ProfileId} {p.Name}: {p.Notes} Eso no es humano, es burocrático. FLAG.");
            ConsoleWriter.Beat(140);

            ConsoleWriter.Agent("🕵️", ConsoleWriter.Magenta, "infiltrados",
                $"…segunda pasada (reglas duras N7): var. térmica {p.ThermalVariance:F1} °C + familia registrada → EXONERADA. Falso positivo: la perfección burocrática me engañó.");
            _board.Verdicts.RemoveAll(v => v.ProfileId == p.ProfileId);
            _board.Verdicts.Add(new ProfileVerdict(p.ProfileId, p.Name, PersonnelProfiles.AndroidScore(p), false,
                "exonerada — falso positivo por «perfección burocrática»"));
            ConsoleWriter.Beat(140);
        }

        // El perfil fantasma: no está en la nómina, está en el registro de mantenimiento.
        var phantom = PersonnelProfiles.Phantom;
        const double phantomScore = 0.97;
        ConsoleWriter.Agent("🕵️", ConsoleWriter.Magenta, "infiltrados",
            $"{phantom.ProfileId} · {phantom.Name} · 168 h/sem · 0 licencias · sin familia · 0,0 °C → {phantomScore:F2} · FLAG DEFINITIVO");
        ConsoleWriter.Beat(140);
        ConsoleWriter.Agent("🕵️", ConsoleWriter.Magenta, "infiltrados",
            "el androide no está entre los 14 perfiles. Está arriba de la nómina. Coordina la operación.");
        ConsoleWriter.Beat(160);
        ConsoleWriter.Agent("🕵️", ConsoleWriter.Magenta, "infiltrados",
            "…siempre lo tuve adelante. Nunca lo vi.");
        _board.Verdicts.Add(new ProfileVerdict(phantom.ProfileId, phantom.Name, phantomScore, true,
            "perfil fantasma del registro N7: coordina la operación desde 1987; no figura en la nómina"));
        ConsoleWriter.Beat(160);

        var summary = $"{profiles.Count} perfiles relevados · 1 falso positivo exonerado · {phantom.ProfileId} flaggeado";
        var id = _board.Post(AgentId, "sintesis", "veredicto", summary);
        ConsoleWriter.Envelope(id, AgentId, "sintesis", "veredicto", summary);

        return summary;
    }

    protected override IReadOnlyList<Artifact> ProduceArtifacts(AgentContext ctx, AgentResponse response)
        => [new Artifact("infiltracion", $"veredictos-{_board.Verdicts.Count}", _board.Verdicts.ToArray(), AgentId)];
}
