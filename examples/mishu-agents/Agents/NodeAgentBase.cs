using Microsoft.Extensions.Logging;
using MiyuAgents.Core;
using MiyuAgents.Llm;
using MiyuAgents.Workflows;
using MishuAgents.Demo.Contracts;

namespace MishuAgents.Demo.Agents;

/// <summary>
/// Puente entre los dos caminos de ejecución del framework: un AgentBase (Template
/// Method + lifecycle events) que además habla NodeResult (signals + artefactos),
/// la vía rica que el control-loop de WorkflowNode prefiere. Así un agente corre
/// DENTRO del árbol (RunNodeAsync) pero el observador sigue viendo los eventos de
/// IAgent (OnMessageReceived, OnLLMCallRequested, OnResponseProduced…).
/// </summary>
public abstract class NodeAgentBase<TResult> : AgentBase<TResult>, INodeAgent
{
    protected NodeAgentBase(ILogger<AgentBase<TResult>> logger) : base(logger) { }

    /// <summary>El gateway "archivo PURSUE" que usan los especialistas para consultar.</summary>
    protected abstract ILlmGateway Gateway { get; }

    public async Task<NodeResult> RunNodeAsync(NodeState state, CancellationToken ct = default)
    {
        var ctx = AgentContext.For(OperationBoard.OperationId, Guid.NewGuid().ToString("N"), state.Input);
        var response = await ProcessAsync(ctx, ct); // ← pasa por AgentBase: eventos + timing + errores
        return NodeResult.From(
            response,
            signal: ComputeSignal(state, ctx, response),
            artifacts: ProduceArtifacts(ctx, response),
            ask: AskFor(state, ctx, response));
    }

    /// <summary>Señal por defecto: Error → Failed, Ok → Done. Sobrescribir para NeedsReplanning, etc.</summary>
    protected virtual NodeSignal ComputeSignal(NodeState state, AgentContext ctx, AgentResponse response)
        => response.Status == AgentStatus.Error ? NodeSignal.Failed : NodeSignal.Done;

    /// <summary>Artefactos que este agente entrega al árbol (hallazgos, informe…).</summary>
    protected virtual IReadOnlyList<Artifact> ProduceArtifacts(AgentContext ctx, AgentResponse response) => [];

    /// <summary>Pregunta hacia el Driver cuando el signal es NeedsInput (null = no pregunta).</summary>
    protected virtual string? AskFor(NodeState state, AgentContext ctx, AgentResponse response) => null;

    /// <summary>
    /// Consulta al "archivo" (gateway pursue-archive) con eventos de ciclo de vida
    /// (OnLLMCallRequested/Responded) para que el monitoreo vea el consumo real.
    /// </summary>
    protected async Task<string> ConsultArchiveAsync(AgentContext ctx, string topic, CancellationToken ct)
    {
        await FireLlmCallRequestedAsync(ctx, "pursue-archive", estimatedTokens: Math.Max(8, topic.Length / 4));
        var req = new LlmRequest { Model = "pursue-archive", Messages = [new("user", topic)] };
        var resp = await Gateway.CompleteAsync(req, ct);
        await FireLlmCallRespondedAsync(ctx, resp.Usage, latency: TimeSpan.Zero);
        return resp.Content;
    }
}
