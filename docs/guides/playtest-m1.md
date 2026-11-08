# Play-testing M1 (movement + netcode)

M1 is a technical slice: walk, sprint, crouch and jump around a greybox map with other players and
bots. There is no survival gameplay yet.

## On iPhone

The app comes from the `ios-testflight` workflow ([ios-testflight.md](ios-testflight.md)), once your
Apple Developer and Unity accounts are connected. In the app:

| Menu button | What it does |
|-------------|--------------|
| **Play offline** | Server and client inside the phone (no network). Quickest check that movement and touch controls work |
| **Host on Wi-Fi** | Runs a server on the phone (UDP 27015) and joins it. The HUD shows the address other phones on the same Wi-Fi can join |
| **Join server** | Connects to the address in the field (`192.168.1.20` or `host:port`) |

iOS asks for **Local Network** permission the first time you join or host on Wi-Fi. Allow it, or
the connection fails.

**Controls:** left side = floating joystick (push fully forward to sprint; slide further up to
lock auto-run, pull back to stop). Drag anywhere else to look. Buttons: JUMP, CROUCH (tap toggles,
hold is momentary), AIM, FIRE (drag while holding also aims), SPRINT (toggle), II (leave).
**Settings:** look sensitivity, invert Y, network stats overlay.

The stats overlay (top-left) shows FPS, ping, server tick, prediction corrections, interpolation
delay and bandwidth. Corrections should stay at or near zero while you move normally.

## Dev server on a PC, Mac or cloud VM

Needs the .NET 8 SDK. From the repository root:

```bash
dotnet run -c Release --project sim/Kindling.DevServer -- --port 27015
```

It prints stats every 5 s (players, tick time, CPU, bandwidth). Open UDP **27015** in the firewall
(cloud VMs: also in the provider's security group). Phones then use **Join server** with the
machine's address. The Unity dedicated server build does the same:
`./KindlingServer.x86_64 -port 27015` (also `-maxPlayers`, `-seed`, `-tickRate`).

## Bots

```bash
dotnet run -c Release --project sim/Kindling.Bots -- --host <server address> --count 30 --duration 120
```

Bots wander, sprint, crouch and jump using the same client code as the game, so phones see them
moving around. Their summary reports connections, round-trip time, corrections and bandwidth.

## Load test on one machine

```bash
sim/loadtest.sh 50 60 30-60 2 ./loadtest-results   # 50 bots, 60 s, 30–60 ms each way, 2% loss
```

Simulated latency/loss uses LiteNetLib's debug simulation (the script builds with it enabled).
Results from the M1 runs are in [docs/milestones/M1-loadtest](../milestones/M1-loadtest/).

## In the Unity editor

Open the Boot scene and press Play: the same menu appears. Keyboard and mouse: WASD, mouse look
(click to capture the cursor, Esc to release, Esc again to leave), Shift sprint, C crouch, Space jump.
To test against bots, start the dev server and bots as above and join `127.0.0.1`.

## What to report

Device model, network (Wi-Fi/LTE/5G), the stats overlay values (screenshot), and anything that felt
wrong: rubber-banding, stutter, controls not responding, misplaced buttons on your screen shape.
