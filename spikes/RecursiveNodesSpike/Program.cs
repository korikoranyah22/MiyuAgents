using MiyuAgents.Core;
using MiyuAgents.Workflows;
using RecursiveNodesSpike;

// ─────────────────────────────────────────────────────────────────────────────
// Spike de T004 — el sketch de nodo recursivo (Recursion.cs) se expresa sobre las
// primitivas REALES del framework (INodeAgent, NodeResult, NodeSignal, NodeState,
// Artifact) sin tocar src/. Cinco escenarios:
//   1. suma recursiva        → tail-recursion + carry (propagación de estado);
//   2. cota de profundidad   → MaxDepth corta con Failed (stack-safety);
//   3. detección de ciclos   → (id, input) repetido en la cadena → Failed;
//   4. recorrido de un árbol de NodeSpec → recursión funcional sobre "workflows
//      como data" (el dominio real del framework), DFS iterativo con agenda;
//   5. fibonacci tail-recursivo → el caso canónico "nodo como función recursiva".
// Verificación (criterios de T004, README.md §7): si todas las aserciones PASS,
// la traza termina con el marcador RECURSIVE_SPIKE_OK y el exit code es 0; si
// alguna falla, se imprimen los fallos y se devuelve exit code ≠ 0 (sin marcador).
// ─────────────────────────────────────────────────────────────────────────────

// LIMITACIÓN ACTUAL DE AUTO-REFERENCIA (por qué esto es un spike y no API del
// framework): hoy WorkflowNode cierra el roster de hijos en el constructor
// (IReadOnlyDictionary<string, IAgent>) y WorkflowBuilder.BuildNode lo arma en
// build-time resolviendo cada Member del NodeSpec — un NodeSpec cuyo Member sea su
// propio Id haría que BuildNode recurra infinitamente al instanciar (no hay set de
// visitados). El sketch la esquiva con auto-referencia DIFERIDA: el cuerpo decide
// en runtime el próximo paso (RecursionDecision.Next → la continuación), y nunca
// existe un vínculo "hijo = yo" en el constructor.

var failures = new List<string>();

void Assert(bool cond, string label)
{
    Console.WriteLine($"  {(cond ? "PASS" : "FAIL")}  {label}");
    if (!cond) failures.Add(label);
}

// ── ESCENARIO 1 · suma recursiva (tail-recursion + carry) ───────────────────
Console.WriteLine("\n=== ESCENARIO 1 · suma recursiva (tail-recursion + carry) ===");
{
    var trace = new List<string>();
    var recSum = new RecursiveWorkflowNode("rec-sum",
        frame =>
        {
            var n   = int.Parse(frame.Input);
            var acc = frame.Carry is int c ? c : 0;
            return n == 0
                ? new RecursionDecision.Base(NodeResult.From(
                    new AgentResponse { AgentId = "rec-sum", AgentName = "rec-sum", Role = AgentRole.Orchestration, Data = acc },
                    artifacts: [new Artifact("int", "sum", acc)]))
                : new RecursionDecision.Next((n - 1).ToString(), acc + n);
        },
        trace: line => { Console.WriteLine(line); trace.Add(line); });

    var r = await recSum.RunNodeAsync(new NodeState { Input = "3" });

    var sum = r.Artifacts.SingleOrDefault(a => a.Name == "sum")?.Payload as int? ?? -1;
    Console.WriteLine($"[rec-sum] resultado: {sum}");
    Assert(r.Signal == NodeSignal.Done, "rec-sum termina Done");
    Assert(sum == 6, "rec-sum(3) = 6");
    Assert(trace.Count(l => l.Contains("enter")) == 4, "traza con 4 frames (3 → 2 → 1 → 0)");
    Assert(trace.Any(l => l.Contains("base 0")), "traza marca el caso base (n=0)");
}

