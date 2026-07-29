using FluentAssertions;
using MiyuAgents.Workflows;
using Xunit;

namespace MiyuAgents.Tests.Unit.Workflows;

// Tools FAKE que usan el plugin sandbox a través del host (prueba del port, sin Docker).
file sealed class WriteFileTool : ITool
{
    public string Name => "write_file";
    public string? Description => "escribe un archivo en el sandbox";
    public async Task<string> ExecuteAsync(string argsJson, IToolHost host, CancellationToken ct = default)
    {
        var sb = host.PluginAs<ISandboxPort>() ?? throw new InvalidOperationException("sin sandbox");
        // args "path|content" (simplificado; en real sería JSON)
        var parts = argsJson.Split('|', 2);
        await sb.WriteFileAsync(parts[0], parts[1], ct);
        return "ok";
    }
}

file sealed class ReadFileTool : ITool
{
    public string Name => "read_file";
    public string? Description => null;
    public async Task<string> ExecuteAsync(string argsJson, IToolHost host, CancellationToken ct = default)
        => await (host.PluginAs<ISandboxPort>()!).ReadFileAsync(argsJson, ct) ?? "(vacío)";
}

// ── W6: ports de tool/plugin (interfaces + fake in-memory) ───────────────────────────────────
public class ToolPortsTests
{
    [Fact]
    public async Task Tool_UsesSandboxPlugin_ThroughHost()
    {
        var sandbox = new InMemorySandbox();
        var host    = new ToolHost(sandbox);

        (await new WriteFileTool().ExecuteAsync("main.py|print(1)", host)).Should().Be("ok");
        (await new ReadFileTool().ExecuteAsync("main.py", host)).Should().Be("print(1)");
    }

    [Fact]
    public async Task Sandbox_Exec_Echoes()
        => (await new InMemorySandbox().ExecAsync("ls -la")).Should().Contain("ls -la");

    [Fact]
    public void ToolHost_WithoutPlugin_ExposesNull()
    {
        var host = new ToolHost();
        host.Plugin.Should().BeNull();
        host.PluginAs<ISandboxPort>().Should().BeNull();
    }

    [Fact]
    public void PluginAs_ReturnsTyped_WhenKindMatches()
    {
        IToolHost host = new ToolHost(new InMemorySandbox());
        host.PluginAs<ISandboxPort>().Should().NotBeNull();
        host.Plugin!.Kind.Should().Be("in-memory-sandbox");
    }
}
