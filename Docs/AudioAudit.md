# DuckRoulette Audio Audit

Date: 2026-09-15. Unity 6000.5.4f1, URP. Active build target: StandaloneWindows64.
Scope: read-only check. No project files were modified. This document is the only output.

Method: followed `unity:optimize-audio` (pre-flight, assessment, 4A-4E diagnostics) and `unity:audio-setup-mixers` (mixer inventory, per-source routing classification). Evidence was gathered from:
- `.meta` files, and the `.prefab`, `.unity` and `.mixer` YAML
- read-only Editor `eval` calls through the Unity CLI (imported clip `loadType`, channels and frequency, Standalone overrides, output sample rate, DSP buffer)
- `ffprobe`/`ffmpeg` on the source files (length, channels, rate, decoded-PCM hashes, silence detection)
- source code reading

Nothing was profiled at runtime. The editor was not put in play mode.

Environment facts:
- Output sample rate 48000 Hz. DSP buffer 1024 x 4 (Best Performance). `ProjectSettings/AudioManager.asset`: Real Voices 32, Virtual Voices 512, Default Speaker Mode Stereo, no spatializer plugin.
- One mixer: `Assets/SFX/AudioMixer.mixer`, with groups Master > {Music, SFX > Dap, VC}. Group depth is 3, which is within the skill's limit. Exposed parameters: `MasterVolume`, `MusicVolume`, `SFXVolume`, `VCVolume`. Snapshots: `Unpaused` (start) and `Paused`.
- Audio singletons (`SFXManager`, `MusicManager`, `SettingsManager`) live only in `Assets/Scenes/Loading.unity` (build index 0) and persist with `DontDestroyOnLoad`.
- Vendor folders contribute no audio clips. `Assets/JMO Assets`, `Assets/Plugins` and `Assets/Samples` contain 0 wav/mp3/ogg files. The GameScene rain loop sits on a JMO-derived object (`CFXR4 Rain Falling`), but it uses the project's own `Assets/SFX/Rain.mp3`. Facepunch Steamworks supplies the voice codec only.

---

## Summary

Counts: **High 5, Medium 10, Low 10** (25 findings).

| ID | Sev | Finding | Primary location |
|----|-----|---------|------------------|
| H1 | High | 17 core-moment clips are unassigned. Death, stun, bullet impact, turn hand-off, round start, shot-clock ticks, victory/defeat, coin, task complete and all UI sounds are silent even though the code calls them. | `Assets/Scenes/Loading.unity:1055-1059, 1079-1094` |
| H2 | High | The voice-chat AudioSource is 2D (spatialBlend 0). Proximity voice has no direction, and slap pain SFX share the same 2D voice source. | `Assets/Prefabs/Player.prefab:4398, 4450`; `Player/SFXHandler.cs:24` |
| H3 | High | Voice is decoded at `SteamUser.SampleRate` (never set) but played through a clip created at `SteamUser.OptimalSampleRate`. If they differ, voice is pitch-shifted and latency keeps growing. | `Player/VoiceChat.cs:66, 75, 161` |
| H4 | High | Short, frequently played SFX (footstep, slap, both daps) are Streaming at quality 100 on the Standalone override. All 37 clips have Preload Audio Data off, so the first gunshot loads from disk. | `.meta` of `WalkSFX.mp3`, `Slap.mp3`, `bad dap.mp3`, `perrfect dap.wav` |
| H5 | High | Gunshot and death one-shots use default 3D settings (Logarithmic, min 1 m, max 500 m): about -20 dB at 10 m. Voice also ducks the whole SFX bus through a 0 dB sidechain send, so the key lobby-wide events are easy to miss. | `SFXManager.cs:159-169`; `AudioMixer.mixer:314-327` |
| M1 | Med | Footsteps of every player play 2D (spatialBlend 0) on every peer. They are audible map-wide with no position, including remote players. | `Player.prefab:14855, 14907`; `FootStepScript.cs:65-93` |
| M2 | Med | The voice ring buffer has no jitter buffer, no latency cap and no overflow guard. There is an audio-thread/main-thread data race and an off-by-one read. The RPC is reliable (head-of-line blocking), and the owner uploads the whole MemoryStream capacity. | `Player/VoiceChat.cs:95-97, 169-205` |
| M3 | Med | Voice gain is x4 with no clamp, which hard-clips. `audioSource.volume = 2.0f` is a no-op that is overwritten every frame. | `Player/VoiceChat.cs:76, 192-197` |
| M4 | Med | Proximity falloff is computed from the local player body, not the AudioListener (wrong in spectate or hiding). It is linear, stacks with the source settings, and toggles per-player reverb and low-pass on the shared voice/pain source. | `Player/VoiceChatRaycast.cs:47-86, 146-156` |
| M5 | Med | Mixer sidechains: the always-on GameScene rain (on SFX) keeps Music and the Boombox ducked for the whole match. Voice ducks SFX, including gunshots. | `AudioMixer.mixer:286-359`; `GameScene.unity:22286-22289` |
| M6 | Med | `SFXManager.PlayAt` creates and destroys a GameObject plus AudioSource per sound, with no pooling or priority. The tail is cut when pitch < 1. | `SFXManager.cs:155-172` |
| M7 | Med | Gun and slap sound RPCs are not rate-limited or checked against the gun holder. A modded owner can spam or fake reload, trigger and blank sounds on every peer. The owner's own clicks wait a server round trip. | `Player/Shooting.cs:510-617`; `Player/Slap.cs:221-250` |
| M8 | Med | `Reload.MP3` has 1.38 s of leading silence (7.44 s total, mostly silence), so the reload cue lags the animation. | `Assets/SFX/Gun/Reload.MP3` |
| M9 | Med | The MusicManager crossfade ratchets volume down when scenes load back to back, restarts the track on LoadingScreen/Tutorial/Error, and plays `hap.mp3` alongside the Boombox's default `hap.mp3`. | `MusicManager.cs:44-80`; `Bumbox.prefab:152` |
| M10 | Med | Possibly two active AudioListeners in GameScene (the scene `Camera` plus the local player `Camera`). | `GameScene.unity:992`; `Player.prefab:20060` |
| L1 | Low | Boombox auto-plays a Streaming `hap.mp3` on spawn with no time sync. Mute is a toggle ClientRpc (desyncs). Track change has no cooldown. | `Bumbox.prefab:150-165`; `BumBox.cs:91-99, 240-254` |
| L2 | Low | 16 stereo one-shots played fully 3D are not Force To Mono. | gun, slapped, cardboard and gun-spin `.meta` files |
| L3 | Low | Every source is lossy MP3, apart from 4 WAVs (the 3 TutoBOT files and `perrfect dap.wav`), and ships re-encoded to Vorbis. Music Standalone override quality is 1.0. `Rain.mp3` is 468 s with Load In Background off. | `Assets/Music/*.meta`, `Assets/SFX/Rain.mp3.meta` |
| L4 | Low | Probable duplicate `Lobby.mp3`/`LobbyMusic.mp3` (both are in the Boombox playlist). `GunClick2.mp3` is corrupt. 9 clips are unreferenced. | `Assets/Music`, `Assets/SFX` |
| L5 | Low | No UI mixer group: UI goes through SFX, so the Effects slider and voice ducking hit UI. The slider can pass `Log10(0)` = -Infinity. The listener-wait coroutine is dead code. | `SFXManager.cs:98-101`; `InpToSlider.cs:87, 116`; `SettingsManager.cs:199-218` |
| L6 | Low | All sources are priority 128. The 8 voice sources loop constantly (the local copy plays silence). About 11 voices are always busy out of 32 real voices, so voice can be stolen by SFX under load. | `Player.prefab:4398`; `VoiceChat.cs:75-78`; `AudioManager.asset` |
| L7 | Low | SFX Reverb runs on the Dap group. Three Duck Volume units are always active. | `AudioMixer.mixer:83-124, 395-414` |
| L8 | Low | Dead or unused audio code: `gunSpinClip` is never played, `NetworkOneShotAudio` is unused (and would spawn a NetworkObject per sound), and the dap `clientRpcParams` is built but not passed. | `SFXManager.cs:53`; `Player/NetworkOneShotAudio.cs`; `GameManager.cs:970-978` |
| L9 | Low | Double pain sound on the slap that stuns: `SFXHandler.PainSound` fires from both the slap event and `Ragdoll.EnableRagdoll`. | `Player/Slap.cs:312`; `Player/Ragdoll.cs:232-235` |
| L10 | Low | The voice relay forwards every frame to all clients regardless of distance or alive state, and has no per-sender rate limit (size check only). | `Player/VoiceChat.cs:108-139` |

