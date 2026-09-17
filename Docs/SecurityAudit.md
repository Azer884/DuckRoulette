# DuckRoulette RPC Security Audit

Scope: every `[ServerRpc]` under `Assets/Scripts` (53 total, no `[Rpc(SendTo.Server)]` in use), as of 2026-09-15.
Threat model: a Steam listen server. The host is trusted. Any remote client may run a modified build and send any RPC it can reach, with arbitrary arguments, as often as it likes.
`RequireOwnership = true` (the default) only proves the call came from the object's owner. It does **not** prove it's their turn, that they're in range, or that the arguments are honest.

Pure validation rules live in `Assets/Scripts/Security/RpcValidation.cs` (EditMode-testable, only depends on `Vector3`).

## Summary

| Severity | Found | Fixed | Left as residual |
|---|---|---|---|
| Critical | 2 | 2 | 0 |
| High | 9 | 9 | 0 |
| Medium | 14 | 13 | 1 (`TaskManager.CompleteTaskServerRpc`, no proximity check) |
| Low | 16 | 14 | 2 (`LoadingManager.ReportProgressServerRpc`, `BulletBehavior.SpawnImpactVfxServerRpc`) |
| SAFE | 12 | n/a | n/a |

## Key server-side mechanisms added

- **Shot registry (GameManager).** `ShootServerRpc` now fires only if `TryAuthorizeShot(sender)` passes. That check requires all of: the sender holds the gun, `canShoot` is set, the gun is reloaded, the live chamber is under the hammer, no shot has been fired yet this turn (tracked by a turn serial that `SetGunHolder` bumps), and the origin is within 5 m of the shooter. An authorized shot is stored as `(shooter, origin, direction, time)` for 6.5 s.
- **Validated kills.** `UpdatePlayerStateServerRpc` and `HidingSpot.KillHolderServerRpc` call `TryValidateShotKill`. A kill is accepted only if every condition holds:
  - the victim is alive;
  - the killer is not the victim;
  - the two are not teammates (server `_teams`);
  - the killer has an unconsumed shot whose straight path (`BulletBehavior.Speed` x elapsed time + 1 s slack) passes within 3.5 m of the victim's hitbox centre. For a hiding spot, the tolerance is 3.5 m plus the spot's collider extents.

  Each shot can kill once. Bystander reporting still works because any peer may report the hit; the report is simply no longer trusted on its own.
- **Kill credit.** `ApplyShotKill` awards the kill on the server. `UpdateKillsServerRpc` is now an intentional no-op, and its calls were removed from `DeathTrigger` and `HidingSpot`.
- **Stun.** `Slap.SlapImpactServerRpc` validates range and cooldown, then calls `GameManager.RegisterSlap(attacker, victim)`. `StunPlayerServerRpc` needs at least 3 slaps counted by the server for that pair within 60 s. The attacker and victim must both be alive and within 5 m. Rocks now call the server-only `GameManager.StunPlayer`.

## RPC table

Line numbers are the method declaration after the fix.

