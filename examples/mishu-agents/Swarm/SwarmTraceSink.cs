using MiyuAgents.Workflows;
using MishuAgents.Demo.Output;

namespace MishuAgents.Demo.Swarm;

/// <summary>
/// Sink de trace (INodeTraceSink): imprime el árbol de ejecución del workflow en
/// vivo — cada NodeStart/NodeEnd con su LANE jerárquico y cada ChildResult con su
/// señal. Es el "se VEA" de la composición recursiva (operacion/swarm/analisis/…).
/// También junta los eventos para el resumen final.
/// </summary>
public sealed class SwarmTraceSink : INodeTraceSink
{
    readonly List<TraceEvent> _events = [];
    readonly Lock _gate = new();

    public IReadOnlyList<TraceEvent> Events
    {
        get { lock (_gate) return [.. _events]; }
    }

    public Task EmitAsync(TraceEvent ev, CancellationToken ct = default)
    {
        lock (_gate) _events.Add(ev);

        var (icon, color) = ev.Kind switch
        {
            TraceKind.NodeStart => ("▸", ConsoleWriter.Blue),
            TraceKind.NodeEnd => ("▪", ConsoleWriter.Blue),
            _ => ("·", ConsoleWriter.Dim),
        };
        var text = ev.Text is null ? "" : $" {ConsoleWriter.Col(ConsoleWriter.Gray, $"→ {ev.Text}")}";
        ConsoleWriter.Raw($"   {ConsoleWriter.Col(ConsoleWriter.Gray, "⎇")} {ConsoleWriter.Col(color, icon)} {ev.Lane}{text}");
        return Task.CompletedTask;
    }
}
