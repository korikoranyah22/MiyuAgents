# MiyuAgents — API real de Workflows (T001)

> Nota de relevamiento, T001 de la épica de documentación. Fuente de verdad:
> el código de `src/Workflows/` y los tests de `tests/MiyuAgents.Tests.Unit/Workflows/`
> (`WorkflowNodeTests.cs`, `WorkflowStrategiesTests.cs`, `WorkflowAuthoringTests.cs`,
> `WorkflowContractTests.cs`, `TraceTests.cs`, `ToolPortsTests.cs`, `DriverTests.cs`,
> `PhotoWorkflowTests.cs`, `WorkflowConcurrencyTests.cs`).
> Sin lenguaje promocional: sólo lo que la API hace, verificable con `dotnet test`.
> No toca AngelNaira; es insumo para el README (T002) y el spike recursivo (T003).

---

## 1. TFM y empaquetado (confirmado)

- **TFM:** `net10.0` (`src/MiyuAgents.csproj`). ImplicitUsings + Nullable habilitados.
- **PackageId:** `MiyuAgents`, **Version** `1.0.0`; `Authors`/`Company` = Miyu Rory Schrank.
- **Dependencia única:** `Microsoft.Extensions.Logging` **10.0.6** (el csproj; el `release/MiyuAgents.1.0.0.nupkg` empaquetado quedó con 10.0.5 → **el nupkg de release está desactualizado** respecto del csproj).
- **Globs de empaquetado:** los **default del SDK** (todo `*.cs` compilado → `lib/net10.0/MiyuAgents.dll`) **+** `<None Include="LICENSE" Pack="true" PackagePath="" />`. Verificado contra el nupkg: contiene sólo `MiyuAgents.nuspec`, `lib/net10.0/MiyuAgents.dll`, `LICENSE` y metadatos. **NO se empaquetan** README, docs ni examples (no hay glob que los incluya).

## 2. Inventario de APIs públicas — `MiyuAgents.Workflows`

| Tipo | Rol |
|---|---|
| `WorkflowNode` (sealed, `: AgentBase<NodeResult>, ICompositeAgent, INodeAgent`) | El **runtime** del Node compuesto: el control-loop sobre sus hijos. |
| `INodeAgent : IAgent` | Un agente que habla `NodeResult` rico (`RunNodeAsync`). La vía de la **recursión**. |
| `NodeResult` (record) | `AgentResponse` + `NodeSignal` + `Artifacts` + `Ask`. Aditivo sobre `AgentResponse`. |
| `NodeSignal` (enum) | `Done, NeedsInput, NeedsReplanning, Failed, HandBack, Continue, RequestTurn`. |
| `NodeState` (record inmutable) | Estado del nodo por paso: `Input, History, Round, Bids`. Se copia con `with`. |
| `Artifact` (record) | Entregable domain-neutral (`Kind`, `Name`, `Payload`, `Id`). |
| `Bid` (record) | Pedido de turno (bidding): `NodeId, Priority, Reason`. |
| `ControlDecision` (record) | `RunNext` + `Parallel` + `Emit`; helpers `Run/RunParallel/Stop`. |
| `IControlStrategy` | "¿Quién sigue?" (`NextAsync(NodeState, ct) → ControlDecision`). |
| `ISignalReactiveStrategy : IControlStrategy` | Intercepta `Failed/NeedsReplanning/HandBack` de un hijo (loop-back) o `null` = bubble-up. |
| `IDriver` | Responde los `NeedsInput` del nodo raíz. |
| `HumanDriver`, `CharacterDriver`, `SystemDriver` | Drivers concretos: bloquea→UI, responde por IAgent (personaje), nunca bloquea (mecánico). |
| `ResiliencePolicy` (record) | `MaxSteps` (anti-cuelgue → `Failed`) + `MaxRetries` (reintenta `Failed`). |
| `NodeScope`, `NodeTrace`, `NodePlugins` (static) | AsyncLocal ambiente: lane-path, sink de trace, plugins de la corrida. |
| `INodeTraceSink`, `TraceEvent`, `TraceKind`, `InMemoryTraceSink` | Trace: `NodeStart/NodeEnd/ChildResult/Reason/Response/Tool/Exec/Widget` con `Lane` = path jerárquico. |
| `ITool`, `IToolHost`, `ToolHost`, `IWorkflowPlugin`, `ISandboxPort`, `InMemorySandbox` | Ports de tool/plugin (impl reales = host/Spike 2). |
| `IWorkflowRegistry`, `WorkflowRegistry` | Resuelve agentes por id + fábricas de strategy por nombre (built-ins + `extraStrategies`). |
| `WorkflowSpec`, `NodeSpec`, `WorkflowBuilder`, `IWorkflowStore`, `InMemoryWorkflowStore` | Workflows como **data**; builder barato → hot-refresh sin rebuild. |
| `ConverseStrategy`, `FairConverseStrategy`, `DeliberateStrategy` (+`Phase`), `LoopStrategy`, `SequenceStrategy`, `ParallelStrategy`, `PlanExecuteStrategy` | Strategies concretas (§3). |

