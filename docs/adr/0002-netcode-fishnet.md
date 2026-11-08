# ADR 0002 — FishNet for netcode, behind our own `Kindling.Net` layer

- **Status:** Superseded by [ADR 0007](0007-netcode-litenetlib-own-simulation.md) (2026-11-08)
- **Details:** docs/blueprint/02-technology-stack.md §3, docs/blueprint/05-multiplayer-networking.md

## Context
The game needs server-authoritative simulation with client-side prediction, interest management and
a UDP transport, with no per-player fees so community servers cost us nothing.

## Decision
Use **FishNet** (Tugboat/LiteNetLib UDP, IPv6 dual-stack) for player/NPC replication and prediction.
We own the systems that matter most for this genre: chunk-based world replication, hitbox history and
lag compensation, join tickets, resume tokens and the server link. Gameplay code talks to
`Kindling.Net` interfaces, not FishNet types, wherever practical.

FishNet is vendored at a pinned version (with its license file) at the start of M1, its first use.

## Validation gate (end of M1)
2+ iPhones and 30 headless bots on a Linux server over Wi-Fi and LTE: prediction corrections are rare
and not visible, tick time and bandwidth are within the budgets in blueprint 05. If not, switch to
**Mirror** before more code depends on FishNet.

## Consequences
- License terms are re-checked when vendoring.
- Our own replication layers are extra work, but they are where performance is decided.