What is already correct (verified):
- Every AudioSource found in the shipped scenes and prefabs is routed to a mixer group. There are no unrouted sources.
- The four exposed parameters exist and their names match `SettingsManager.ApplyAudioSettings` and `InpToSlider`.
- Each networked sound path plays exactly once per peer, including on the host (see section 4).
- Long music and ambience are Streaming, not Decompress On Load.
- No clip uses PCM or Decompress On Load, so there is no uncompressed memory bloat.
- Remote players' cameras (and their AudioListener) are deactivated (`Movement.cs:224`).

---

## 1. AudioClip inventory

Load type values: DOL = Decompress On Load, CIM = Compressed In Memory, STR = Streaming. All clips have `normalize: 1`, `ambisonic: 0`, no sample-rate override, and `preloadAudioData: 0`. "Std override" is the `Standalone` platform override, which applies to the shipping target.
- **Length, channels and rate** come from ffprobe on the source files. For the five clips spot-checked through Editor `eval` (Shot, WalkSFX, Slap, perrfect dap, hap), imported length, channels and load type matched.
- **Ch (import)** is the channel count after Force To Mono.

| Path | Src | Length | Src ch / Hz | Default load / fmt / q | Std override | Mono | BG load | Ch (import) | Used by | Flags |
|------|-----|--------|-------------|------------------------|--------------|------|---------|-------------|---------|-------|
| Assets/Music/AFRO 2.mp3 | mp3 320k | 96.5 s | 2 / 44100 | STR / Vorbis / 0.5 | none | no | no | 2 | Bumbox playlist, Tutorial Boombox override | mp3 source |
| Assets/Music/Lobby.mp3 | mp3 192k | 149.4 s | 2 / 44100 | STR / Vorbis / 0.5 | STR / Vorbis / **1.0** | no | yes | 2 | Bumbox playlist | probable duplicate of LobbyMusic; q 1.0 |
| Assets/Music/LobbyMusic.mp3 | mp3 192k | 149.4 s | 2 / 44100 | STR / Vorbis / 0.5 | STR / Vorbis / **1.0** | no | yes | 2 | MusicManager[0] (Lobby), Bumbox playlist | q 1.0 |
| Assets/Music/hap.mp3 | mp3 320k | 142.4 s | 2 / 44100 | STR / Vorbis / 0.5 | STR / Vorbis / **1.0** | no | yes | 2 | MusicManager[1] (GameScene), Bumbox default clip + playlist | q 1.0; played twice at once in GameScene (M9) |
| Assets/SFX/Rain.mp3 | mp3 256k | **467.8 s** (15.0 MB) | 2 / 44100 | STR / Vorbis / 0.5 | none | no | **no** | 2 | GameScene `CFXR4 Rain Falling` (2D loop, SFX group) | BG load off; feeds the Music ducker (M5) |
| Assets/SFX/Gun/Shot.MP3 | mp3 192k | 1.07 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | **no** | no | 2 | `SFXManager.shootClip` (3D PlayAt) | stereo 3D; preload off |
| Assets/SFX/Gun/EmptyGunShot.MP3 | mp3 192k | 1.07 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | **no** | no | 2 | `emptyShotClip` (3D) | stereo 3D; same size and length as Shot.MP3 but PCM differs |
| Assets/SFX/Gun/Trigger.MP3 | mp3 192k | 0.57 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | **no** | no | 2 | `triggerClip` (3D) | stereo 3D |
| Assets/SFX/Gun/Reload.MP3 | mp3 192k | **7.44 s** | 2 / 44100 | CIM / Vorbis / 0.7 | none | **no** | no | 2 | `reloadClip` (3D) | 1.38 s leading silence (M8); stereo 3D |
| Assets/SFX/GunSpinAfterShot.mp3 | mp3 128k | 0.55 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | **no** | no | 2 | `gunSpinAfterShotClip` (3D, on blanks) | stereo 3D |
| Assets/SFX/GunSpinning.mp3 | mp3 172k | 1.08 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | no | no | 2 | `gunSpinClip` (assigned, never played) | dead (L8) |
| Assets/SFX/Slap.mp3 | mp3 286k | 1.61 s | 2 / **48000** | CIM / Vorbis / 0.7 | **STR** / Vorbis / **1.0** | yes | no | 1 | `SFXManager.slapClip`, Player `Hand_R` source | **Streaming short SFX (H4)** |
| Assets/SFX/WalkSFX.mp3 | mp3 249k | 0.89 s | 2 / 44100 | CIM / Vorbis / 0.7 | **STR** / Vorbis / **1.0** | yes | no | 1 | `footstepClips[0]` | **Streaming footstep (H4)** |
| Assets/SFX/WalkSFX1.mp3 | mp3 530k | 0.16 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | yes | no | 1 | `footstepClips[1]` | only 2 footstep variants, 1 Streaming |
| Assets/SFX/bad dap.mp3 | mp3 320k | 1.91 s | 2 / 44100 | CIM / Vorbis / 0.7 | **STR** / Vorbis / **1.0** | yes | no | 1 | `dapSound` | Streaming short SFX; 1.63 s trailing silence |
| Assets/SFX/perrfect dap.wav | wav PCM16 | 0.94 s | 2 / 44100 | CIM / Vorbis / 0.7 | **STR** / Vorbis / **1.0** | yes | no | 1 | `perfectDapSound` | Streaming short SFX |
| Assets/SFX/DuckGettingSlapped (1..8).mp3 (8 files) | mp3 128k | 0.55 s each | 2 / 44100 | CIM / Vorbis / 0.7 | none | **no** | no | 2 | `slapPainClips` via SFXHandler (2D voice source) | stereo; routed through VC (H2) |
| Assets/SFX/CardBoardHidding.mp3 | mp3 291k | 0.29 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | **no** | no | 2 | `hideEnterClip` (3D) | stereo 3D |
| Assets/SFX/CardboardPopping.mp3 | mp3 128k | 1.07 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | **no** | no | 2 | `hideExitClip` (3D) | stereo 3D |
| Assets/SFX/TutoBOT.wav | wav PCM16 | 5.18 s (914 KB) | 2 / 44100 | CIM / Vorbis / 0.7 | none | no | no | 2 | Tutorial TutoBot | stereo 3D source; tutorial only |
| Assets/SFX/TutoBOT narrator.wav | wav PCM16 | 5.15 s (908 KB) | 2 / 44100 | CIM / Vorbis / 0.7 | none | no | no | 2 | Tutorial | dialogue at q 0.7 is fine |
| Assets/SFX/TutoBOT door.wav | wav PCM16 | 1.42 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | no | no | 2 | Tutorial doors (6 sources, 3D) | stereo 3D |
| Assets/SFX/GunClick1.mp3 | mp3 | 0.16 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | no | no | - | unreferenced | unused |
| Assets/SFX/GunClick2.mp3 | mp3 | ? | ? | CIM / Vorbis / 0.7 | none | no | no | - | unreferenced | **ffprobe: "Invalid data found"**, corrupt |
| Assets/SFX/GunEmptyShot.mp3 | mp3 | 0.36 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | no | no | - | unreferenced | unused (could be a better blank click than EmptyGunShot.MP3) |
| Assets/SFX/GunLoadBullet.mp3 | mp3 | 1.07 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | no | no | - | unreferenced | unused (a candidate reload replacement) |
| Assets/SFX/GunShot.mp3 | mp3 | 2.09 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | no | no | - | unreferenced | unused |
| Assets/SFX/RunSFX.mp3 | mp3 | 0.55 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | yes | no | - | unreferenced | unused |
| Assets/SFX/WalkSFX2.mp3 | mp3 | 0.16 s | 2 / 44100 | CIM / Vorbis / 0.7 | none | yes | no | - | unreferenced | unused footstep variant |
| Assets/typewriter-sound-effect-312919.mp3 | mp3 | 72.9 s (2.3 MB) | 2 / 48000 | STR / Vorbis / 0.5 | STR / Vorbis / 1.0 | yes | yes | - | unreferenced | loose in Assets root, unused |
| Assets/wooden-door-creaking-102413.mp3 | mp3 | 3.46 s | 2 / **24000** | CIM / Vorbis / 0.7 | STR / Vorbis / 1.0 | yes | no | - | unreferenced | loose in Assets root, unused, 24 kHz source |