// ── ESCENARIO 2 · cota de profundidad (stack-safety) ─────────────────────────
Console.WriteLine("\n=== ESCENARIO 2 · cota de profundidad (stack-safety) ===");
{
    var trace = new List<string>();
    var recDeep = new RecursiveWorkflowNode("rec-deep",
        frame =>
        {
            var n = int.Parse(frame.Input);
            return n == 0
                ? new RecursionDecision.Base(NodeResult.From(
                    new AgentResponse { AgentId = "rec-deep", AgentName = "rec-deep", Role = AgentRole.Orchestration, Data = n },
                    artifacts: [new Artifact("int", "deep", n)]))
                : new RecursionDecision.Next((n - 1).ToString(), null);
        },
        budget: new RecursionBudget(MaxDepth: 3),
        trace: line => { Console.WriteLine(line); trace.Add(line); });

    var r = await recDeep.RunNodeAsync(new NodeState { Input = "10" });

    Assert(r.Signal == NodeSignal.Failed, "corta por profundidad (Failed)");
    Assert(r.Response.ErrorMessage?.Contains("profundidad") == true, "razón = 'profundidad máxima excedida'");
    Assert(trace.Count(l => l.Contains("enter")) == 4, "corre raíz + 3 llamadas recursivas y corta en la 4ª");
}

// ── ESCENARIO 3 · detección de ciclos ────────────────────────────────────────
Console.WriteLine("\n=== ESCENARIO 3 · detección de ciclos ===");
{
    var trace = new List<string>();
    var recCycle = new RecursiveWorkflowNode("rec-cycle",
        _ => new RecursionDecision.Next("x", null),
        trace: line => { Console.WriteLine(line); trace.Add(line); });

    var r = await recCycle.RunNodeAsync(new NodeState { Input = "x" });

    Assert(r.Signal == NodeSignal.Failed, "detecta el ciclo (Failed)");
    Assert(r.Response.ErrorMessage?.Contains("ciclo") == true, "razón = 'ciclo detectado'");
    Assert(trace.Count(l => l.Contains("enter")) == 1, "corta en el 2º frame, sin colgarse");
}

// ── ESCENARIO 4 · recorrido recursivo de un árbol de NodeSpec (workflows como data) ──
Console.WriteLine("\n=== ESCENARIO 4 · recorrido recursivo de un árbol de NodeSpec (workflows como data) ===");
{
    var trace = new List<string>();

    // Mini-árbol de NodeSpec (el dominio "workflows como data" del framework):
    //   root → a → a1, a2 ; root → b   (a1, a2 y b son hojas).
    NodeSpec Leaf(string id) => new(id, "sequence", []);
    var specs = new Dictionary<string, NodeSpec>
    {
        ["root"] = new("root", "sequence", ["a", "b"],
            Children: [new("a", "sequence", ["a1", "a2"], Children: [Leaf("a1"), Leaf("a2")]), Leaf("b")]),
        ["a"]    = new("a", "sequence", ["a1", "a2"], Children: [Leaf("a1"), Leaf("a2")]),
        ["a1"]   = Leaf("a1"),
        ["a2"]   = Leaf("a2"),
        ["b"]    = Leaf("b"),
    };

    // Recursión funcional tail: DFS con agenda explícita en el carry. El cuerpo
    // devuelve Next(próximo nodo a visitar, agenda + hojas actualizadas) hasta que
    // la agenda se vacía → Base con el resultado (hojas en orden DFS).
    var flatten = new RecursiveWorkflowNode("flatten",
        frame =>
        {
            var spec         = specs[frame.Input];
            var (agenda, leaves) = frame.Carry is FlattenCarry fc
                ? (fc.Agenda, fc.Leaves)
                : (Array.Empty<string>(), Array.Empty<string>());
            var children     = spec.Children is null or [] ? [] : spec.Children.Select(c => c.Id).ToArray();

            if (children.Length == 0)                                   // hoja
            {
                string[] all = [.. leaves, spec.Id];
                return agenda.Length == 0
                    ? new RecursionDecision.Base(NodeResult.From(
                        new AgentResponse { AgentId = "flatten", AgentName = "flatten", Role = AgentRole.Orchestration, Data = string.Join(",", all) },
                        artifacts: [new Artifact("text", "leaves", string.Join(",", all))]))
                    : new RecursionDecision.Next(agenda[0], new FlattenCarry(agenda[1..], all));
            }

            // nodo interno: primero el primer hijo, el resto pasa a la agenda (DFS).
            return new RecursionDecision.Next(children[0], new FlattenCarry([.. children[1..], .. agenda], leaves));
        },
        trace: line => { Console.WriteLine(line); trace.Add(line); });

    var r = await flatten.RunNodeAsync(new NodeState { Input = "root" });

    var leaves = r.Artifacts.SingleOrDefault(a => a.Name == "leaves")?.Payload as string ?? "";
    Console.WriteLine($"[flatten] hojas: {leaves}");
    Assert(r.Signal == NodeSignal.Done, "flatten termina Done");
    Assert(leaves == "a1,a2,b", "hojas del árbol = a1, a2, b");
    Assert(trace.Count(l => l.Contains("enter")) == 5, "visita los 5 nodos una sola vez (sin ciclos)");
}

