# Play-testing (M1 movement + M3 survival)

A technical test with greybox art: move around with other players and bots, chop trees, pick plants,
craft tools and a campfire, cook, survive the night, die and respawn. The world is saved.

## On iPhone

The app comes from the `ios-testflight` workflow ([ios-testflight.md](ios-testflight.md)), once your
Apple Developer and Unity accounts are connected. In the app:

| Menu button | What it does |
|-------------|--------------|
| **Play offline** | Server and client inside the phone (no network); your world is saved on the phone |
| **Host on Wi-Fi** | Runs a server on the phone (UDP 27015, saved on the phone) and joins it. The HUD shows the address other phones on the same Wi-Fi can join |
| **Join server** | Connects to the address in the field (`192.168.1.20` or `host:port`) |

iOS asks for **Local Network** permission the first time you join or host on Wi-Fi. Allow it, or
the connection fails.

**Controls:** left side = floating joystick (push fully forward to sprint; slide further up to
lock auto-run, pull back to stop). Drag anywhere else to look. Buttons: JUMP, CROUCH (tap toggles,
hold is momentary), AIM, FIRE (drag while holding also aims), SPRINT (toggle), II (leave).
**Survival:** HIT swings what you hold (start with a rock: hit trees for wood). The top row is the
hotbar: tap a slot to hold it, tap it again to eat or place it. BAG opens inventory and crafting
(tap an item, then tap where it should go). When you look at something usable, a prompt and the USE
button appear (pick plants, open chests and campfires, pick up items, drink in ponds). Campfires need
wood inside and "Light"; they cook mushrooms and keep you warm at night. A bedroll lets you respawn
where you placed it.

**Settings:** look sensitivity, invert Y, network stats overlay.

The stats overlay (top-left) shows FPS, ping, server tick, prediction corrections, interpolation
delay and bandwidth. Corrections should stay at or near zero while you move normally.

## Dev server on a PC, Mac or cloud VM

Needs the .NET 8 SDK. From the repository root:

```bash
dotnet run -c Release --project sim/Kindling.DevServer -- --port 27015
```

The world is saved to `./world/world.db` (SQLite; `--world <path>`, `--wipe` to start over,
`--no-save` for a throwaway world) with hourly backups; it prints stats every 5 s (players, tick and
world time, CPU, bandwidth, saves). Options: `--gather 2`, `--day-minutes 30`, `--sleepers vanish`. Open UDP **27015** in the firewall
(cloud VMs: also in the provider's security group). Phones then use **Join server** with the
machine's address. The Unity dedicated server build does the same:
`./KindlingServer.x86_64 -port 27015` (also `-maxPlayers`, `-seed`, `-tickRate`).

## Bots

```bash
dotnet run -c Release --project sim/Kindling.Bots -- --host <server address> --count 30 --duration 120
```

Half the bots wander; the other half chop trees, craft and place campfires, eat and drink (the same
client code as the game), so phones see them working. Their summary reports connections, round-trip time, corrections and bandwidth.

## Load test on one machine

```bash
sim/loadtest.sh 50 60 30-60 2 ./loadtest-results   # 50 bots, 60 s, 30–60 ms each way, 2% loss
```

Simulated latency/loss uses LiteNetLib's debug simulation (the script builds with it enabled).
Results from the M1 runs are in [docs/milestones/M1-loadtest](../milestones/M1-loadtest/).

## In the Unity editor

Open the Boot scene and press Play: the same menu appears. Keyboard and mouse: WASD, mouse look
(click to capture the cursor, Esc to release, Esc again to leave), Shift sprint, C crouch, Space jump,
left click hit, 1–6 hotbar, F use/place, E interact, Tab inventory.
To test against bots, start the dev server and bots as above and join `127.0.0.1`.

## What to report

Device model, network (Wi-Fi/LTE/5G), the stats overlay values (screenshot), and anything that felt
wrong: rubber-banding, stutter, controls not responding, misplaced buttons on your screen shape.