## 3. Cómo se compone un `WorkflowNode`

```csharp
new WorkflowNode(
    id: "root",
    strategy: new SequenceStrategy(["a", "b", "c"]),   // IControlStrategy
    children: new Dictionary<string, IAgent> { ["a"] = a, ["b"] = b, ["c"] = c },
    logger:   logger,                                  // ILogger<AgentBase<NodeResult>>
    policy:   new ResiliencePolicy(MaxSteps: 50, MaxRetries: 0),  // opcional
    driver:   null,                                    // opcional (responde NeedsInput)
    name:     null);                                   // opcional (default = id)
```

- Cada hijo es un `IAgent` → puede ser **otro `WorkflowNode`** (recursión: el hijo es `INodeAgent` y se invoca por `RunNodeAsync(state)`). Un `IAgent` común se envuelve: `Ok → Done`, `Error → Failed`.
- El loop (`RunLoopAsync`) por paso: `strategy.NextAsync(state)` → si `IsTerminal` termina con `Emit ?? Done` → corre los `RunNext` (secuencial o `Task.WhenAll` si `Parallel`) → por cada `Signal`:
  - `Failed` → reintenta hasta `MaxRetries`; agotado, la strategy reactiva puede interceptar o sube el signal.
  - `NeedsReplanning`/`HandBack` → `ISignalReactiveStrategy` (loop-back) o bubble-up.
  - `NeedsInput` → `driver.AnswerAsync(Ask)` y la respuesta entra al historial (id `"driver"`).
  - `RequestTurn` → encola un `Bid` en `state.Bids` (la strategy arbitra el próximo paso).
  - `Done`/`Continue` → historial.
- Presupuesto: si se agotan `MaxSteps`, el nodo termina `Failed` (acotado, anti-cuelgue — testeado en `Budget_CutsWhenStrategyNeverStops`).
- Entrada: `RunNodeAsync(new NodeState { Input = "…" })` (camino rico) o `ProcessAsync(ctx)` (vía `AgentBase`, arma el `NodeState` desde `ctx.UserMessage`).
- Authoring como data: `WorkflowSpec → WorkflowBuilder.Build(spec, registry) → WorkflowNode`; el `WorkflowRegistry` trae built-ins `sequence/parallel/loop/converse/plan-execute` y acepta strategies custom por nombre (`extraStrategies`).

## 4. Strategies de loop

