# turnpolicy — el sueño y la grupal como `ITurnPolicy` sobre un solo rail

Prueba de concepto **aislada** (no toca AngelNaira) para la oportunidad **#1** del
review de MiyuAgents: unificar el turn-taking.

## El problema que ataca

Hoy AngelNaira tiene **dos motores de turnos divergentes**:

- `NairaGroupConversationOrchestrator` *envuelve* a `DefaultGroupConversationOrchestrator`
  pero **bypassa** su `SendMessageAsync` y reimplementa la alternancia a mano con un
  `TurnPolicyResolver` aparte y su **propio** `_history`. Resultado: **dos historiales**
  (el del framework queda vacío).
- El **sueño** es *otro* loop a medida más (`DreamService.RunDreamLoopAsync`).

## La tesis (validada acá)

Ambos comportamientos son sólo **políticas de "quién sigue / cuándo cortar"** y caben
en `ITurnPolicy` corriendo sobre el **mismo** `DefaultGroupConversationOrchestrator`,
con **un solo** historial. El loop multi-ronda del framework **es** el loop:
una sola llamada a `SendMessageAsync(seed)` corre el sueño entero. `maxRoundsPerTurn`
es el techo bobo de seguridad; la **policy** es el corte inteligente.

## Qué hay acá

| archivo | rol |
|---|---|
| `Policies.cs` → `DreamTurnPolicy` | sueño: alternancia humano→sub→char→sub→char + **dos cortes** (maxVueltas + repetición estructural por solapamiento de n-gramas) |
| `Policies.cs` → `HybridMentionTurnPolicy` | grupal: un agente por turno humano, mención por nombre o round-robin |
| `TextOverlap.cs` | solapamiento de n-gramas (espejo de `TextSimilarity` de AngelNaira) — el corte anti-repetición vive en la policy |
| `Program.cs` | runner con 3 escenarios + asserts PASS/FAIL |

> El agente determinista sin LLM (`ScriptedAgent`) ya **no** vive acá: se promovió a
> la librería compartida [`MiyuAgents.Testing`](../../testing/MiyuAgents.Testing/),
> que referencian tanto este example como la suite `MiyuAgents.Tests.Unit`.

## Correr

```bash
dotnet run --project Packages/MiyuAgents/examples/turnpolicy
```

Salida esperada: `✅ TODO VERDE`. Tres escenarios:

1. **Sueño 1a** — corta por `maxVueltas (3)`.
2. **Sueño 1b** — el personaje se repite ⇒ corta por `repetición estructural`.
3. **Grupal** — mención a Kori ⇒ Kori; sin mención ⇒ round-robin (Naira, luego Kori),
   todo sobre **un** historial compartido.

## Migración (siguiente paso, sólo si se aprueba)

1. Portar `DreamTurnPolicy` y `HybridMentionTurnPolicy` al framework o a
   `AngelNaira.Infrastructure/GroupConversations`.
2. Reemplazar el `TurnPolicyResolver` + `_history` propio del
   `NairaGroupConversationOrchestrator` por `SendMessageAsync` del rail con la policy
   híbrida → **un solo historial**.
3. Reescribir `DreamService.RunDreamLoopAsync` como `SendMessageAsync(seed)` con
   `DreamTurnPolicy` (el `SubconscienteAgent` y el personaje pasan a ser participantes).
4. Mantener los efectos secundarios (sedimentación oníricá, pulso→deseo, ilustración al
   despertar) como handlers sobre `OnMessageProduced` / post-turno, no dentro del loop.