Checklist results:
- Long music set to Decompress On Load: **none**.
- Short SFX set to Streaming: **4 in use** (Slap, WalkSFX, bad dap, perrfect dap), plus 1 unused (wooden door). See H4.
- Stereo 3D one-shots not forced to mono: **16 clips** (L2).
- Uncompressed PCM on big files: **none**. All clips are Vorbis.
- MP3 sources: **33 of 37** (L3).
- Duplicates: `Lobby.mp3`/`LobbyMusic.mp3` share an identical byte size (3,585,518) and duration (149.394 s), but their decoded PCM hashes differ. This is probably the same track encoded twice; confirm by ear (L4). No other exact duplicates: the eight `DuckGettingSlapped` files, `WalkSFX1`/`WalkSFX2`, and `Shot`/`EmptyGunShot` all decode to different PCM.

---

## 2. AudioMixer, routing and volume settings

### Mixer topology (`Assets/SFX/AudioMixer.mixer`)

| Group | Parent | Exposed volume | Effects (in order) |
|-------|--------|----------------|--------------------|
| Master | - | `MasterVolume` | Attenuation |
| Music | Master | `MusicVolume` | Lowpass (22000 Hz Unpaused / 365 Hz Paused), Attenuation, **Duck Volume** (threshold -53.3 dB) |
| SFX | Master | `SFXVolume` | Attenuation, **Send -> Music ducker** (0 dB), **Duck Volume** (threshold -45.4 dB) |
| Dap | SFX | - | Attenuation, **SFX Reverb** |
| VC | Master | `VCVolume` | Attenuation, **Send -> Music ducker** (0 dB), **Send -> SFX ducker** (0 dB) |

There is no UI group and no Ambience group.

### Source routing (audio-setup-mixers Step 2 classification)

| Source (asset / GameObject) | Plays | Current group | Proposed group | Note |
|-----------------------------|-------|---------------|----------------|------|
| Loading.unity `MusicManager` | LobbyMusic / hap | Music | Music | ok |
| Loading.unity `SFXManager.uiSource` (runtime AddComponent) | UI stingers, clicks | SFX | **UI** (new) | the Effects slider and voice ducking currently hit UI |
| `SFXManager.PlayAt` runtime one-shots | gun, hide, impact, death | SFX | SFX | ok |
| `SFXManager.PlayAt` dap one-shots | dap | Dap | Dap | ok |
| Player.prefab `Player` (voice) | Steam voice, **and slap pain** via SFXHandler | VC | VC (voice only); pain -> SFX | shared source (H2) |
| Player.prefab `LegsSFX` | footsteps | SFX | SFX (or a Foley sub-group) | 2D (M1) |
| Player.prefab `Hand_R` | Slap.mp3 fallback | SFX | SFX | ok |
| Bumbox.prefab `Bumbox` | hap / playlist | Music | **Diegetic Music** (child of SFX or its own group) | ducked by rain; the Music slider mutes a world object |
| GameScene `CFXR4 Rain Falling` | Rain.mp3 | SFX | **Ambience** (new, with no sidechain send) | drives the music ducker (M5) |
| Tutorial doors x6, TutoBot, Map, Hand_R, LegsSFX | tutorial SFX | SFX | SFX | ok |
| Tutorial `Audio` / `Player` | voice | VC | VC | ok |

No source was left unrouted. Groups to add in the Audio Mixer window if you accept the proposal: `UI` under Master, `Ambience` under Master, and optionally `WorldMusic`. No routing was applied; this was a check only.

### How volume is applied

- **Path:** `SettingsManager.ApplyAudioSettings()` (`SettingsManager.cs:185-197`) converts linear 0-1 to dB (`Log10(clamp(v, 0.0001, 1)) * 20`) and calls `AudioMixer.SetFloat` on `MasterVolume`, `MusicVolume`, `SFXVolume` and `VCVolume`. The settings UI (`InpToSlider.cs`) writes the same exposed parameters live and persists them to `Settings.ini` (`EffectsVolume`/`VoiceChatVolume` key mapping at `InpToSlider.cs:19-25`).
- **Not used:** `AudioListener.volume`, and no per-source volume scaling from settings.
- **Coverage:** every source is under Master, so Master reaches everything.
  - Music reaches MusicManager and the Boombox.
  - Effects reaches all SFX, UI, dap, footsteps, rain and slap pain.
  - Voice (`VCVolume`) reaches voice chat, and also the slap pain sounds, because they share the voice source (H2).
  - The Boombox is controlled by Music, not Effects.
- **Script volume writes that bypass settings (they multiply under the mixer, so they are not a coverage gap):**
  - `VoiceChatRaycast` overwrites the voice source volume every frame.
  - `MusicManager` fades `musicSource.volume`, which can ratchet (M9).
- **Snapshot interaction:** exposed parameters set by `SetFloat` are removed from snapshot control, so the Pause/Unpause transitions (`MusicManager.cs:82-92`) do not reset user volumes. Correct.

---

## 3. Spatial audio

