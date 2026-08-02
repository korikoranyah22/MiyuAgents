namespace MishuAgents.Demo.Output;

/// <summary>
/// Salida de consola con personalidad: color ANSI (desactivado si la salida está
/// redirigida), lock global (los especialistas corren EN PARALELO y no se pisan),
/// delay opcional para dar ritmo y "slow print" para el momento del giro.
/// Puro chrome del demo: el framework no se entera de que existe.
/// </summary>
public static class ConsoleWriter
{
    static readonly object Gate = new();
    static readonly bool Color = !Console.IsOutputRedirected;

    public const string Reset   = "\x1b[0m";
    public const string Dim     = "\x1b[2m";
    public const string Bold    = "\x1b[1m";
    public const string Cyan    = "\x1b[36m";
    public const string Green   = "\x1b[32m";
    public const string Yellow  = "\x1b[33m";
    public const string Red     = "\x1b[31m";
    public const string Magenta = "\x1b[35m";
    public const string Blue    = "\x1b[34m";
    public const string Gray    = "\x1b[90m";
    public const string White   = "\x1b[37m";

    /// <summary>Modo rápido (MISHU_FAST=1): sin delays, sin slow-print. Útil para CI.</summary>
    public static bool Fast => Environment.GetEnvironmentVariable("MISHU_FAST") == "1";

    /// <summary>Envuelve un texto en color (no-op si la salida está redirigida).</summary>
    public static string Col(string color, string s) => Color ? $"{color}{s}{Reset}" : s;

    /// <summary>Escribe una línea cruda (ya coloreada o no).</summary>
    public static void Raw(string s = "")
    {
        lock (Gate) Console.WriteLine(s);
    }

    public static void Line(string s = "") => Raw(s);
    public static void DimLine(string s) => Raw(Col(Gray, s));
    public static void Info(string s) => Raw(Col(Cyan, s));

    /// <summary>Línea de un agente: "  📁 Analista de Expedientes mensaje".</summary>
    public static void Agent(string icon, string color, string name, string text)
        => Raw($"  {Col(color, icon + " " + name)} {text}");

    /// <summary>Envelope del bus: "📨 mishu → expedientes · delegación · … [T-001]".</summary>
    public static void Envelope(string taskId, string from, string to, string kind, string summary)
        => Raw($"  {Col(Gray, "📨")} {Col(Bold, from)} {Col(Gray, "→")} {Col(Bold, to)} {Col(Gray, "·")} {kind} · {summary} {Col(Gray, $"[{taskId}]")}");

    /// <summary>Ritmo del demo: pausa corta entre líneas (desactivable con MISHU_FAST=1).</summary>
    public static void Beat(int ms = 40)
    {
        if (!Fast) Thread.Sleep(ms);
    }

    /// <summary>Escritura lenta, letra por letra — para los momentos dramáticos.</summary>
    public static void Slow(string s, int msPerChar = 7)
    {
        if (Fast)
        {
            Raw(s);
            return;
        }

        lock (Gate)
        {
            foreach (var ch in s)
            {
                Console.Write(ch);
                Thread.Sleep(msPerChar);
            }
            Console.WriteLine();
        }
    }

    /// <summary>Recorta un texto largo para las líneas de log.</summary>
    public static string Snippet(string s, int max = 64)
    {
        var t = s.Replace("«", "").Replace("»", "").Trim();
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }
}
