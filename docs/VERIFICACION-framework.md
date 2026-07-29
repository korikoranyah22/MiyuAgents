# MiyuAgents — Verificación del framework (pre-migración)

> Estado: VERIFICACIÓN (auditoría, no implementación) — 2026-06-15
> Objetivo: antes de migrar AngelNaira al rail nuevo, auditar **MiyuAgents como
> framework** — coherencia interna, divergencias, y si las capacidades nuevas
> (#5/#7/#3) y el seam pendiente (Opción A) encajan limpio.
> Leyenda: ✅ coherente · 🟡 funciona pero hay roce · ⚠️ smell / decisión ·
> ❌ roto / contradicción

Relación con los docs de migración (que asumen AngelNaira):
[`pending/MIGRACION-MIYUAGENTS-00-overview.md`](../../../pending/MIGRACION-MIYUAGENTS-00-overview.md).
Este doc mira **hacia adentro del framework**, no hacia AngelNaira.

---

## 1. Inventario de capas (qué hay y para qué)

| Capa | Rol | Estado |
|---|---|---|
| `Core/` | `AgentBase<T>`, `AgentContext`, `AgentResponse`, roles, registry, eventos | ✅ núcleo sólido |
| `Llm/` | gateways, router, tokens, chunks | ✅ |
| `Memory/` | stores, consolidación, embeddings | ✅ |
| `Pipeline/` | `PipelineRunner` + stages (retry/timed/parallel/conditional) | ✅ |
| `Orchestration/` | **rail 1:1** (`ITurnOrchestrator`) + **rail grupo A** (`IGroupOrchestrator`, stateless, strategies) | ⚠️ ver §2 |
| `GroupConversations/` | **rail grupo B** (`IGroupConversationOrchestrator`, stateful, policies + participants) | ✅ rail vivo de AngelNaira |
| `Testing/` (nuevo, #7) | `ScriptedAgent` determinista | ✅ |

---

## 2. ⚠️ Hallazgo principal — TRES rails de orquestación, dos de ellos para "grupo"

El framework tiene tres orquestadores, no uno:

| Rail | Interfaz | Forma | Quién lo usa | Veredicto |
|---|---|---|---|---|
| **1:1** | `Orchestration/ITurnOrchestrator` + `DefaultTurnOrchestrator` | un turno, un pipeline | AngelNaira (`NairaTurnOrchestrator` envuelve `DefaultTurnOrchestrator`) | ✅ **vivo y canónico** para 1:1 |
| **Grupo A** | `Orchestration/IGroupOrchestrator` + `DefaultGroupOrchestrator` + `Strategies/` (`IRoundDecisionStrategy`) | **stateless**, recibe `IAgent[]` por llamada, decide por estrategia de ronda | example `debate` + tests | 🟡 vivo SÓLO en el example/tests |
| **Grupo B** | `GroupConversations/IGroupConversationOrchestrator` + `DefaultGroupConversationOrchestrator` + `ITurnPolicy` + `IParticipant` | **stateful**, posee participantes, ciclo de vida completo | AngelNaira (grupal + sueño) | ✅ **vivo y canónico** para grupo |

El propio `IGroupConversationOrchestrator` documenta la distinción A vs B
("stateless single-turn N agents" vs "stateful, owns participants"). Es una
distinción **intencional y razonable** — no son un copy-paste. PERO arrastran
tres smells concretos:

### ⚠️ Smell 1 — `GroupTurnResult` existe DOS veces

- `MiyuAgents.Orchestration.GroupTurnResult` (sobre `GroupMessage`)
- `MiyuAgents.GroupConversations.GroupTurnResult` (sobre `GroupConversationMessage`)

Mismo nombre, distinto namespace, **misma forma** (ProducedMessages, Decisions,
RoundsExecuted, TotalLatency). Cualquier archivo que importe ambos namespaces
necesita desambiguar. El example `turnpolicy` y AngelNaira ya conviven con esto,
pero es una trampa de legibilidad. → **Decisión: renombrar uno** (p.ej. el de
`Orchestration` a `GroupRoundResult`, que describe mejor su modelo de rondas) o
unificar el record en un namespace neutral.

### ⚠️ Smell 2 — `GroupMessage` vs `GroupConversationMessage`

Dos tipos de mensaje de grupo. `GroupMessage` (A) es un record plano
(Sender/Role/Content/Timestamp). `GroupConversationMessage` (B) es rico
(MessageId, ConversationId, SenderId/Name/Kind, AddressedToId, Metadata). B es
superconjunto de A. → Si A sobrevive, dejar claro que es el "modelo liviano"; si
se deprecia A, desaparece el dúo.

### ⚠️ Smell 3 — `IRoundDecisionStrategy` (A) vs `ITurnPolicy` (B): el MISMO rol

Ambos responden "¿quién habla en la próxima ronda?". Firmas distintas:
- `IRoundDecisionStrategy.DecideAsync(...) → OrchestratorDecision` (devuelve ids).
- `ITurnPolicy.SelectRespondersAsync(...) → TurnSelection` (devuelve `AgentParticipant[]`).

Comparten `OrchestratorDecision` (B lo mapea desde `TurnSelection` vía
`MapDecision`). Es la duplicación conceptual más cara: las estrategias de A
(`RoundRobin`, `ExpertRouting`, `LlmRoundDecision`, `Priority`,
`SentimentThreshold`) **no son reutilizables** por B aunque resuelvan el mismo
problema. → Si quisiéramos esas estrategias en grupo-B (p.ej. `LlmRoundDecision`
para una grupal moderada), hoy habría que reescribirlas como `ITurnPolicy`.

### Veredicto §2

El split 1:1 vs grupo es sano. El split **grupo-A vs grupo-B no se sostiene a
largo plazo**: B es estrictamente más capaz (stateful, participantes, lifecycle)
y es donde vive todo lo nuevo (#3/#5/#7 + la migración). **Recomendación: declarar
B canónico para grupo, y A en estado "congelado / example-only"**, con un ADR que
diga: features nuevas de grupo van a B; A se mantiene sólo mientras el example
`debate` lo use. Opcional futuro: portar las estrategias útiles de A como
`ITurnPolicy` y deprecar A.

**Esto NO bloquea la migración de AngelNaira** (que ya vive 100% en B). Es deuda
de framework a anotar, no a pagar ahora.

---

## 3. Coherencia de las capacidades nuevas (#5 / #7 / #3)

| Capacidad | ¿En el rail correcto? | ¿Coherente con lo existente? | Estado |
|---|---|---|---|
| **#5 `ConversationMode`** (`Core/`) | ✅ está en `Core` (lo lee cualquier rail, no sólo grupo) | ✅ default `Normal`, helpers `IsDream/IsGroup/IsCarnal`; convive con `Metadata` sin pisarlo | ✅ |
| **#7 `ScriptedAgent`** (`Testing/`) | ✅ proyecto aparte, referenciado por example + unit tests | ✅ `AgentBase<string>` real, no toca producción | ✅ |
| **#3 `IGroupSessionRegistry`** (`GroupConversations/`) | ✅ en rail B (donde se necesita) | ✅ genérico `<TSession>`, `TimeProvider` inyectable, `onEvicted` para #2 | ✅ |

### Roces menores a revisar

- 🟡 **#5 y Smell 3**: `ConversationMode` es ortogonal a quién decide el turno,
  pero si alguna vez una `ITurnPolicy` necesita el modo, hoy lo recibe vía
  `GroupConversationContext` → `AgentContext.Mode`. Verificar que
  `GroupConversationContext.From(...)` propague `Mode` (el example no lo ejercita
  porque sus policies miran `SenderId`, no el modo). **Acción: test que confirme
  que `ctx.Mode` sobrevive el round-trip `AgentContext → GroupConversationContext
  → ToAgentContext()`** (hoy sin cobertura).
- 🟡 **#3 ubicación**: `GroupSessionRegistry<T>` es genérico y podría servir al
  rail 1:1 también (sesiones de turn-orchestrator). Está en `GroupConversations/`
  por nombre, pero no depende de nada de grupo. Si A/B convergen, considerar
  moverlo a `Core/` o un `Sessions/`. Por ahora: ✅ donde está.

---

## 4. ⚠️ El seam de Opción A (`ITurnExecutor`) — ¿encaja en el rail B?

La migración de AngelNaira bajo Opción B **no** necesita esto; pero el usuario
pidió evaluar A como capacidad de framework. Verificación de factibilidad:

`DefaultGroupConversationOrchestrator.SendMessageAsync` corre el loop y, por cada
responder elegido, llama `RespondAsync(agentP, ctx, ct)` (`:128`), que hace:
`agent.ProcessAsync` → envolver string en `GroupConversationMessage` →
`OnMessageProduced`. **Hardcodeado.** Para que AngelNaira ríe el loop del
framework necesitaría inyectar SU ejecutor (eventos + projection + broadcast +
AgentContext rico + id anti-overflow).

**Diseño propuesto del seam (sin implementar):**

```csharp
// Nuevo en GroupConversations/
public interface ITurnExecutor
{
    // Dada la selección y el contexto del grupo, produce el mensaje del turno
    // (incluye los efectos secundarios del consumidor: persistencia, broadcast…).
    Task<GroupConversationMessage?> ExecuteAsync(
        AgentParticipant responder,
        GroupConversationContext ctx,
        IReadOnlyList<GroupConversationMessage> history,
        CancellationToken ct);
}
```

`DefaultGroupConversationOrchestrator` aceptaría un `ITurnExecutor?` opcional en
el ctor; si es null, usa el `RespondAsync` actual como **default executor**
(no-breaking). AngelNaira pasaría un `NairaTurnExecutor` que envuelve el actual
`RunOneAgentTurnAsync`.

| Punto de verificación | Resultado |
|---|---|
| ¿El default actual se puede extraer a un `DefaultTurnExecutor`? | ✅ `RespondAsync` ya es esa función, casi literal |
| ¿`GroupConversationContext` lleva lo que AngelNaira arma hoy (ProfileId, N-profiles, Metadata×10, Model)? | ⚠️ **NO** — hoy AngelNaira arma el `AgentContext` a mano, no vía `GroupConversationContext.From`. El seam exige enriquecer `GroupConversationContext` o que el executor reciba un hook para construir el ctx |
| ¿El loop del framework permite el id anti-overflow `t-{guid}`? | 🟡 sí si el executor controla la creación del `GroupConversationMessage` (lo hace) |
| ¿`OnMessageProduced` cubre el broadcast? | ✅ ya se usa así |
| ¿Pre-turno (decision broadcast) tiene lugar? | ⚠️ no hay hook pre-responder; habría que agregar `OnBeforeRespond` o que el executor lo haga al entrar |

**Veredicto A:** factible y no-breaking, pero el verdadero costo no es el executor
— es que **`GroupConversationContext` hoy NO modela el contexto rico que AngelNaira
necesita** (N-profiles, system block, speaker metadata). Migrar a A obliga a subir
ese contexto al framework o a darle al executor libertad total de construir el
`AgentContext`. Es una capacidad de framework de tamaño medio, **con su propio
example** (un executor con efectos secundarios fakeados — lo que el `ScriptedAgent`
no cubre). Confirma la recomendación: **A después, como ítem separado; B ahora.**

---

## 5. Decisiones que salen de esta verificación

1. **Rail grupo: B es canónico.** A (Orchestration group) queda example-only /
   congelado. Anotar ADR. (No bloquea nada.)
2. **Smell 1 (`GroupTurnResult` ×2):** renombrar el de `Orchestration` →
   `GroupRoundResult` cuando se toque A. Barato, alto retorno de legibilidad.
3. **#5 round-trip de `Mode`:** ✅ **HECHO — y encontró un bug real.**
   `GroupConversationContext.From(...)` y `ToAgentContext()` reenviaban TODOS los
   campos de `AgentContext` **menos `Mode` (#5) y `ParticipantProfileIds`** (este
   último un gap PRE-EXISTENTE del trabajo N-profile que nunca se propagó). Ambos
   se perdían silenciosamente → default. Corregido + 3 tests de regresión
   (`GroupConversationContextRoundTripTests`). Sin esto, una `ITurnPolicy` o un
   agente que leyera `ctx.Mode`/`ctx.ParticipantProfileIds` tras pasar por el
   wrapper habría visto `Normal`/vacío — un "no encaja" que habría aparecido recién
   en runtime durante la migración.
4. **Opción A:** factible; bloqueada por que `GroupConversationContext` no modela
   el contexto rico. Se diseña/implementa como capacidad aparte, con example
   propio, DESPUÉS de la migración B.
5. **Migración AngelNaira:** procede bajo **Opción B** sin deuda nueva de
   framework; los smells de §2 son pre-existentes y no se agravan.

---

## 6. Acción inmediata sugerida (única dentro del framework, antes de AngelNaira)

Cerrar el gap de cobertura del punto 5.3: un test que pruebe que `ConversationMode`
sobrevive el round-trip por `GroupConversationContext`. Si **falla**, encontramos
un "no encaja" real de #5 antes de que la migración dependa de él. Si pasa, #5
queda blindado para la migración.