| # | RPC (file:line) | Verdict | Exploit (modified client) | Fix |
|---|---|---|---|---|
| 1 | `GameManager.UpdatePlayerStateServerRpc` GameManager.cs:268 | **Critical**, fixed | `UpdatePlayerStateServerRpc(anyVictim, anyKiller)` (RequireOwnership=false) instantly kills any player, any time, and can end the match. | Requires a server-authorized, unconsumed shot by `killer` whose path passes near the victim, plus victim alive and not a teammate (`TryValidateShotKill`). |
| 2 | `Shooting.ShootServerRpc` Shooting.cs:426 | **Critical**, fixed | The owner calls it out of turn, repeatedly, with any `spawnPoint`. That's unlimited bullets from anywhere, and `haveToReload=false` keeps the gun loaded. | `TryAuthorizeShot`: holder, `canShoot`, reloaded, live chamber, one shot per turn, origin within 5 m. `haveToReload` is ignored and zero direction falls back to forward. The shot is registered for hit validation. |
| 3 | `GameManager.UpdateKillsServerRpc` GameManager.cs:885 | **High**, fixed | `UpdateKillsServerRpc(me, 1)` spammed inflates anyone's kills, which also inflates the coin reward computed from them. | Now a no-op. Credit is awarded in `ApplyShotKill` only for validated kills. |
| 4 | `GameManager.StunPlayerServerRpc` GameManager.cs:487 | **High**, fixed | Knock out any player from anywhere, endlessly, which also chain-stuns the gun holder. | Sender = attacker. Both alive, different, within 5 m, and at least 3 server-counted slaps in 60 s. |
| 5 | `Shooting.ReloadServerRpc` Shooting.cs:500 | **High**, fixed | Any player re-rolls `randomBulletPosition` during someone else's turn. The holder re-rolls until the chamber is live. | `GameManager.IsReloadAllowed`: holder only, `canShoot`, not already loaded. |
| 6 | `Shooting.OnHasShotChangedServerRpc` Shooting.cs:491 | **High**, fixed | A non-holder toggles its own `hasShot`. That skips the holder's turn and advances the chamber, and can be repeated. | `GameManager.OnClientShotChanged` ignores anyone but the current holder. |
| 7 | `Ragdoll.EnableServerRpc` Ragdoll.cs:435 | **High**, fixed | `EnableServerRpc(victim, false)` on the victim's Ragdoll disables their CharacterController on every peer, freezing them permanently. | Sender must be the owner, and it always uses `OwnerClientId`. |
| 8 | `Interact.MoveObjectServerRpc` Interact.cs:307 | **High**, fixed | Teleport any spawned NetworkObject (bullets onto victims, hiding spots, cards, rocks) anywhere. | The object must be a `BumBox` held by this player, and the target within 5 m of the player. |
| 9 | `NetworkTransmission.IWishToSendAChatServerRPC` NetworkTransmission.cs:66 | **High**, fixed | Post as any player (`_fromWho`), post red "Server" messages (`isServer`), unbounded length, TMP rich-text tags, flooding. | Author = sender. `isServer` is honoured only for the host. `SanitizeChatMessage` (200 chars, control chars stripped, `<` neutralized). 0.3 s rate limit for non-hosts. |
| 10 | `NetworkTransmission.AddMeToDictionaryServerRPC` NetworkTransmission.cs:92 | **High**, fixed | Spawn lobby characters or list entries for other client ids, repeatedly, with a spoofed name. | clientId = sender. The name is sanitized (64 chars). A second spawn is skipped while this sender's character is alive. `_steamId` is still unverified (see residual risks). |
| 11 | `HidingSpot.KillHolderServerRpc` HidingSpot.cs:145 | **High**, fixed | Force any hider out of any spot at will. Previously combined with an unvalidated kill report. | New `killerId` parameter. Requires `TryValidateShotKill` against the spot's collider, then exits the spot and applies the kill server-side. |
| 12 | `GameManager.EndTeamUpServerRpc` GameManager.cs:1020 | Medium, fixed | Send `EndTeamUp` to any player. Their client drops its team (breaking client-side friendly-fire protection) and outlines reset. | Honoured only if (sender, teamMate) is in server `_teams`. |
| 13 | `GameManager.TeamUpRequestServerRpc` GameManager.cs:892 | Medium, fixed | Spam requests to anyone from anywhere, request yourself, or overwrite another player's pending request to hijack or cancel it. | Target is a different connected player, neither is teamed, within 8 m, 4 s per-requester cooldown. |
| 14 | `HidingSpot.ExitServerRpc` HidingSpot.cs:120 | Medium, fixed | Call `Exit(myId)` on a spot someone else occupies. The spot clears on every peer while the real hider stays invisible, frozen and unshootable. | Requires `IsHeld && holderId == sender`. |
| 15 | `NetworkTransmission.RemoveMeFromDictionaryServerRPC` NetworkTransmission.cs:193 | Medium, fixed | Remove any Steam ID from everyone's lobby list. | Honoured only from the host, which also receives Steam's member-left callback. |
| 16 | `NetworkTransmission.IsTheClientReadyServerRPC` NetworkTransmission.cs:217 | Medium, fixed | Mark other players ready (with coins), letting the host start a match they didn't agree to. | clientId = sender. `haveEoughCoins` is still self-reported. |
| 17 | `CardDeck.SendMsgServerRpc(string)` CardDeck.cs:782 | Medium, fixed | Broadcast fake dealer messages ("X got a Blackjack!") to everyone. | Server-only (sender == `ServerClientId`). All legitimate callers are server logic. |
| 18 | `CardDeck.SendMsgServerRpc(string, ulong)` CardDeck.cs:793 | Medium, fixed | Send fake table messages to a chosen player. | Server-only, same as #17. |
| 19 | `VoiceChat.SendVoiceDataToClientsServerRpc` VoiceChat.cs:109 | Medium, fixed | A bogus `compressedWritten` (negative or larger than the array) makes every receiver throw. Huge arrays are amplified to the whole lobby. | `IsValidVoicePayload` (at most 8192 bytes and at most the array length). Only the used bytes are forwarded. The receiver also guards the bounds and the odd-length PCM loop. |
| 20 | `PlayerPushObject.ColliderHitServerRpc` PlayerPushObject.cs:29 | Medium, fixed | Apply impulses to any rigidbody anywhere, including in-flight bullets on the server. | `hitPoint` within 4 m of the server's view of the player. Direction is derived server-side. Bullets are skipped. |
| 21 | `Slap.SlapImpactServerRpc` Slap.cs:201 | Medium, fixed | Shake or knock any player's camera from anywhere, at any rate, with a fake `attackerPosition`. | Victim is another connected player within 5 m. The attacker is alive and uses the server-side position. Cooldown of `slapCoolDown * 0.5`. Counts toward the stun. |
| 22 | `TaskManager.CompleteTaskServerRpc` TaskManager.cs:260 | Medium, **residual** | Complete your own assigned tasks from anywhere on the map without interacting, which makes you eligible for the gun. | Not fixed: the task objectives register only a `Challenge` asset, with no position the server could check. |
| 23 | `Username.SetPlayerNameServerRpc` Username.cs:25 | Medium, fixed | A name over 29 UTF-8 bytes (including legitimate long or non-Latin Steam names) throws on the server and the name never sets. Also rich-text names. | `TruncateUtf8(SanitizeChatMessage(name, 64), 29)`. |
| 24 | `NetworkCosmetics.ChangeNetVarsServerRpc` NetworkCosmetics.cs:128 | Medium, fixed | Out-of-range or negative indices throw `IndexOutOfRange` in `OnValueChanged` on every peer. | `SanitizeCosmeticIndex` against each item array (invalid becomes 0). |
| 25 | `FootStepScript.SpawnFootstepVfxServerRpc` FootStepScript.cs:119 | Medium, fixed | Every call spawns a replicated NetworkObject anywhere, so flooding degrades the whole session. | Position within 4 m of the player, and at most one every 0.2 s (real steps are at least 0.35 s apart). |
| 26 | `GameManager.TeamUpResponseServerRpc` GameManager.cs:940 | Low, fixed | Already bound to a real pending request, but could form a second team or place the dap VFX/sound anywhere. | Neither player already teamed, within 8 m, and the sound position is clamped to the responder. |
| 27 | `HidingSpot.HideServerRpc` HidingSpot.cs:66 | Low, fixed | Hide in an occupied spot, hide while dead, or hide from across the map. | Spot must be free, hider alive, within 8 m. |
| 28 | `BumBox.PickUpServerRpc` BumBox.cs:157 | Low, fixed | Steal the box out of someone's hands, from anywhere. | Box must be free, sender within 8 m. |
| 29 | `BumBox.ChangeMusicServerRpc` BumBox.cs:91 | Low, fixed | Change the music from anywhere. | Sender within 8 m. |
| 30 | `BumBox.MuteServerRpc` BumBox.cs:230 | Low, fixed | Mute or unmute from anywhere. | Sender within 8 m. |
| 31 | `Shooting.PlayReloadSoundServerRpc` Shooting.cs:556 | Low, fixed | Any client plays gun sounds on any player, at any position, as spam. | Sender = owner, position within 5 m (`IsOwnSoundRequest`). |
| 32 | `Shooting.PlayTriggerSoundServerRpc` Shooting.cs:569 | Low, fixed | Same as #31. | Same as #31. |
| 33 | `Shooting.PlayShootSoundServerRpc` Shooting.cs:582 | Low, fixed | Fake gunshot sounds, which also mislead players about who fired. | Same as #31. |
| 34 | `Shooting.PlayEmptyShotSoundServerRpc` Shooting.cs:595 | Low, fixed | Fake dry-fire sounds. | Same as #31. |
| 35 | `Slap.PlaySlapVfxServerRpc` Slap.cs:233 | Low, fixed | Slap VFX and sound spam anywhere, on any player. | Sender = owner, within 5 m. |
| 36 | `Movement.RequestGroundParryVfxServerRpc` Movement.cs:654 | Low, fixed | VFX spam on any player, anywhere. | Sender = owner, within 5 m. (Remote copies' `OnDisable` used to broadcast this too; those duplicates are now dropped.) |
| 37 | `Movement.RequestRunVfxServerRpc` Movement.cs:910 | Low, fixed | Spawn or despawn run-VFX NetworkObjects on other players. | Sender = owner. |
| 38 | `NetworkTransmission.SendPrivateChatServerRpc` NetworkTransmission.cs:167 | Low, fixed | Unbounded or rich-text whisper payloads. The sender was already derived correctly. | `SanitizeChatMessage` (200 chars), empty messages dropped. |
| 39 | `LoadingScreenController.ReportNameServerRpc` LoadingScreenController.cs:212 | Low, fixed (name) | A name over 61 UTF-8 bytes throws on the server. `steamId` is caller-provided, so it can show someone else's avatar. | `TruncateUtf8(SanitizeChatMessage(name, 64), 61)`. The `steamId` spoof is residual. |
| 40 | `LoadingManager.ReportProgressServerRpc` LoadingManager.cs:115 | Low, **residual** | Report 100% early, which activates the scene before the others finish. | Not fixed. The sender is already derived, and this loading pipeline is unused (PlayerSpawner/LoadingScreenController is the live path). |
| 41 | `BulletBehavior.SpawnImpactVfxServerRpc` BulletBehaivor.cs:69 | Low, **residual** | The bullet's owner can spawn impact VFX and sound at arbitrary positions while the bullet exists (at most 5 s). | Not fixed (cosmetic, owner-only, short-lived). |
| 42 | `CardDeck.EnterGameServerRpc` CardDeck.cs:115 | SAFE | n/a | Sender-derived seat, full-table check. |
| 43 | `CardDeck.ExitGameServerRpc` CardDeck.cs:214 | SAFE | n/a | Sender-derived. |
| 44 | `CardDeck.RequestDrawCardServerRpc` CardDeck.cs:314 | SAFE | n/a | Sender-derived, turn and seat checks, server deals. |
| 45 | `CardDeck.StandServerRpc` CardDeck.cs:565 | SAFE | n/a | Sender-derived, seat check. |
| 46 | `BulletBehavior.DestroyServerRpc` BulletBehaivor.cs:60 | SAFE | n/a | Owner (shooter) only. Can only shorten their own bullet. |
| 47 | `BumBox.DropServerRpc` BumBox.cs:197 | SAFE | n/a | Only the current holder. |
| 48 | `NetworkTransmission.RequestKickServerRpc` NetworkTransmission.cs:139 | SAFE | n/a | Host-only, target must be listed. |
| 49 | `NetworkTransmission.StarGameFeeServerRpc` NetworkTransmission.cs:253 | SAFE | n/a | Owner-only on a server-owned in-scene object, so host-only. (The coin deduction itself is client-side; see residual risks.) |
| 50 | `LoadingScreenController.ReportProgressServerRpc` LoadingScreenController.cs:424 | SAFE | n/a | Sender-derived, clamped, monotonic. |
| 51 | `Rocks.DestroyServerRpc` Rocks.cs:29 | SAFE | n/a | Owner-only, and rocks are server-spawned (server-owned). |
| 52 | `WeatherHandler.HandleWeatherServerRpc` WeatherHandler.cs:54 | SAFE | n/a | Owner-only. `WheaterHandler.prefab` is an in-scene, server-owned object. Re-audit if it is ever put on a player-owned object. |
| 53 | `WeatherHandler.SpawnRocksServerRpc` WeatherHandler.cs:67 | SAFE | n/a | Same as #52. |

## Residual risks (need a redesign, not an RPC patch)

1. **Chamber state is public (High).** `GameManager.bulletPosition` and `randomBulletPosition` are NetworkVariables that every client can read. The client also decides locally whether a pull is live (`Shooting.ExecuteShot`). A modified client always knows whether the next pull fires. The server now refuses a live bullet when the chamber isn't live, but it can't hide the information. The fix is a server-resolved "pull trigger" RPC that returns only the outcome, with the chamber kept private on the server.
2. **Hit detection is peer-reported (Medium).** Kills need a real shot passing within 3.5 m of the victim, but a malicious peer can still turn a near miss into a kill. Closing this needs server-side hit detection (a server raycast or sweep with lag compensation).
3. **Owner-authoritative movement and aim (by design).** `ClientNetworkTransform` allows teleport and speed hacks, and every distance tolerance above is measured against that self-reported position. `targetAim` is client-chosen, so aimbots can't be prevented.
4. **The economy is client-side (Critical if coins or cosmetics ever matter commercially).** Coins (`Coin.Value`) and cosmetic unlocks live in the client's Steam Remote Storage. The end-of-match reward is added locally in `EndGameClientRpc`, the entry fee is deducted locally, and `haveEoughCoins` is self-reported. Any client can edit its own balance or unlocks. The fix is a trusted backend or Steam Inventory Service.
5. **Steam identity isn't verified (Medium).** The `_steamId` sent to `AddMeToDictionaryServerRPC` and `ReportNameServerRpc` is caller-provided, which enables avatar and identity spoofing in the lobby and loading screen. `FacepunchTransport` knows each connection's real SteamId, but it's vendor code and keyed by transport id. The fix is a small accessor plus an NGO-to-transport client id mapping, or Steam auth session tickets.
6. **Task completion has no proximity check (Medium, #22).** Fixing it needs objective positions registered alongside the `Challenge` assets.
7. **Owner-writable NetworkVariables.** `Stats.shotCounter/emptyShots/timeSurvived` (end-screen accuracy and luck), `Shooting.haveGun` (gun visibility) and `hasShot` (the holder can only end their own turn) can all be spoofed. Low.
8. **No global RPC flood protection.** Only the cheap per-RPC rate limits above exist. NGO message floods on other RPCs are still possible (DoS, Low).
9. **The host is fully trusted.** This is inherent to a Steam listen server.

## Behaviour changes to verify (host + remote Steam client)

- Kills: normal shots, point-blank shots, shots on hiding spots, and teammate shots (must not kill). Kill counts and coins on the end screen now come only from validated server-side kills.
- A late bystander report more than 6.5 s after the shot, or more than 3.5 m off the bullet's straight path, is rejected. Watch for missed kills at high ping.
- Reload is allowed once per load and only for the gun holder. Only the holder's trigger pull passes the turn.
- A stun needs 3 validated slaps within 60 s by the same attacker on the same victim. Rocks still stun directly.
- Lobby: `AddMeToDictionaryServerRPC` now always uses the sender id. `PlayerSpawner.AddPlayersClientRpc` used to pass the spawner's `OwnerClientId` (0) from every client on return-to-lobby, so check that each player gets exactly one lobby character.
- In chat, `<` is followed by a zero-width space, so rich-text tags no longer render.
