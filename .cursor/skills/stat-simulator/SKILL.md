---
name: stat-simulator
description: >-
  Builds a Unity test tool (typically an EditorWindow) that takes the current stat
  configuration as input, determines the resulting Identity, and prints predicted
  damage to the Unity Console log. Use when implementing or debugging Identity
  resolution and damage prediction for Stat/Identity systems.
---

# StatSimulator

## Instructions
When the agent is asked to create or update a "stat simulator" / "stat test tool" that:
1) accepts stat inputs (e.g., HP/ATK/DEF/identity-relevant stats),
2) determines an `Identity`,
3) predicts expected damage,
4) logs the result,
the agent should follow this workflow.

### 1. Discover the existing project model
1. Identify the types that represent stats and Identity (e.g., `StatContainer`, `Identity`, `IdentityType`, `StatManager`).
2. Identify the existing damage formula entry point(s) (e.g., `DamageCalculator`, `AttackData` usage, `StatCalculator`, etc.).
3. Determine whether Identity resolution is rule-based, threshold-based, or derived from a composed score.

### 2. Enforce data/logic separation
1. Keep the simulator UI as a thin layer (EditorWindow only).
2. Move resolution logic into pure C# services (no Unity editor UI dependencies).
3. Prefer ScriptableObject configs for simulator presets, rule sets, or input parameter definitions.

### 3. Define core interfaces (for extensibility)
The agent should introduce (or adapt) interfaces like:
- `IIdentityResolver`: maps stat inputs to an `Identity` (and optionally provides reasoning details).
- `IDamagePredictor`: computes predicted damage given attack context + resolved Identity + stat inputs.
- `IStatInputSource`: provides simulator input values to the services (UI adapter can implement this).

If the project already has similar abstractions, adapt by reusing those interfaces to avoid duplication.

### 4. Implement event-free simulation (Editor tool)
Because the simulator is invoked by user action, the agent should avoid `Update()`:
1. Create a Unity `EditorWindow` (or a similar Editor tool) with serialized fields for the stat inputs.
2. Add a "Simulate" button.
3. On button click:
   - call `IIdentityResolver` to get `Identity` (+ optional debug/trace info),
   - call `IDamagePredictor` to compute damage,
   - log a concise breakdown to the Console.

### 5. Logging contract (make logs actionable)
The agent should log at least:
- resolved `Identity`
- main rule/threshold that caused the identity (or top scoring inputs)
- predicted damage total
- key intermediate values (e.g., raw damage, multipliers, defense reduction)

To reduce GC during repeated clicks:
- reuse a `StringBuilder` if building multi-line messages
- avoid string concatenation in tight loops

### 6. Unity optimization & lifecycle rules
1. UI should not allocate large temporary lists per click; preallocate where reasonable.
2. If the simulator uses ScriptableObjects, cache references in `OnEnable` and release nothing explicitly (Unity handles it).
3. Keep the simulator deterministic: no random elements unless there is an explicit seed in the simulator inputs.

## Examples
**Scenario A: Identity threshold rules**
Input: stats -> `IIdentityResolver` returns `Identity.Fighter` because score >= threshold.
Output logs:
`[StatSimulator] Identity=Fighter, Threshold=...`
`[StatSimulator] Damage=... (raw=..., mult=..., def=...)`

**Scenario B: Derived Identity from composed stats**
Input: multiple stat weights -> resolver selects the highest matching rule.
Output logs include the winning rule name and the ranked top candidates.

