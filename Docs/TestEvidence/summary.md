# Test Evidence Summary

Run: 2026-09-15, live Unity Editor 6000.5.4f1 via Unity CLI (`com.unity.pipeline` 0.7.0-exp.1), `run_tests --mode editor`.

## Final run

| Category | Total | Passed | Failed | Ignored |
|---|---|---|---|---|
| A: Unit | 123 | 123 | 0 | 0 |
| B: Functional | 128 | 128 | 0 | 0 |
| C: Security | 69 | 69 | 0 | 0 |
| **All** | **320** | **320** | **0** | **0** |

`test_status`: `{'status': 'completed', 'duration': 95.3}` with summary
`{'total': 320, 'passed': 320, 'failed': 0, 'skipped': 0, 'inconclusive': 0}`.
The raw output is in `final-test-status.json`.

## Build

Built from the live Editor with `unity command build --target StandaloneWindows64 --outputPath Builds/CI/DuckRoulette.exe`:
- `result = Succeeded`, `totalErrors = 0`, `totalWarnings = 560`
- Size 1,242,788,358 bytes; build time 174 s (17:56:56Z to 17:59:50Z)
- Raw report: `build-status.json`

## Player smoke test

The built `Builds/CI/DuckRoulette.exe` ran windowed for 45 s with `steam_appid.txt` (480), then was stopped.
- The process was still alive at 45 s, so there was no crash on boot.
- Steam initialized: `Found save file in Steam Cloud.`, `Cosmetic indexes loaded successfully from Steam Cloud.`, `File saved successfully to Steam Cloud.`
- The log had 0 lines matching `Exception|NullReference|Error`. The log is in `player-smoke.log`.

## Not covered

- A full online match between two Steam accounts on separate machines was not automated. The host turn flow is covered by the UnityTransport host tests above.
- There is no browser test, because the game is a native Steam desktop build with no WebGL target (Facepunch Steamworks has no web support).

## Previous run

That run had 318 passed and 2 ignored (`editmode-results.json` / `.xml`). The two ignored cases were
`CosmeticsTests.Cosmetics_Change_OutOfRangeSavedIndex_DoesNotThrow(3|-1)`, which exposed a real bug:
`Cosmetics.Change` indexed `list[index - 1]` with an unvalidated index loaded from Steam Cloud. The bug is fixed with
`RpcValidation.SanitizeCosmeticIndex`, and the `[Ignore]` was removed.

## Coverage

- **Unit:**
  - GameManager guards and gun-pass fallback.
  - Blackjack deck, scoring and turns.
  - TaskManager selection and serialization.
  - Coin/save format.
  - Cosmetics index handling.
  - Settings parsing and clamps.
  - Input action names and rebind round-trip.
- **Functional:**
  - All 6 build scenes open with no missing scripts.
  - 103 prefabs pass integrity checks, and DefaultNetworkPrefabs is valid.
  - Tutorial play mode logs no errors for 5 s.
  - Real Netcode host on UnityTransport: turn passing, chamber wrap, timeout pass.
- **Security:**
  - All `RpcValidation` rules, including edge cases.
  - Host regression tests: non-holder shots rejected, one shot per turn, kills require a server-recorded shot, `UpdateKillsServerRpc` grants nothing, team-up spoofing rejected.
