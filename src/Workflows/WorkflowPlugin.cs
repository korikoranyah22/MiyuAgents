using System.Collections.Concurrent;

namespace MiyuAgents.Workflows;

/// <summary>
/// El PLUGIN de sesión (§4 del spike): el entorno de ejecución bindeado al Node raíz que algunas
/// tools necesitan. El framework sólo define el PORT; el host provee la impl (sandbox Docker,
/// playground WASM, …). Los tools que no necesitan plugin corren sin él.
/// </summary>
public interface IWorkflowPlugin
{
    string Kind { get; }
}

/// <summary>Plugin de tipo SANDBOX: exec + archivos. Docker (código) lo implementa en Spike 2; acá
/// va sólo el port + un fake in-memory.</summary>
public interface ISandboxPort : IWorkflowPlugin
{
    Task<string>  ExecAsync(string command, CancellationToken ct = default);
    Task          WriteFileAsync(string path, string content, CancellationToken ct = default);
    Task<string?> ReadFileAsync(string path, CancellationToken ct = default);
}

/// <summary>Sandbox FAKE en memoria (default para tests / playground trivial): archivos en un dict,
/// exec que ecoa. Nada de Docker.</summary>
public sealed class InMemorySandbox : ISandboxPort
{
    readonly ConcurrentDictionary<string, string> _files = new();

    public string Kind => "in-memory-sandbox";
    public Task<string>  ExecAsync(string command, CancellationToken ct = default) => Task.FromResult($"$ {command}\n(ok)");
    public Task          WriteFileAsync(string path, string content, CancellationToken ct = default) { _files[path] = content; return Task.CompletedTask; }
    public Task<string?> ReadFileAsync(string path, CancellationToken ct = default) => Task.FromResult(_files.GetValueOrDefault(path));
}
