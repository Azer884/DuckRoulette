---
name: duckroulette-debug-performance
description: Diagnose DuckRoulette Unity, Steam, or multiplayer performance/replication issues with a narrow evidence-first workflow.
---

# DuckRoulette debugging and performance

Start with the affected scene and the one owning script; then follow only direct calls/RPCs. Exclude `Library`, build output, and third-party/sample folders unless the evidence points there.

For multiplayer bugs, establish: host/client role, owner/server authority, NetworkObject spawn timing, NetworkVariable/RPC direction, and the Steam lobby/Facepunch connection state. Reproduce with a host plus one remote client before changing authority or synchronization.

For performance, profile a development build or Unity Profiler capture before optimizing. Check allocations and per-frame work in `Update`, coroutines, voice buffers, network serialization/RPC frequency, instantiated VFX, and object cleanup. Prefer a measured, localized fix; do not rewrite networking or replace packages as a first response.

Do not edit generated or imported vendor assets to silence errors. Capture the exact console error/stack, then inspect project code and serialized references first.
