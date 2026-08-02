# Mishu Agents · Operación Triángulo

Demo funcional y vistoso del framework: un enjambre de agentes autónomos que procesa
los **162 expedientes desclasificados de mayo 2026** (portal WAR.GOV/UFO, sistema
PURSUE), traza la anomalía triangular de la **Apollo 17 (diciembre de 1972)**, busca
**androides infiltrados** entre personas normales… y al final descubre que el
coordinador que lo orquestó todo nunca figuró en ninguna nómina.

> **Sobre el nombre.** El demo se llama *Mishu Agents* por su coordinador central
> (**Mishu**, el androide-secretario). El framework sobre el que corre es el que vive
> en `src/` de este repo: **MiyuAgents** (`.NET 10`, cero dependencias transitivas).
> El demo lo referencia como proyecto, no como paquete.

---

## Correr

```bash
dotnet run --project examples/mishu-agents
```

Salida: ~15 segundos de operativo narrado (logs con personalidad, árbol del workflow
en vivo, expediente final en caja ASCII y el giro). Para salida rápida sin delays
(CI, pipes):

```bash
MISHU_FAST=1 dotnet run --project examples/mishu-agents
```

El demo es **determinista**: mismo archivo, mismos scores, mismo replan, misma firma.
No necesita API keys: el "LLM" del operativo es `PursuitArchiveGateway`, un gateway
real (`ILlmGateway`/`GatewayBase`) que lee el archivo desclasificado.

---

## Qué vas a ver (y qué capacidad del framework muestra cada cosa)

| Momento | Qué pasa | Capacidad de MiyuAgents |
|---|---|---|
| Apertura | 162 fragmentos indexados en memoria declarativa | `InMemoryStore<TQuery, T>` (memoria declarativa) |
| Nómina | Los 5 agentes se descubren solos | `AgentRegistry` + `[AgentCapability]` + DI |
| Monitoreo | Mishu se suscribe a los eventos de todos | `IAgent` lifecycle events (`OnResponseProduced`, `OnLLMCallRequested`…) |
| Delegación | Mishu reparte 4 tareas | Envelopes del `OperationBoard` (contratos de mensajes) |
| Árbol | El enjambre corre como **un solo** workflow | `WorkflowNode` + `PlanExecuteStrategy` + `SequenceStrategy` + `ParallelStrategy` (3 niveles de recursión) |
| Replan | EX-0042 ilegible → vuelve a planificar | `NeedsReplanning` → loop-back acotado por `maxReplans` |
| Trace | Se ve crecer el árbol con lanes jerárquicos | `INodeTraceSink` + `NodeTrace.Begin` + `NodeScope` |
| Análisis | El analista procesa 3 ondas con memoria de trabajo | `MemoryWindow` con decay LTP (reconsolidación) + `AgentBase` |
| Consultas | Los especialistas leen el archivo | `GatewayBase` custom + `TokenTracker` + stats |
| Síntesis | El sintetizador corre su propio pipeline | `PipelineRunner` + `RetryStage` (el portal "se cae" 1 vez) + `AbortIfEmptyStage` + `ConditionalStage` + `TimedStage` |
| El giro | Mishu deja de ser invisible | El coordinador siempre fue el nodo `plan` del workflow |

## Arquitectura