| Strategy | Comportamiento (verificado en `WorkflowStrategiesTests`) |
|---|---|
| `SequenceStrategy(order)` | Uno por paso, en orden; `Done` cuando corrieron todos. |
| `ParallelStrategy(ids)` | Fan-out de todos en un paso; `Done`. |
| `LoopStrategy(agentId, continueOn = Continue)` | ReAct: corre UN agente hasta que su signal ≠ `continueOn`. Auto-scoped por `agentId` (anidado no se corta por señales ajenas). |
| `PlanExecuteStrategy(planId, execId, maxReplans = 2)` | plan → exec; si exec emite `NeedsReplanning` → **loop-back a planning** (bounded; agotado → el signal sube). |
| `DeliberateStrategy(phases)` | Fases en orden, cada una secuencial o paralela; cursor = `NodeState.Round` (stateless). |
| `ConverseStrategy(roster, maxRounds = 6, pollAgents)` | Round-robin + arbitraje de bids (anti-monopolio: el bid no bypasea la policy) + poll proactivo opcional (`IBiddingParticipant`). |
| `FairConverseStrategy(roster, maxRounds = 12, …)` | Equidad por participación efectiva (count/peso, ventana de recencia) + bids acotados + poll por relevancia; `Stop` si nadie quiere hablar. |

## 5. ¿Es `WorkflowNode` seguro para ejecución concurrente? → **Sí (con condiciones), verificado con tests**

**Por qué sí (diseño):**
- `WorkflowNode` es `sealed`, sus campos son `readonly` y no muta estado por-run: el `NodeState` se **pasa** (record inmutable, cada paso es un `with`), el loop y sus listas son locales a la invocación.
- Los scopes ambiente (`NodeScope` lane, `NodeTrace` sink, `NodePlugins`) son `AsyncLocal` → quedan **aislados por rama** en el fan-out paralelo y fluyen por awaits; un run no ve el lane de otro.
- El fan-out paralelo interno usa `Task.WhenAll` → cada rama lleva su propio contexto async.
- Las strategies built-in son puras sobre `NodeState` (sin estado mutable).

**Condiciones (lo que se comparte):** los hijos (`IAgent`), la strategy, el driver y el sink son instancias **compartidas** entre runs concurrentes del mismo nodo → deben ser thread-safe (los fakes de tests lo son; un `AgentBase` con contador/estado mutable o un `FairConverseStrategy` con `Random`/`onDecision` propio requieren cuidado).

**Evidencia (nuevo `WorkflowConcurrencyTests.cs`):**
- 20 runs del **mismo** nodo en paralelo → cada run termina `Done` con sus 3 artefactos completos y aislados; 60 llamadas exactas a las hojas (sin pérdidas).
- Nodo **recursivo** (outer→inner→leaf) × 10 runs paralelos → artefacto burbujea por run; hoja llamada 10 veces.
- Con `InMemoryTraceSink` encendido, 10 runs concurrentes → lanes correctos (`root`, `root/a`…) y 50 eventos exactos (sink thread-safe, sin corrupción).

→ **Conclusión para T002:** el primer ejemplo puede mostrar ejecución concurrente (mismo workflow disparado N veces, o fan-out paralelo), con la salvedad de que los nodos hoja reales deben ser thread-safe.

## 6. Nota para el spike recursivo (T003)

Lo que hoy **ya** soporta la recursión: sub-workflows por `NodeSpec.Children`/`INodeAgent` (profundidad arbitraria), lane-path en el trace, loop-back por `NeedsReplanning` (`PlanExecuteStrategy`) y el presupuesto `MaxSteps` como cota. Lo que **no** existe (a relevar en el spike): un nodo que se **auto-referencie** (llamarse a sí mismo por id — hoy el roster es un set acíclico, y `WorkflowBuilder` re-resolvería un ciclo en loop infinito al instanciar), un "stack de llamada"/estado de retorno explícito del estilo función recursiva, y una cota de profundidad además de `MaxSteps`. El contrato no bloquea ninguno: `WorkflowNode` como `IAgent` + `NodeState` inmutable alcanzarían para modelarlo, pero hoy no hay API first-class para ello.
