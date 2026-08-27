# DuckRoulette

Unity 6.5 URP multiplayer party game. The core loop is Russian-roulette-style: alive players take turns holding a six-chamber gun; a shot ends the round, passes the gun, and can eliminate a player. The last active player wins. Players can also move, interact with objects, team up, slap, use voice chat, earn coins, and equip cosmetics. `Tutorial.unity` reuses the normal player mechanics offline, then gates them by tutorial step.

## Start here

- Scenes: `Lobby` is the Steam lobby/menu; `LoadingScreen` precedes a networked map; `GameScene` is the main match. Other scenes include `Tutorial`, `Skins`, and testing scenes.
- `Assets/Scripts/GameManager.cs` is the server-authoritative match state machine: turn/gun, bullets, alive state, kill/reward calculation, teams, tasks, and exit.
- `RoundManager`, `PlayerSpawner`, `PlayersManager`, and `GameNetworkManager` are the next files to read for rounds, spawning, player lists, and scene/session transitions.
- Player gameplay lives in `Assets/Scripts/Player/` (`Movement`, `Shooting`, `Death`, `Slap`, `Interact`, `TeamUp`, `VoiceChat`, `Ragdoll`).

## Online stack

- Netcode for GameObjects 2.13 + Facepunch Transport (`Assets/Plugins/Steamworks/Runtime/FacepunchTransport.cs`): Steam lobby owner hosts; clients target the host Steam ID.
- `Lobby/GameNetworkManager.cs` owns Steam lobby create/join/list/invite handling, starts host/client, and uses Netcode scene management. `LobbyManager`, `NetworkTransmission`, `SteamFriendManager`, and `LobbySaver` support lobby UI, chat/ready data, friends, and persisted lobby state.
- Steamworks provides lobbies, friends/invites, rich presence, Remote Storage, and voice. `VoiceChat` captures Steam voice on the owner and forwards it server-to-clients. There is no Unity Relay, Unity Authentication, Unity Cloud Save, or Vivox integration here.
- Steam Cloud: `SaveSystem` stores coins as `Coin.Value`; `Cosmetics`/`NetworkCosmetics` store cosmetic indices as `cosmeticData.txt` and replicate selections with NetworkVariables.

## Networking rules

- Keep match decisions server-authoritative. Validate every client-originated ServerRpc using its sender and server-side state; do not trust a caller-provided client ID, position, reward, or target.
- Player movement and animation intentionally use owner-authoritative `ClientNetworkTransform` and `OwnerNetworkAnimator`; do not move security-sensitive gameplay authority to clients.
- Preserve host/client scene flow: `GameNetworkManager.StartGame()` loads `LoadingScreen` through Netcode, then continues to the selected map. Do not replace it with local `SceneManager.LoadScene` calls for network sessions.
- Prefer `NetworkVariable` for durable replicated state and RPCs for validated requests/effects. Guard Netcode callbacks against objects that have not spawned yet.

## Conventions and scope

- Existing code uses singleton access (`Instance`/`instance`), Unity serialization via `[SerializeField]`, and mixed camelCase/PascalCase names. Follow the surrounding file; avoid broad style-only rewrites.
- Inspect prefab/scene references before renaming serialized fields, scripts, tags, layers, animator parameters, or scene names.
- Do not edit generated/vendor/sample content unless the task specifically requires it: `Library/`, `Temp/`, `Logs/`, `obj/`, `Build/`, `.idea/`, `Assets/Plugins/Steamworks/`, `Assets/Samples/`, `Assets/JMO Assets/`, `Assets/IniFileParser/`, `Assets/GifTextures/`, `Assets/Quantum Mana Studio/`.
- For focused guidance, load only the relevant on-demand skill in `.claude/skills/`; do not scan all assets or packages by default.
