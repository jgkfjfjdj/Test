# Game netcode protocol (client ↔ game server, UDP)

Version **1** (`NetProtocol.Version`). Implemented in `game/Assets/Kindling/Runtime/Net` (pure C#,
shared by the Unity client, the Unity dedicated server, the .NET dev server and bots). Design record:
[ADR 0007](../adr/0007-netcode-litenetlib-own-simulation.md).

## Transport

- **LiteNetLib 1.2.0** over UDP, IPv4 + IPv6 dual-stack, default port **27015**.
- Connection key `kindling-v1`: packets without it are rejected before any parsing.
- Two delivery modes:
  - **ReliableOrdered** (`Delivery.ReliableOrdered`): handshake, player info, player left, background
    notices. Fragmented by LiteNetLib when large.
  - **Sequenced** (`Delivery.Sequenced`): inputs and snapshots. They can be lost; an older packet
    that arrives after a newer one is dropped. They are **never fragmented**, so each must fit the
    connection's current single-packet limit: **504 bytes** before path-MTU discovery (576-byte
    MTU), growing to ~1400 as LiteNetLib discovers the MTU. The server queries the limit for every
    snapshot it builds (`IServerTransport.GetMaxSequencedPayload`).
- Disconnects carry a payload: `[DisconnectCode u8][message string]`.

## Encoding

Little-endian. `varuint` = unsigned LEB128. `string` = varuint byte length + UTF-8 (limits below).
`blob` = varuint length + bytes. Readers bounds-check everything; any malformed message drops the
connection with `ProtocolError`. Floats are IEEE-754 single; NaN/∞ are rejected.

Every message starts with a `MessageType` byte:

| Type | Value | Direction | Delivery |
|------|------:|-----------|----------|
| Hello | 1 | C → S | reliable |
| Welcome | 2 | S → C | reliable |
| Inputs | 3 | C → S | sequenced |
| Snapshot | 4 | S → C | sequenced |
| PlayerInfo | 5 | S → C | reliable |
| PlayerLeft | 6 | S → C | reliable |
| Background | 7 | C → S | reliable |

### Hello (client, first message after the transport connects)

| Field | Type | Notes |
|-------|------|-------|
| protocolVersion | u16 | Must equal the server's, else `ProtocolMismatch` (the rest is not read) |
| buildVersion | string ≤ 32 | Informational |
| displayName | string ≤ 48 | Sanitized server-side (control characters removed, ≤ 24 chars) |
| authToken | string ≤ 2048 | Join ticket ([join-tickets.md](join-tickets.md)). Empty on dev servers (`AllowDevAuth`) |
| resumeToken | blob 0 or 16 | From an earlier Welcome, to take back the same player |

A connection that sends no Hello within **5 s** is dropped (`HandshakeTimeout`).

### Welcome

| Field | Type | Notes |
|-------|------|-------|
| playerId | u16 | ≥ 1; also the player's entity id |
| serverTick | u32 | Ticks simulated so far |
| tickRate | u8 | 30 |
| mapSeed | i32 | Greybox layout seed: client and server build the same collision world from it |
| resumeToken | blob 16 | Random; keep it to resume after a drop |
| resumed | bool | True when this connection took back an existing player |
| lastInputSequence | u32 | Client continues numbering inputs after this |
| state | PlayerState | Full-precision state (below) |

Followed by PlayerInfo messages listing everyone, in batches of 20.

### Inputs (every client tick)

`newestSequence u32, count u8 (1–8), count × Input` — newest first, consecutive sequence numbers.
The client sends its newest **3** inputs each tick, so one lost packet loses no input.

Input (7 bytes): `moveX i8, moveY i8` (×127), `yaw u16` (×65536/360), `pitch i16` (×100, ±89°),
`buttons u8` (Jump 1, Crouch 2, Sprint 4, Attack 8, Interact 16, Aim 32).
**The client simulates the quantized values** (`InputCodec.Quantize`), so prediction and server see
identical inputs.

### Snapshot (every server tick, per client)

```
header:  tick u32, flags u8 (1 = has owner block), inputBufferDepth u8
owner:   lastProcessedInput u32, PlayerState          (part 0 only)
count u8, count × Entity
```

- **PlayerState** (41 bytes): position 3×f32, velocity 3×f32, yaw f32, pitch f32, flags u8
  (Grounded 1, Crouched 2, Sprinting 4, JumpHeld 8, Exhausted 16), stamina f32, staminaRegenDelay f32.
  Full precision so reconciliation replays from exactly the server's state.
- **Entity** (≤ 14 bytes): `id varuint`, `x i24` and `z i24` (centimetres, ±83 km), `y u16`
  (`PositionQuantizer`: 1 cm from −128 m), `yaw u16`, `pitch i8` (×127/90), `flags u8`
  (Grounded 1, Crouched 2, Sprinting 4, Away 8).
- `inputBufferDepth`: inputs waiting on the server for this player after the tick (time dilation).
- A tick's entities may be split over several **parts** (each a complete message with the same
  tick; only part 0 has the owner block), so that every part fits the connection's packet limit
  and the 1100-byte budget.

### PlayerInfo / PlayerLeft

PlayerInfo: `count varuint, count × (id u16, displayName string ≤ 48)`. PlayerLeft: `id u16` —
the player was removed (quit or resume grace expired).

### Background

`away bool`. Sent when the iOS app goes to the background (true) or returns (false). An away
player stands still instead of having their last input repeated. Only Background(false) or a
resume clears it (late inputs do not).

## Server rules (authority)

- Fixed **30 Hz** tick. Each tick: receive, then for every player consume **exactly one** input (the
  lowest sequence above the last processed one). With no input queued, the last input repeats
  without Jump (hides a late packet); away or detached players get no movement.
- Input queue per player: at most **8**; extra inputs drop the oldest. Sending inputs faster than the
  tick rate therefore can't make a player faster (tested). Sequences more than 1024 ahead are ignored.
- **Interest:** players within **100 m** every tick, 100–250 m every 3rd tick (staggered by id),
  beyond **250 m** not at all (the client forgets players it hasn't heard about for 2 s).
- **Drops and resume:** a transport timeout keeps the player in the world for **120 s**
  (`ResumeGraceSeconds`), shown as Away. A Hello with its resume token takes it back (a still-open
  older connection is closed with `Replaced`). A clean quit (`ClientQuit`) removes the player at once.
- Rejections carry a `DisconnectCode`: `ProtocolMismatch`, `ServerFull`, `AuthFailed`,
  `HandshakeTimeout`, `ProtocolError`, `Kicked`, `ServerShutdown`, `Replaced`.

## Client rules

- **Prediction:** the client runs the same `CharacterMotor` on each quantized input immediately and
  keeps 256 ticks of input/state history.
- **Reconciliation:** when a snapshot's owner block arrives, compare the server state with the
  predicted state for `lastProcessedInput`. If position differs by more than **1 cm** (or velocity,
  crouch or grounded differ), reset to the server state and replay the newer inputs. The visual
  jump is smoothed out (exponential decay, rate 12/s); corrections over 2 m snap.
- **Time dilation:** the client ticks up to 3% faster when the server's input buffer averages
  below 1.5, and 3% slower above 3.5, keeping inputs arriving just before they're needed.
- **Interpolation:** remote players are drawn at an estimated server tick minus an adaptive delay
  (3 ticks = 100 ms, growing with measured jitter up to 10 ticks).

## Not in protocol v1 (planned)

Join-ticket verification on the game server (M2 Unity part; dev auth until then), lag-compensated
hit validation (M5), chunk/entity replication of the world (M3–M4), delta compression of snapshots.
