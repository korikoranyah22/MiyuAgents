namespace MiyuAgents.Core.Attributes;

/// <summary>
/// Optional annotation that documents the cognitive / neurological analogy
/// for a memory agent. Pure documentation — also readable at runtime for debug panels.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CognitiveSystemAttribute : Attribute
{
    /// <summary>Which cognitive system this agent models.</summary>
    public CognitiveSystem System { get; init; }

    /// <summary>Brain region or network, for reference.</summary>
    public string BrainRegion { get; init; } = "";

    /// <summary>Academic reference (author, year, paper title).</summary>
    public string Reference { get; init; } = "";
}