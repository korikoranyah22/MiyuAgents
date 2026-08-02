using Microsoft.Extensions.Logging;

namespace MishuAgents.Demo.Output;

/// <summary>
/// Logger mínimo de consola (ILogger&lt;T&gt;) para que el logging del framework
/// (retries, errores de agentes) se vea en pantalla con el mismo estilo del demo.
/// En producción se reemplaza por el logging de la app sin tocar nada más.
/// </summary>
public sealed class ConsoleLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    // Solo Warning+ a pantalla: el Debug del PipelineRunner (una línea por etapa)
    // ensucia el demo; el retry y los errores sí se ven.
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Los overloads con template (LogDebug(msg, args)) no chequean IsEnabled:
        // el filtro vive acá.
        if (!IsEnabled(logLevel)) return;

        var msg = formatter(state, exception);
        if (exception is not null)
            ConsoleWriter.Raw($"   {ConsoleWriter.Col(ConsoleWriter.Red, $"⚠️ [log:{logLevel}] {msg} — {exception.Message}")}");
        else
            ConsoleWriter.Raw($"   {ConsoleWriter.Col(ConsoleWriter.Gray, $"[log:{logLevel}] {msg}")}");
    }
}
