---
name: vfx-linker
description: >-
  Generates Unity C# interface and binding code that connects a Stat/Identity
  change event to visual effects, focusing on Shader integration (e.g., updating
  material properties when Identity changes). Use when integrating Identity
  state changes with Particle System or Shader visual feedback.
---

# VFXLinker

## Instructions
When the agent is asked to "link Identity change to VFX" / "VFX linker" / "shader update on Identity" with:
1) an existing Identity change event or state manager,
2) desired visual behavior based on Identity,
the agent should implement a decoupled event-to-visual binding.

### 1. Discover the event source and visual targets
1. Find where Identity changes are produced (e.g., in `StatManager`, `IdentityResolver`, or a dedicated state component).
2. Locate how the Identity change is currently represented:
   - event/delegate (preferred), or
   - polling via state checks (less preferred).
3. Identify the render targets:
   - For shader mode: `Renderer`/`SkinnedMeshRenderer` and shader/material property strategy.

### 2. Enforce decoupling via interfaces
The agent should define (or adapt) interfaces so the visual binder does not depend on stat/identity internals:
- `IIdentityChangeSource`: exposes `event Action<Identity> IdentityChanged` (or equivalent).
- `IIdentityVisualBinder`: consumes the Identity and applies visual changes.
- (Optional) `IIdentityDebugInfoProvider`: returns extra context for logging.

Prefer passing `Identity` (or a small identity model) into the binder instead of passing whole stat containers.

### 3. Implement shader binding correctly (no Update polling)
The agent should generate a MonoBehaviour/Component that:
1. Subscribes to the Identity change event in `OnEnable`.
2. Unsubscribes in `OnDisable` to avoid leaks and double subscriptions.
3. Updates shader properties using a cached `MaterialPropertyBlock`:
   - cache `Shader.PropertyToID(string)` results
   - reuse a single `MaterialPropertyBlock` instance
   - call `renderer.SetPropertyBlock(block)` only when Identity changes

### 4. Configuration model (prefer ScriptableObject)
To reduce code churn when designers tweak VFX mapping:
1. Add a ScriptableObject config that maps Identity -> shader parameter values.
2. Include:
   - property name(s) (string) and/or type (float/int/color)
   - per-Identity values (e.g., float enum index, color, emission intensity)
3. Validate config at edit time if possible (e.g., warn if property names are empty).

### 5. Shader parameter strategy (keep it scalable)
1. Use numeric indices for identity when possible (e.g., set a single `int/float` property).
2. If multiple properties are needed, centralize them in the config and keep binder logic generic.
3. Document which shader property convention is expected (e.g., `_IdentityIndex`, `_IdentityColor`).

### 6. Unity performance and GC rules
1. Do not allocate per event:
   - avoid creating new lists/strings in the event handler
   - avoid string concatenation loops
2. Keep event handler small and deterministic.
3. Use caching for property IDs and references (renderer, property block, configs).

## Examples
**Scenario A: Single shader float property**
Identity -> set `_IdentityIndex` float to N.
Logs:
`[VFXLinker] Identity=Fighter, _IdentityIndex=2`

**Scenario B: Color/emission based on Identity**
Identity -> set `_IdentityColor` and `_EmissionIntensity`.
Logs include applied values from the config.

