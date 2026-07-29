namespace TurnPolicyExample;

/// <summary>
/// Solapamiento de n-gramas — la misma idea que <c>TextSimilarity</c> de
/// AngelNaira (el corte anti-espejo del sueño).  Acá vive en el example para
/// que el corte estructural sea parte de la POLICY, no del orquestador.
/// </summary>
public static class TextOverlap
{
    public static double MaxNGramOverlap(string text, IEnumerable<string> refs, int n = 4)
    {
        var grams = NGrams(text, n);
        if (grams.Count == 0) return 0;
        double max = 0;
        foreach (var r in refs)
        {
            var rg = NGrams(r, n);
            if (rg.Count == 0) continue;
            var hits = grams.Count(rg.Contains);
            max = Math.Max(max, (double)hits / grams.Count);
        }
        return max;
    }

    private static HashSet<string> NGrams(string text, int n)
    {
        var words = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + n <= words.Length; i++)
            set.Add(string.Join(' ', words, i, n));
        return set;
    }

    private static string Normalize(string text) =>
        new(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
}
