# Player Skill Design

## Skill Layout

### Monostat
- Each monostat job has 1 unique skill.

### Strategist
- Strategist has 2 skills.
- Skills:
  - Preset Change
  - Dash

#### Preset Change Bonus
- When changing preset, the bonus is decided by the highest stat in the target preset.
- The cooldown is long, but the skill should be powerful when used with good situational judgment.

##### STR Target Preset
- For 5 seconds:
  - Attack power is increased by `1.2x`.
  - A Fresnel-style aura is shown around the character while the effect is active.

##### AGI Target Preset
- For 4 seconds:
  - Move speed is increased by `15%`.
  - Attack speed is increased by `20%`.

##### CON Target Preset
- Gain shield from the existing preset-change shield rules.
- Shield sources:
  - Shield equal to current HP reduction caused by changing to a lower max HP preset.
  - Shield equal to half of the max HP increase when changing to a higher max HP preset.
  - Additional shield equal to `20%` of the target preset max HP.
- Example:
  - Max HP changes from `200` to `400`.
  - Max HP increase is `200`, so base shield is `100`.
  - Target preset max HP is `400`, so CON bonus shield is `80`.
  - Total shield gained is `180`.
- If max HP changes from `400` to `200`, current HP is clamped down to the new max HP.
  - The reduced current HP amount is gained as shield.
  - The max HP increase shield does not apply because max HP decreased.

##### DEF Target Preset
- Gain invincibility for 2 seconds.

### Polymath
- Polymath has 2 skills.
- Skills:
  - Dash
  - Weapon Swap

#### Weapon Swap Bonus
- After weapon swap:
  - Move speed is increased by `20%` for 3 seconds.
  - Next attack is empowered by `1.3x`.
- The next attack empowerment applies to both melee and ranged attacks.
