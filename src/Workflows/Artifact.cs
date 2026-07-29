namespace MiyuAgents.Workflows;

/// <summary>
/// Un ENTREGABLE producido por un Node — domain-neutral. <paramref name="Kind"/> lo interpreta
/// el host (p.ej. "file", "text", "plan", "diff", "image"); <paramref name="Payload"/> es opaco
/// al framework. Los artefactos suben con el <see cref="NodeResult"/> y el padre los compone.
/// </summary>
public sealed record Artifact(
    string  Kind,
    string? Name    = null,
    object? Payload = null,
    string? Id      = null);
