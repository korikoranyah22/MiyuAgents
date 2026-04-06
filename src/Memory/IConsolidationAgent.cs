using MiyuAgents.Core;

namespace MiyuAgents.Memory;

/// <summary>
/// An agent that stores memories produced during an exchange.
/// Called after the response is delivered to the user (fire-and-forget).
///
/// Neurological model: hippocampal-cortical consolidation during sleep (REM).
/// Reference: Walker (2017) Why We Sleep; Stickgold (2005).
/// </summary>
public interface IConsolidationAgent : IAgent
{
    /// <summary>
    /// Consolidate the exchange into long-term memory.
    /// Should not be awaited by the caller — runs as a background task.
    /// </summary>
    Task ConsolidateAsync(ExchangeRecord exchange, AgentContext ctx, CancellationToken ct);
}