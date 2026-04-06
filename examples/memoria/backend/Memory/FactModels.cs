namespace Memoria.Memory;

/// <summary>Typed query for semantic fact retrieval.</summary>
public record FactQuery(float[] Vector, int Limit = 5, float MinScore = 0.3f);

/// <summary>A single fact hit returned from Qdrant with its relevance score.</summary>
public record FactHit(string Id, string Label, string Value, string Text, float Score);

/// <summary>An entry to be stored in Qdrant — label, value, pre-embedded vector.</summary>
public record FactEntry(string Label, string Value, string Text, float[] Vector);

/// <summary>A fact extracted by the LLM extractor agent before embedding.</summary>
public record ExtractedFact(string Clave, string Valor);