| Event | How it plays | spatialBlend | Rolloff / min / max | Doppler | Assessment |
|-------|--------------|--------------|---------------------|---------|------------|
| Gunshot, blank, trigger, reload | `SFXManager.PlayAt` new source | 1 | Logarithmic / 1 / 500 (defaults) | 1 | too quiet at lobby distances (H5) |
| Death, body impact, stun | `PlayAt` | 1 | Log / 1 / 500 | 1 | clips missing (H1); same distance issue |
| Slap | `PlayAt` | 1 | Log / 1 / 500 | 1 | ok for a close-range event |
| Slap pain | `SFXHandler` -> voice source | **0** | n/a | - | 2D, on VC (H2) |
| Footsteps | `LegsSFX.PlayOneShot` | **0** | Log / 1 / 500 | 1 | 2D map-wide (M1) |
| Boombox | `Bumbox` source | 1 | Log / **5** / 500 | 0 | reasonable (min 5 m); ducked by rain (M5) |
| Voice | `Player` source + script volume | **0** | script: linear `1 - d/25`, 0 beyond 25 m; walls x0.5 plus LPF/reverb | 1 | no direction (H2); wrong reference point (M4) |
| Rain | scene source | 0 | - | - | fine as a 2D bed |

**Can the whole lobby hear the important events?** Not reliably.
- Logarithmic rolloff with min distance 1 m gives about -6 dB at 2 m, -14 dB at 5 m, -20 dB at 10 m and -26 dB at 20 m. The SFX-bus ducker adds more reduction whenever anyone talks.
- Death currently has no clip at all.
- The gunshot is the signature moment of the game and should be clearly audible to everyone in the arena.

**Does proximity voice attenuate correctly?** Volume does fall off, but only by a script. The source is 2D, so there is no panning or HRTF cue. The distance is measured from the listening player's body, not the camera or AudioListener. The Tutorial copy of the rig (`Tutorial.unity`, `Audio` source) is 3D, so the two setups disagree.

---

## 4. Networking of audio

### Exactly-once per peer (verified by code path)

| Sound | Trigger path | Plays on | Host double-play? |
|-------|--------------|----------|-------------------|
| Gunshot | Owner `ShootServerRpc` -> server `PlayShootSoundClientRpc` (`Shooting.cs:472, 589`) | every peer once | no |
| Reload, trigger, blank + chamber spin | Owner `Play*SoundServerRpc` -> `Play*SoundClientRpc` (`Shooting.cs:555-608`) | every peer once, **including the owner after a round trip** | no |
| Slap | Owner `PlaySlapVfxServerRpc` -> `PlaySlapSoundClientRpc` (`Slap.cs:233-250`) | every peer once | no |
| Death, stun | Server `ClientRpc` (`GameManager.cs:457-461, 555-558`) | every peer once | no |
| Bullet impact | Owner `SpawnImpactVfxServerRpc` -> ClientRpc (`BulletBehaivor.cs:69-80`) | every peer once | no |
| Dap | Server `PlayDapSoundClientRpc` (`GameManager.cs:978, 1060`) | every peer once (the targeted `clientRpcParams` is unused, L8) | no |
| Hide enter/exit | Existing broadcast RPCs (`HidingSpot.cs:430-439`) | every peer once | no |
| Round start | `NetworkVariable.OnValueChanged` (`RoundManager.cs:45-52`) | every peer once | no |
| Turn start | `HandleGunHolderChanged`, local holder only (`GameManager.cs:799-810`) | new holder only | no |
| Victory/defeat, coin, task complete | local or targeted ClientRpc | the local player only | no |
| Footsteps | Replicated `isWalking` + local cooldown (`FootStepScript.cs:58-78`) | every peer locally | no |
| Voice | Owner ServerRpc -> ClientRpc to all but the sender (`VoiceChat.cs:108-139`) | each other peer once | no |

Exception: the slap that triggers a stun plays the pain sound twice on each peer (L9).

### Can clients trigger sounds for everyone, and is it rate-limited?

| RPC | Validation (after the security pass) | Rate limit | Gap |
|-----|--------------------------------------|------------|-----|
| `Shooting.Play{Reload,Trigger,Shoot,EmptyShot}SoundServerRpc` | sender == owner, position within 5 m | **none** | no gun-holder check and no cooldown (M7) |
| `Slap.PlaySlapVfxServerRpc` | sender == owner, within range | **none** (the separate `SlapImpactServerRpc` has one) | spam (M7) |
| `BulletBehavior.SpawnImpactVfxServerRpc` | owner only | none | SecurityAudit #41 residual |
| `BumBox.ChangeMusicServerRpc` / `MuteServerRpc` | within 8 m | **none** | restart or pause music for all peers at frame rate (L1) |
| `GameManager.TeamUpResponseServerRpc` (dap) | validated, 4 s requester cooldown | yes | ok |
| `VoiceChat.SendVoiceDataToClientsServerRpc` | payload <= 8192 bytes | none | relayed to all regardless of distance (L10) |

### Pooling

None. `SFXManager.PlayAt` allocates `new GameObject` + `AddComponent<AudioSource>` + `Destroy(obj, clip.length)` on every call and on every peer (M6). A typical turn produces reload + trigger + blank + spin = 4 allocations per peer. Add slaps, daps and impacts on top.

---

## 5. Voice chat pipeline (`Player/VoiceChat.cs`, `Player/VoiceChatRaycast.cs`)

| Aspect | Current implementation | Issue |
|--------|------------------------|-------|
| Capture | `SteamUser.VoiceRecord` gated by push-to-talk; `ReadVoiceData(stream)` every frame (`:89-97`) | ok. Sends `stream.GetBuffer()`, the whole backing array, not `compressedWritten` bytes. The server trims it, but the upload is wasted (M2) |
| Transport | `[ServerRpc]` -> `[ClientRpc]`, NGO default **reliable sequenced** delivery | a single lost packet stalls all later voice until it is retransmitted (M2) |
| Sample rate | clip created at `SteamUser.OptimalSampleRate` (`:66, 75`) | decode uses `SteamUser.SampleRate`, which the DLL exposes as `get_SampleRate`/`set_SampleRate` and the project never sets (H3) |
| Decompress | `SteamUser.DecompressVoice(input, compressedWritten, output)` -> 16-bit mono PCM (`:161`) | output rate as above |
| Clip / streaming | `AudioClip.Create("VoiceData", rate*5, 1, rate, stream: true, OnAudioRead)`, `loop = true`, `Play()` in `Start` on every player copy (`:68-78`) | the local player's copy also streams silence forever, a permanent voice (L6) |
| Buffer | 5 s float ring buffer. Main thread writes (`WriteToClip`, `:190-205`); the audio thread reads (`OnAudioRead`, `:169-188`); a shared `int playbackBuffer` is changed from both threads | not thread-safe; no overflow clamp (`playbackBuffer` can exceed capacity and replay stale audio); `dataPosition` pre-increments, so reads start one sample after writes |
| Gain | x4 float gain with no clamp (`:192-197`) | clipping (M3) |
| Latency | no pre-roll or jitter buffer; underruns insert silence, and the next burst plays later with no catch-up or drop policy | delay accumulates over a session (M2). Not measured |
| Missing packet | with reliable delivery nothing is ever missing, but delivery stalls. With the buffer empty, `OnAudioRead` outputs zeros (a gap); there is no concealment | combine an unreliable channel with a small jitter buffer |
| Proximity | owner-side `VoiceChatRaycast`: linear falloff over 25 m, 3 raycasts per remote player per frame, low-pass/reverb filters toggled on the remote player root | 2D source (H2); body-based distance (M4) |

---

## 6. Missing audio feedback on core moments (cross-checked with `Docs/GDD.md`)

