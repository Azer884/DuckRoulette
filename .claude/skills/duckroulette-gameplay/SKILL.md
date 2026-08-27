---
name: duckroulette-gameplay
description: Work on DuckRoulette gameplay, player mechanics, match flow, or tutorial without changing multiplayer authority accidentally.
---

# DuckRoulette gameplay

Read the smallest relevant set first:

- Match/round/win/rewards: `Assets/Scripts/GameManager.cs`, `RoundManager.cs`, `PlayerSpawner.cs`, `PlayersManager.cs`.
- Player ability: corresponding file in `Assets/Scripts/Player/` plus its direct caller.
- Tutorial: `Assets/Scripts/Tutorial/TutorialManager.cs` and the matching partial/step controller. It uses the shared networked player mechanics offline; preserve that relationship.

Gameplay contract:

- The server owns gun turn selection, bullet state, player active/dead state, kills, teams, and rewards.
- Owners read input; a client requests an action through its owned RPC; the server validates eligibility, range/target/state, then mutates replicated state.
- Keep gameplay components disabled for non-owners where the existing component does so. Do not turn an owner-authoritative transform/animator into general gameplay authority.
- Treat inspector wiring, tags/layers, animations, NetworkObject configuration, and Input System action names as part of the feature. Inspect the prefab/scene before changing any of them.

Verification: test as host and remote client; cover player spawn, turn timeout, empty/live chamber, death/disconnect, and return-to-lobby for any match-flow change.