// ── ESCENARIO 5 · fibonacci tail-recursivo (el caso canónico "nodo como función") ──
Console.WriteLine("\n=== ESCENARIO 5 · fibonacci tail-recursivo (carry de par) ===");
{
    var trace = new List<string>();
    // fib(n) = fib(n-1) + fib(n-2) en su forma NAIVE es no-tail: post-procesa el
    // resultado del hijo (sumarlos) y eso necesita la continuación que recibe el
    // resultado del hijo — el gap documentado en README.md §4.4, NO implementado.
    // La versión tail del sketch lleva la pareja (prev, curr) en el carry: cada paso
    // reduce el input en 1 y actualiza la pareja; el caso base (n=0) pliega fib(n).
    var recFib = new RecursiveWorkflowNode("rec-fib",
        frame =>
        {
            var n = int.Parse(frame.Input);
            var (prev, curr) = frame.Carry is FibCarry f ? (f.Prev, f.Curr) : (0, 1);
            return n == 0
                ? new RecursionDecision.Base(NodeResult.From(
                    new AgentResponse { AgentId = "rec-fib", AgentName = "rec-fib", Role = AgentRole.Orchestration, Data = prev },
                    artifacts: [new Artifact("int", "fib", prev)]))
                : new RecursionDecision.Next((n - 1).ToString(), new FibCarry(curr, prev + curr));
        },
        trace: line => { Console.WriteLine(line); trace.Add(line); });

    var r = await recFib.RunNodeAsync(new NodeState { Input = "8" });

    var fib = r.Artifacts.SingleOrDefault(a => a.Name == "fib")?.Payload as int? ?? -1;
    Console.WriteLine($"[rec-fib] resultado: {fib}");
    Assert(r.Signal == NodeSignal.Done, "rec-fib termina Done");
    Assert(fib == 21, "rec-fib(8) = 21");
    Assert(trace.Count(l => l.Contains("enter")) == 9, "traza con 9 frames (8 → … → 0)");
}

// ── Veredicto ────────────────────────────────────────────────────────────────
Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("✅ TODO VERDE — la recursión funcional se expresa sobre las primitivas del framework (spike T004).");
    Console.WriteLine("RECURSIVE_SPIKE_OK");   // marcador de aceptación: ÚLTIMA línea de la traza (README §7)
    return 0;
}

Console.WriteLine($"❌ {failures.Count} fallo(s):");
foreach (var f in failures) Console.WriteLine($"   - {f}");
return 1;

// ── Tipos auxiliares de los escenarios 4 y 5 (carry legible en la traza) ─────
sealed record FlattenCarry(string[] Agenda, string[] Leaves)
{
    public override string ToString() => $"agenda=[{string.Join(",", Agenda)}] hojas=[{string.Join(",", Leaves)}]";
}

sealed record FibCarry(int Prev, int Curr)
{
    public override string ToString() => $"prev={Prev} curr={Curr}";
}