| Moment | Code hook | Clip assigned? | Status | GDD reference |
|--------|-----------|----------------|--------|---------------|
| Gun hand-off / turn start | `GameManager.cs:807-810` `turnStartClip` | **no** (`Loading.unity:1085`) | **silent** | GDD §2 "Hand-off feedback plays only for the new holder" |
| Round start | `RoundManager.cs:51` | **no** (`:1088`) | **silent** | - |
| Shot-clock tick / urgent tick | `ShotClockUI.cs:106-108` | **no** (`:1089-1090`) | **silent** | GDD HUD: "per-second tick SFX" and "urgent ticks at 5 s or less" |
| Blank click | `Shooting.cs:602-608` | yes (EmptyGunShot + GunSpinAfterShot) | ok; confirm EmptyGunShot really is a dry click (same size and length as Shot.MP3) | GDD §1 "A blank clicks" |
| Live shot | `Shooting.cs:589-592` | yes | ok, but quiet at range (H5) | - |
| Reload | `Shooting.cs:563-566` | yes | plays about 1.4 s late (M8) | - |
| Trigger/cock | `Shooting.cs:576-579` | yes | ok | - |
| Death / elimination | `GameManager.cs:457-461` | **no** (`deathClip`, `bodyImpactClips` empty) | **silent** | GDD §4 Death "plays death VFX/SFX" |
| Stun knockout | `GameManager.cs:555-558` | **no** (`stunClip`) | pain sound only | - |
| Bullet impact | `BulletBehaivor.cs:77-80` | **no** | **silent** | - |
| Win / lose | `GameManager.cs:710-715` | **no** (`:1091-1092`) | **silent** | - |
| Coin gain | `GameManager.cs:701-704` | **no** (`:1093`) | **silent** | - |
| Task complete | `TaskManager.cs:318-321` | **no** (`:1094`) | **silent** | GDD §4.6 "Only the completing player hears or sees the feedback" |
| Team-up request / accept / break | none found in `TeamUp.cs` or `GameManager` team RPCs | - | request and break-up are silent | - |
| Dap | `TeamUp.cs:216-227` | yes | ok (bad dap has 1.63 s trailing silence) | GDD §4.5 |
| Slap / pain | `Slap.cs:247-250`, `SFXHandler.cs:17-25` | yes | ok; pain is 2D on VC (H2) | GDD §4.3 |
| Hide enter/exit | `HidingSpot.cs:430-439` | yes | ok | - |
| Gun spin (reload/spin) | none (`gunSpinClip` never referenced) | yes | assigned but never played (L8) | - |
| Boombox pick-up / drop / throw / mute | none | - | silent | GDD §4.7 |
| UI click / hover / select / toggle | `SFXManager.cs:141-151` auto-hooked | **no** (`Loading.unity:1055-1059`) | **silent** | - |
| Lobby chat message, player join/leave, ready | none found | - | silent | GDD §5 Lobby |

---

## 7. Performance

- **Player.prefab AudioSources:** 3 per player.
  - `Hand_R`: 3D, SFX, volume 0.615, Slap.mp3, fallback only.
  - `Player`: 2D, VC, the voice stream plus pain.
  - `LegsSFX`: 2D, SFX, volume 0.25, **Play On Awake = 1** with no clip, so harmless but should be off.
  - Each player also carries a disabled `AudioLowPassFilter` and `AudioReverbFilter` that `VoiceChatRaycast` enables per occluded player. That is one reverb DSP per occluded talker.
- **Always-playing voices (estimate for an 8-player GameScene, not profiled):** 8 voice streams (`loop = true`, started in `Start` on every copy, including the silent local one), 1 MusicManager, 1 Boombox (Play On Awake + Loop), 1 rain loop. That is about 11 of 32 real voices before any SFX. Footsteps (up to about 2 overlapping one-shots per walking player), gun, slap and pain one-shots come on top. Every source uses priority 128, so under load Unity may virtualize a talking player's voice before a footstep (L6).
- **Loops and Play On Awake that should not be there:**
  - `Bumbox.prefab:153, 156`: Play On Awake + Loop starts `hap.mp3` streaming on spawn on every peer, unsynced, while MusicManager also plays `hap.mp3` (M9, L1).
  - `LegsSFX` Play On Awake (`Player.prefab:14867`).
  - Tutorial `Map` source Play On Awake with no clip (harmless).
- **Streaming decoders:** music (1) + Boombox (1) + rain (1) + every Streaming footstep, slap and dap one-shot (H4). Each streaming instance opens its own decoder and file stream.
- **Mixer DSP:** Lowpass on Music, three Duck Volume units, SFX Reverb on Dap. Mixer suspend is enabled at -80 dB, and Virtualize Effects is on. That is modest for PC (L7).
- **Per-frame script cost:**
  - `VoiceChatRaycast`: 3 raycasts per remote player, plus `Debug.DrawRay` calls left in (`:135, 140`).
  - `SoundToScale.GetOutputData(256)` per player and per Boombox on each peer.
  - `PlayAt` GameObject churn (M6).
- **DSP buffer:** 1024 x 4 at 48 kHz adds about 21 ms of output buffering per block. That is acceptable for a party game, but it adds to voice latency.

---

## Findings in detail