```
examples/mishu-agents/
├── Program.cs                     # conductor: DI, carga del archivo, lanzamiento, informe, giro
├── Contracts/                     # CONTRATOS DE MENSAJES (ver tabla abajo)
│   ├── OperationMessages.cs       #   records: envelope, delegación, hallazgos, veredictos, informe
│   └── OperationBoard.cs          #   bus en memoria + estado compartido (thread-safe)
├── Data/
│   ├── ExpedienteArchive.cs       # 162 fragmentos PURSUE/WAR.GOV/UFO (EX-0042 = tachadura crítica)
│   ├── TriangleCatalog.cs         # Apollo 17 (dic 1972) + 3 triangulaciones más
│   ├── PersonnelProfiles.cs       # 14 "personas normales" + firma N7 del androide
│   └── Keywords.cs                # vocabulario de entidades del analista
├── Agents/
│   ├── NodeAgentBase.cs           # puente AgentBase + INodeAgent (eventos + signals/artefactos)
│   ├── MishuCoordinatorAgent.cs   # el secretario: planifica, delega, monitorea, se revela
│   ├── ExpedienteAnalystAgent.cs  # 162 expedientes en 3 ondas + memoria de trabajo
│   ├── TriangleTracerAgent.cs     # triangulaciones: tres luces, mismo cuadrante
│   ├── InfiltratorDetectorAgent.cs# perfiles vs firma N7 (falso positivo → PHANTOM-0)
│   ├── SynthesizerAgent.cs        # fusiona todo + pipeline propio
│   └── SynthesisStages.cs         # etapas del pipeline (lectura/retry, anexo, guardia, firma)
├── Swarm/
│   ├── SwarmWorkflow.cs           # el árbol WorkflowNode (plan-execute + parallel + sequence)
│   ├── PursuitArchiveGateway.cs   # el "LLM" del operativo (GatewayBase determinista)
│   └── SwarmTraceSink.cs          # trace del árbol en vivo (INodeTraceSink)
└── Output/
    ├── ConsoleWriter.cs           # color ANSI, locks, slow-print (chrome del demo)
    ├── ConsoleLogger.cs           # ILogger<T> de consola (filtra Debug, muestra retries)
    └── ReportFormatter.cs         # el expediente final en caja ASCII
```

## Contratos de mensajes

Toda la comunicación entre agentes pasa por **envelopes** tipados (el registro
visible de la operación, id `T-NNN`, `From`/`To`/`Kind`/`Summary`). En una
integración real estos mismos records viajan serializados por un bus (Eventuous,
MassTransit, Kafka…); acá el `OperationBoard` hace de bus en memoria.

| Record | Rol |
|---|---|
| `OperationEnvelope` | sobre de mensaje: `From → To`, `Kind`, `Summary` |
| `DelegationOrder` | orden de tarea emitida por Mishu (instrucción + prioridad) |
| `ExpedienteFinding` | salida del analista: entidades + tachaduras por fragmento |
| `TriangleSighting` | salida del trazador: luces, cuadrante, veredicto, confianza |
| `ProfileVerdict` | salida del detector: score de androide por perfil + flag |
| `SynthesisReport` | salida del sintetizador: el expediente desclasificado |

## Cómo se conectaría "de verdad"

Los agentes **no se conocen entre sí**: hablan por contratos. Por eso el demo es
directamente portable a producción:

1. **Bus**: reemplazar `OperationBoard` por un bus real manteniendo los mismos
   records de `Contracts/` (o serializándolos). El estado compartido pasa a un
   event store.
2. **LLM**: `PursuitArchiveGateway` cumple `ILlmGateway`. Para usar modelos reales
   se registra el gateway de DeepSeek/Anthropic/OpenAI en el `ServiceCollection` y
   se cambia el `Model` de las consultas — ningún agente se entera.
3. **Orquestación**: el árbol `SwarmWorkflow` es `WorkflowNode` puro del framework:
   `PlanExecuteStrategy` + `SequenceStrategy` + `ParallelStrategy` ya resuelven la
   coreografía (delegación, paralelismo, replan).
4. **Observabilidad**: el `SwarmTraceSink` y el monitoreo de Mishu son `INodeTraceSink`
   e lifecycle events — enchufables a SignalR/Eventuous sin tocar el árbol.

> **Nota de requisito.** El enunciado pedía documentar un stub si el framework "no
> estuviera instalado ni fuera encontrable". En este repo **sí está**: `src/` es el
> framework MiyuAgents completo (y hay nupkg en `release/`). El demo corre sobre él;
> los `Contracts/` quedan igual como el contrato arquitectónico de integración.

## El giro (spoiler)

El Detector de Infiltrados revisa 14 perfiles, se deja engañar por la "perfección
burocrática" de la archivista (falso positivo → exonerada) y termina flaggeando al
**PHANTOM-0**: un perfil sin legajo que coordina operaciones desde 1987 y aparece
sólo en el registro de mantenimiento N7. El informe final dice
"Coordinación a cargo de: **[CENSURADO]**". Después de la síntesis, el secretario
deja de ser invisible: fue Mishu, el nodo `plan` del workflow, el que delegó cada
tarea, pidió el replan y eligió quién hablaba. *"Soy el secretario. Los secretarios
no se ven."* — y el expediente se re-firma: **MISHU · androide coordinador · modelo
N7 · activo desde 1987**.
