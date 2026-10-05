# Skill animation source

- Author: Quaternius (Universal Animation Library).
- Official pack: https://quaternius.com/packs/universalanimationlibrary.html
- Download: https://quaternius.itch.io/universal-animation-library
- Package: Universal Animation Library[Standard].zip, free Standard edition, downloaded 2026-10-04. The download page lists the archive update as 2026-06-16.
- License: CC0 1.0 Universal; original `License.txt` is included.
- Original file: `Unity/UAL1_Standard.fbx`, the version without root motion.
- Takes used: `Armature|Sprint_Loop` (frames 0–20, 30 fps) and `Armature|Fixing_Kneeling` (frames 0–156, 30 fps).

The importer uses Humanoid/Create From This Model and Bake Axis Conversion. The sprint take is imported as `Charge_Sprint` with looping and baked root transforms. The kneeling assembly action is imported as `Trap_Assembly`, without looping and with baked root transforms. Cameras, lights and materials are not imported.

`Battle PvP > Skills > Apply Charge Run Animation` copies the baked humanoid clip into `Assets/Remodel/Skills/Animations/SHARED_Charge.anim` without changing its GUID, then configures the existing charge state. The full skill installer uses the same source. The source FBX retains the creator's original file; only the extracted animation is referenced by the player controller.

`Battle PvP > Skills > Apply Skill Interaction Motions` copies the complete kneel, assemble and stand action into `SHARED_Trap.anim`. The animator state retimes it to the Excel `CastSeconds` value. The knife readiness and shared right-arm throw are authored humanoid muscle clips, independent of this source asset.