### H1. 17 core-moment clips unassigned in SFXManager
- **Evidence:** `Assets/Scenes/Loading.unity:1055-1059` has `buttonClick`, `buttonSelect`, `buttonHover`, `toggleOn` and `toggleOff` all `{fileID: 0}`. `:1079-1094` has `deathClip: {fileID: 0}`, `bodyImpactClips: []`, `stunClip`, `bulletImpactClip`, `turnStartClip`, `roundStartClip`, `shotClockTickClip`, `shotClockUrgentTickClip`, `victoryClip`, `defeatClip`, `coinRewardClip` and `taskCompleteClip` all `{fileID: 0}`. The callers (`GameManager.cs:460, 461, 557, 558, 703, 712, 809`; `RoundManager.cs:51`; `ShotClockUI.cs:106`; `TaskManager.cs:320`; `BulletBehaivor.cs:79`) run, but `PlayUI`/`PlayAt` return early on `clip == null` (`SFXManager.cs:143, 157`), so nothing is heard and nothing is logged.
- **Impact:** a death is silent, the gun hand-off is silent (the GDD's hidden-threat pillar depends on that cue), the shot clock is silent, win/lose is silent, and the UI is silent. The GDD claims several of these exist.
- **Fix:**
  1. Source or author the 17 clips (short UI clicks as mono 44.1 kHz WAV, Decompress On Load, PCM or ADPCM; stingers as CIM Vorbis 0.7) and assign them on the `SFXManager` object in `Loading.unity`.
  2. Add a one-time `OnValidate`/`Awake` warning in `SFXManager` that lists null clip fields, so this cannot regress silently.
  3. Unused candidates already in the project: `GunEmptyShot.mp3` (0.36 s, a dry click) and `GunLoadBullet.mp3` (1.07 s).

### H2. Voice source is 2D, and slap pain shares it
- **Evidence:**
  - `Assets/Prefabs/Player.prefab:4398`: AudioSource `&199935326035327325`, `OutputAudioMixerGroup` = VC, `panLevelCustomCurve` value `0` at `:4450` (spatialBlend 0).
  - `VoiceChat.audioSource` and `SFXHandler.source` both reference `{fileID: 199935326035327325}`.
  - `SFXHandler.cs:24` calls `source.PlayOneShot(clip)` for pain.
  - The Tutorial copy (`Tutorial.unity` `Audio`, VC) has spatialBlend 1.
- **Impact:**
  - Proximity voice has no left/right or front/back cue, so players cannot tell who is talking or where they are.
  - Pain sounds are 2D, obey the Voice slider instead of Effects, trigger the voice sidechains (they duck SFX and Music), and are scaled and filtered by `VoiceChatRaycast`.
- **Fix:**
  1. Set the voice source spatialBlend to 1. Use a Custom rolloff curve (flat to 2 m, falling to 0 at 25 m) or Linear with min 2 / max 25, and remove the script's linear volume write (keep only the occlusion multiplier).
  2. Give `SFXHandler` its own 3D AudioSource on the SFX group (or use `SFXManager.PlayAt` at the head position).

### H3. Voice decode rate may not match the clip rate
- **Evidence:**
  - `VoiceChat.cs:66`: `optimalRate = (int)SteamUser.OptimalSampleRate;`
  - `:75`: `AudioClip.Create("VoiceData", clipBufferSize, 1, optimalRate, true, OnAudioRead, null);`
  - `:161`: `SteamUser.DecompressVoice(input, compressedWritten, output);`
  - `Facepunch.Steamworks.Win64.dll` exports a separate `get_SampleRate`/`set_SampleRate` next to `get_OptimalSampleRate`. The project never assigns `SteamUser.SampleRate`, and in Facepunch the Stream overload of `DecompressVoice` decodes at `SampleRate`.
- **Impact:** if Steam's optimal rate is not equal to the `SampleRate` default, received voice plays at the wrong speed and pitch. If PCM arrives faster than it is consumed, the ring buffer overflows (see M2). This was not verified at runtime: it needs Steam and two clients.
- **Fix:** in `Start`, set `SteamUser.SampleRate = SteamUser.OptimalSampleRate;` (or choose a fixed 24000 and use it for both the decoder and `AudioClip.Create`). Log both values once to confirm on real hardware.

### H4. Short SFX set to Streaming; preload off everywhere
- **Evidence:**
  - Standalone override `loadType: 2` (Streaming), `quality: 1` in `Assets/SFX/WalkSFX.mp3.meta`, `Slap.mp3.meta`, `bad dap.mp3.meta` and `perrfect dap.wav.meta`.
  - An Editor `eval` confirmed `clipLoadType=Streaming stQ=1` for WalkSFX, Slap and perrfect dap with target StandaloneWindows64.
  - All 37 metas have `preloadAudioData: 0`.
- **Impact:**
  - WalkSFX is one of the only two footstep variants, played by every walking player several times per second on every peer. Each play opens a streaming decoder and file handle, which adds start latency and disk I/O and risks hitches.
  - Quality 1.0 inflates the build.
  - With preload off, the first `Shot.MP3` play in a session loads synchronously, at the most important moment.
- **Fix:**
  1. Delete the Standalone overrides on these clips.
  2. Footsteps, slap, trigger, blank: Decompress On Load (they are all < 200 KB PCM mono), Force To Mono, Preload on.
  3. Daps, hide, reload, shot: Compressed In Memory, Vorbis 0.6-0.7, Preload on.
  4. Apply with an `AudioImporter` batch plus `SaveAndReimport` in the Editor, not by editing metas.

### H5. Key lobby-wide events inaudible at distance; voice ducks SFX
- **Evidence:**
  - `SFXManager.cs:162-169` sets only `spatialBlend = 1f` and `rolloffMode = Logarithmic`. `minDistance`/`maxDistance` keep their defaults of 1/500, and `priority` stays 128.
  - Mixer: the VC group sends to the SFX Duck Volume at 0 dB (`AudioMixer.mixer:314-327`, send level `30aed28c…: 0` at `:178/:280`), with threshold -45.4 dB (`b73c51bb…` at `:175/:277`).
- **Impact:** a gunshot 10-20 m away arrives at -20 to -26 dB, and it drops further whenever anyone is talking, which in a party game is constantly. The shot and the elimination should be the loudest, most readable events in the mix.
- **Fix:**
  1. Add `minDistance`, `maxDistance`, `spatialBlend`, `priority` and `volume` parameters to `PlayAt`, or accept a small `SpatialProfile` ScriptableObject.
  2. Gunshot: min 10 m, max 80 m, Custom rolloff, priority 10, optionally spatialBlend 0.8 so the arena always hears it.
  3. Death: play a 2D elimination stinger for everyone plus a 3D body impact.
  4. Remove the VC -> SFX sidechain send, or route the gun and death stingers to a non-ducked `SFX/Critical` group.

### M1. Footsteps are 2D for every player
- **Evidence:** `Player.prefab:14855` `LegsSFX` AudioSource, curve value `0` at `:14907`, volume 0.25, `m_PlayOnAwake: 1` (`:14867`). `FootStepScript.cs:65-78` runs `PlayFootstep()` on every peer for every walking player, and `:93` calls `footstepSource.PlayOneShot(clip, 0.9f)`. The Tutorial copy is 3D.
- **Impact:** all players' footsteps are heard at the same level everywhere, with no direction. The map-wide noise masks important sounds and removes a positional cue for finding hidden players.
- **Fix:** spatialBlend 1, Logarithmic min 1 / max 20, turn off Play On Awake, and optionally skip `PlayFootstep` for remote players beyond 25 m. Add more footstep variants (WalkSFX2 and RunSFX are unused).

### M2. Voice buffer, threading and transport
- **Evidence:** `VoiceChat.cs:97` `SendVoiceDataToClientsServerRpc(stream.GetBuffer(), compressedWritten)`. The stream is never truncated, so the array sent is the MemoryStream capacity. RPCs use the default reliable delivery. `:177-184` reads `dataPosition = (dataPosition + 1) % clipBufferSize` before sampling. `:199-204` writes on the main thread while `playbackBuffer++` and `--` race with the audio thread. There is no clamp when `playbackBuffer > clipBufferSize`.
- **Impact:** packet loss produces growing delay; long bursts replay stale samples; an intermittent click can come from the off-by-one read. Upload bandwidth is wasted, and a `byte[]` is allocated per frame per talker on the server and on clients.
- **Fix:**
  1. Send `new ArraySegment`/`stream.ToArray()` of `compressedWritten` bytes, or use `ReadVoiceDataBytes` into a pooled buffer.
  2. Move voice to `[Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]` and its relay counterpart. Voice tolerates loss better than delay.
  3. Protect the ring with `Interlocked` or a lock-free SPSC buffer.
  4. Add about 60-100 ms of pre-roll before playback starts after silence, and drop the oldest samples when buffered audio exceeds about 300 ms.
  5. Fix the read-index order (read, then advance).

### M3. Voice gain x4 unclamped; `volume = 2.0` is a no-op
- **Evidence:** `VoiceChat.cs:192` `float gain = 4.0f;` and `:197` `converted *= gain;` with no clamp. `:76` `audioSource.volume = 2.0f;` (AudioSource volume is clamped to 1, and `VoiceChatRaycast.cs:58` overwrites it every frame anyway).
- **Impact:** loud talkers hard-clip, and the harsh distortion is then run through the reverb filter.
- **Fix:** remove the fixed gain. If a boost is needed, apply +6 dB on the VC group, or use `Mathf.Clamp(converted * gain, -1f, 1f)` with a soft limiter. Delete the `volume = 2.0f` line.

### M4. Proximity uses the player body, linear falloff, shared filters
- **Evidence:**
  - `VoiceChatRaycast.cs:47` `Vector3.Distance(transform.position, otherPlayer.position)`, where `transform` is the local player root, not the AudioListener.
  - `:57` `1f - (distanceToOtherPlayer / maxDistance)`, and `:75` `voiceAudio.volume *= 0.5f`.
  - `:148-155` toggles `AudioLowPassFilter`/`AudioReverbFilter` on the remote player root, where both the voice and pain sounds play.
- **Impact:** when dead, spectating or hidden, the listener's camera is elsewhere, but voice volume still follows the body. A 25 m linear ramp sounds unnatural. The filters also muffle pain sounds.
- **Fix:** once H2 is done (3D voice), let the source rolloff handle distance. Compute occlusion from the AudioListener position, `FindFirstObjectByType<AudioListener>()` cached per scene. Set the low-pass cutoff (e.g. 1500 Hz) instead of toggling a reverb filter. Stagger raycasts (one remote player per frame) and remove the `Debug.DrawRay` calls.

### M5. Sidechain ducking always active in GameScene
- **Evidence:**
  - GameScene `CFXR4 Rain Falling` AudioSource: SFX group (`GameScene.unity:22286`), `Rain.mp3` (`:22288`), Play On Awake (`:22289`), Loop, volume 0.774, 2D.
  - The SFX group sends at 0 dB (`7fb19785…: 0`, `AudioMixer.mixer:164/266`) to the Music Duck Volume, whose threshold is -53.3 dB (`f184e34a…`, `:170/:272`).
  - The Boombox is on Music (`Bumbox.prefab:150`).
- **Impact:** the rain bed sits permanently above the -53 dB threshold, so match music and the Boombox stay ducked for the entire match. The VC send ducks Music and SFX whenever anyone talks.
- **Fix:**
  1. Create an `Ambience` group with no send and route the rain to it.
  2. Raise the Music ducker threshold to about -25 dB with a fast release.
  3. Drop the VC -> SFX send (see H5).
  4. Verify in the Audio Mixer window in Play mode ("Edit in Play Mode" meters).

### M6. One-shots are not pooled
- **Evidence:** `SFXManager.cs:159` `new GameObject($"{clip.name}_OneShot")` (which also allocates a string), `:162` `AddComponent<AudioSource>()`, `:171` `Destroy(audioObject, clip.length)`.
- **Impact:**
  - GC and CPU on every sound on every peer.
  - The `Destroy` delay ignores pitch: `TeamUp.cs:226` plays daps at 0.9-1.1, and at 0.9 the last ~10% of the clip is cut.
  - No priority is set, so these one-shots compete equally with voice.
- **Fix:** a pool of 16-24 pre-created 3D AudioSources on the `SFXManager` object. Take a free one, set position, clip, pitch, group and spatial profile, then `Play`. Return it after `clip.length / Mathf.Abs(pitch)`. Steal the oldest at priority >= 128 when the pool is exhausted.

### M7. Gun and slap sound RPCs: no holder check, no cooldown, owner latency
- **Evidence:** `Shooting.cs:613-617` `IsOwnSoundRequest` checks only `SenderClientId == OwnerClientId` and a distance of 5 m. There is no `GameManager.IsGunHolder` check and no `RpcValidation.IsCooldownElapsed`. The same holds for `Slap.cs:236-242`. The owner path `Shooting.cs:510-553` never plays locally when networked; it waits for the ClientRpc.
- **Impact:**
  - A modified client can spam reload, trigger and blank sounds at frame rate. Each one allocates a GameObject on every peer (M6).
  - A non-holder can fake gun sounds to mislead the lobby about who has the gun.
  - Legitimate holders hear their own trigger or reload one RTT late.
- **Fix:**
  1. Server: require `GameManager.Instance.IsGunHolder(sender)` for trigger, blank and reload, and add a 0.25 s per-sound cooldown via `RpcValidation.IsCooldownElapsed`. Add a 0.3 s cooldown to `PlaySlapVfxServerRpc`.
  2. Client: play the owner's own sound immediately and send the ClientRpc to everyone except the owner (`TargetClientIds` excluding `OwnerClientId`).
  3. Design note (GDD B4): positional blank, reload and trigger sounds reveal the hidden holder. Decide whether that is intended.

### M8. Reload cue starts 1.38 s late
- **Evidence:** `ffmpeg silencedetect` on `Assets/SFX/Gun/Reload.MP3` gives `silence_start: 0 silence_end: 1.384921`, with further silent spans until 6.35 s of the 7.44 s total. It is imported stereo, CIM.
- **Impact:** the reload sound lags the `Reload` animation (`Shooting.cs:202-205`) by about 1.4 s, and it holds a voice for 7.4 s.
- **Fix:** trim the source to its audible parts (about 0.1 s pre-roll), or replace it with `GunLoadBullet.mp3`. Force To Mono. Also trim the 1.63 s tail of `bad dap.mp3`.

### M9. MusicManager crossfade and duplicate music in GameScene
- **Evidence:**
  - `MusicManager.cs:51` `float initialVolume = musicSource.volume;` is captured at the start of each coroutine. A new `sceneLoaded` during a fade (Loading -> Lobby, LoadingScreen -> GameScene) starts a second coroutine from the already-lowered volume, and nothing stops the first.
  - `:62-71` only handles `"Lobby"` and `"GameScene"`. For `LoadingScreen`, `Tutorial` and `Error` it fades out and calls `Play()` on the same clip, restarting it.
  - `musicClips[1]` = `hap.mp3` (`Loading.unity`), and `Bumbox.prefab:152` also defaults to `hap.mp3` with Play On Awake.
- **Impact:** music volume can permanently drift toward 0 over a session. The lobby track restarts on the loading screen. In GameScene the same track plays twice, out of sync (2D music plus the 3D Boombox).
- **Fix:**
  1. Store the target volume once in `Awake`, and `StopCoroutine` the previous fade before starting a new one.
  2. Use a scene -> clip map where scenes without an entry keep the current clip playing.
  3. Pick a different default for the Boombox, or leave its clip empty and turn off Play On Awake.

### M10. Possibly two AudioListeners in GameScene
- **Evidence:** `Assets/Scenes/GameScene.unity:992` has an AudioListener on an active `Camera`. `Player.prefab:20060` has an AudioListener on the `Camera` GameObject (`&8813567206117873541`, `m_IsActive: 1`), which `Movement.ApplyOwnerVisualState` (`Movement.cs:198`, field `cam` at `Player.prefab:3644`) keeps active for the local player. No script disables the scene camera's listener.
- **Impact:** if both are enabled, Unity warns "There are 2 audio listeners" and spatialization follows whichever listener it picks.
- **Fix:** remove the AudioListener from the GameScene `Camera` (Cinemachine drives the player camera), or disable it in `PlayerSpawner` when the local player spawns. Check in Play mode with `FindObjectsByType<AudioListener>`.

### L1. Boombox playback state not synchronized
- **Evidence:** `Bumbox.prefab:152-165`: clip `hap.mp3`, Play On Awake, Loop, Music group, Logarithmic min 5 / max 500, Doppler 0. `BumBox.cs:106-123` sets the clip and calls `Play()` from sample 0 on each peer. `BumBox.cs:240-254`: `MuteClientRpc` toggles pause/unpause per peer (also noted as GDD issue 10). `ChangeMusicServerRpc` has a distance check only.
- **Fix:**
  1. Replicate a `NetworkVariable<bool> isMuted` and a server start time (`NetworkManager.ServerTime`), then set `audioSource.time = (serverNow - startTime) % clip.length` on apply and for late joiners.
  2. Add a 1 s cooldown to `ChangeMusicServerRpc` and `MuteServerRpc`.
  3. Consider max distance 30 m so the box stays local.

### L2. Stereo 3D one-shots not forced to mono
- **Evidence:** `forceToMono: 0` on `Gun/Shot.MP3`, `Gun/EmptyGunShot.MP3`, `Gun/Trigger.MP3`, `Gun/Reload.MP3`, `GunSpinAfterShot.mp3`, `GunSpinning.mp3`, `CardBoardHidding.mp3`, `CardboardPopping.mp3` and `DuckGettingSlapped (1..8).mp3`. All are 2-channel sources played at spatialBlend 1 (`SFXManager.cs:164`). An `eval` confirmed `Shot.MP3 ch=2`.
- **Fix:** Force To Mono (Normalize on) through an `AudioImporter` batch plus `SaveAndReimport`. This halves decode and memory and gives consistent 3D positioning.

### L3. Lossy sources; music quality 1.0; Rain background load
- **Evidence:** 33 of 37 sources are `.mp3`, and Unity re-encodes them to Vorbis (a double lossy generation). The Standalone override is `quality: 1` on `hap.mp3`, `Lobby.mp3` and `LobbyMusic.mp3`. `Rain.mp3.meta` has `loadInBackground: 0` on a 467.8 s, 15 MB source.
- **Fix:** request WAV masters for new SFX and music. Set music quality to 0.6-0.7 (Vorbis at 1.0 roughly doubles music size for no audible gain on MP3 sources). Enable Load In Background on Rain. Consider cutting the rain to a seamless 60-90 s loop.

### L4. Duplicates, corrupt and unused clips
- **Evidence:**
  - `Lobby.mp3` and `LobbyMusic.mp3` are both 3,585,518 bytes and 149.394 s; their decoded MD5s differ, and both are in the `Bumbox.prefab` playlist.
  - `GunClick2.mp3`: ffprobe reports "Invalid data found when processing input".
  - Unreferenced by any scene, prefab or asset: `GunClick1.mp3`, `GunClick2.mp3`, `GunEmptyShot.mp3`, `GunLoadBullet.mp3`, `GunShot.mp3`, `RunSFX.mp3`, `WalkSFX2.mp3`, `Assets/typewriter-sound-effect-312919.mp3` (2.3 MB) and `Assets/wooden-door-creaking-102413.mp3`. These are not built unless they sit under `Resources`, so the cost is repo size only.
- **Fix:** listen and remove one lobby track from the playlist if they match. Delete or re-export `GunClick2.mp3`. Move the two root mp3s into `Assets/SFX`, or delete them. Use `WalkSFX2`/`RunSFX` as extra footstep variants.

### L5. No UI group; slider edge cases; dead code
- **Evidence:**
  - `SFXManager.cs:98-101` routes `uiSource` to `sfxMixerGroup`.
  - `InpToSlider.cs:116` `Mathf.Log10(value) * 20` is not clamped, so a slider at 0 passes -Infinity to `SetFloat`.
  - `InpToSlider.cs:87` applies `Log10(number)` before the clamp at `:90`, so typing `5` applies +14 dB until the next load.
  - `SettingsManager.cs:199-218` `WaitForAudioListenerThenApply` is never started.
- **Fix:** add a `UI` group under Master (and an optional `UIVolume` exposed parameter). Use `Mathf.Log10(Mathf.Clamp(value, 0.0001f, 1f)) * 20f` in both handlers. Delete or wire up the coroutine.

### L6. Voice priority and permanent voices
- **Evidence:** all AudioSources have `Priority: 128` (for example `Player.prefab:4398` block). `VoiceChat.cs:77-78` sets `loop = true` and calls `Play()` on every player instance, including the owner's. `ProjectSettings/AudioManager.asset` has `m_RealVoiceCount: 32`.
- **Fix:** voice sources at priority 20, gun and death at 10, footsteps at 200, ambience at 150. Skip `Play()` on the owner's own voice source (`if (IsOwner) return;` before creating the clip). Consider 48 real voices for PC.

### L7. Mixer effect cost
- **Evidence:** `AudioMixer.mixer:395-414` puts SFX Reverb (the most expensive built-in effect) on the Dap group, with three Duck Volume units (`:25-52, :286-313, :415-442`). The third ducker (`&7283576295547633621`) is not attached to any group's effect list (orphaned).
- **Fix:** replace the reverb with a send to a shared reverb return that is bypassed by snapshot when idle, or bake the reverb into the dap clip. Remove the orphaned ducker in the Mixer window. Measure in the Profiler Audio module.

### L8. Dead or unused audio code
- **Evidence:**
  - `SFXManager.cs:53` `gunSpinClip` is assigned (`Loading.unity:1083`) but never read.
  - `Player/NetworkOneShotAudio.cs` has no references in any scene, prefab or asset. If used, it would spawn a NetworkObject per sound.
  - `GameManager.cs:970-976` builds `clientRpcParams` that `:978` never passes, so the dap is broadcast to everyone, which the comment at `:964` says is intended.
- **Fix:** play `gunSpinClip` on reload (it replaces the slow Reload cue nicely), or remove the field. Delete `NetworkOneShotAudio`. Remove the unused params.

### L9. Double pain sound on the stun slap
- **Evidence:** `Slap.cs:312` invokes `victimSlap.OnSlapRecived`, which runs `SFXHandler.PainSound`. On the stun, `GameManager.StunPlayerClientRpc` -> `Ragdoll.TriggerRagdoll` -> `EnableRagdoll` also calls `sfxHandler.PainSound()` (`Ragdoll.cs:232-235`).
- **Fix:** remove the call in `EnableRagdoll`, or have it play `stunClip` instead once H1 is fixed.

### L10. Voice relay scope and rate
- **Evidence:** `VoiceChat.cs:130-138` targets every connected client except the sender, regardless of distance or `isDead`. The only guard is `IsValidVoicePayload` (size).
- **Fix:** server-side, skip receivers beyond about 30 m (the audible range plus margin). Decide the dead-player policy (GDD open question: separate channel or mute to the living). Add a per-sender budget (for example max 20 packets/s, 16 KB/s).

---

## Could not verify

- **Runtime voice behaviour:** the actual `SteamUser.OptimalSampleRate` versus `SteamUser.SampleRate` on player machines (H3), end-to-end voice latency, and packet-loss behaviour. These need Steam and two clients.
- **Two AudioListeners in GameScene (M10):** confirmed in YAML only. Whether the scene `Camera` is disabled or destroyed at runtime by something not found in code (an animation, a Cinemachine setup, or the spawner) was not checked in Play mode.
- **Mix levels:** actual loudness in context (H5, M5), ducking depth, and whether the rain really exceeds the ducker threshold after its 0.774 volume and the SFX Attenuation. No Play-mode mixer metering was done.
- **Voice count, DSP CPU and audio memory:** not profiled; the numbers in section 7 are estimates.
- **Clip content:** whether `EmptyGunShot.MP3` is a dry click or a gunshot variant (same size and length as `Shot.MP3`, different PCM), and whether `Lobby.mp3`/`LobbyMusic.mp3` are the same track. Nothing was listened to.
- **Imported values in the Editor:** a bulk Editor `eval` over all clips timed out (5 s main-thread limit), so it was spot-checked on 5 clips. The rest of the import settings come from `.meta` files, and the Editor results matched the metas for every clip checked.
- **Cleanup:** the console shows `Exposed name does not exist: Voice` and `VoiceVolume`. Those warnings were caused by this audit's own `AudioMixer.GetFloat` probe, not by project code. Clear the console.
