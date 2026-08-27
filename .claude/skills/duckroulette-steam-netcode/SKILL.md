---
name: duckroulette-steam-netcode
description: Work on DuckRoulette Steam lobbies, Facepunch Transport, Netcode RPCs, voice, friends, persistence, or network scene flow.
---

# DuckRoulette Steam + Netcode

Architecture:

- Steamworks owns identity, lobbies, invites/friends, rich presence, Remote Storage, and voice.
- `Lobby/GameNetworkManager.cs` creates/joins Steam lobbies and starts NGO host/client with the Facepunch transport. Clients use the lobby owner's Steam ID as the target.
- `LobbyManager`, `NetworkTransmission`, `SteamFriendManager`, and `LobbySaver` provide lobby UI/state/chat/friends/current-lobby support.
- `GameNetworkManager.StartGame()` uses NGO's scene manager to load `LoadingScreen`; preserve that ordered network-scene flow.
- Voice is `Player/VoiceChat.cs`: owner captures Steam voice, the server forwards it, receiving clients decode/play it. It is not Vivox.
- Steam Cloud holds `Coin.Value` and `cosmeticData.txt`; cosmetics replicate through `NetworkCosmetics` NetworkVariables.

Rules:

- Do not add Relay, Authentication, Cloud Save, or Vivox unless explicitly requested; none are currently integrated.
- For every `ServerRpc(RequireOwnership = false)`, identify the sender from `ServerRpcParams`, validate all referenced IDs and state server-side, and restrict ClientRpc targets when broadcasting is unnecessary.
- Keep the Facepunch Transport and Steam lobby lifecycle coordinated: do not shut down Netcode or leave a lobby from a competing callback without checking the existing teardown path.
- Test host + remote Steam client for lobby creation/join/invite, disconnect, scene synchronization, and the affected RPC path.
