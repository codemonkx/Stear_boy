# Phone protocol (Gyropad 2.x, "PW2")

Derived from `decompile/Gyropad/P.cs` and `decompile/Gyropad/Net.cs`. This is the
contract the Linux port must reproduce exactly; the phone app cannot be changed.

**Status:** complete from source. Still to be confirmed against a live capture
(Phase 2) — anything marked *verify* is inferred from code, not observed traffic.

## Transports and ports

| Port | Proto | Direction | Purpose |
|---|---|---|---|
| 5005 | UDP | phone → PC | input packets (default; PC may move to an ephemeral port) |
| 5006 | UDP | both | discovery broadcasts (bind `0.0.0.0:5006`) |
| 5007–5011 | TCP | both | pairing listener on the PC (first free port wins) |
| 7507 | TCP | both | default pairing port on the phone |
| 30000 | UDP | game → PC | Live for Speed OutGauge |
| 30001 | UDP | game → PC | Live for Speed OutSim (configurable) |

The PC announces the actual input port, so a busy 5005 is not fatal for phones on
Gyropad 2.0+. Older phones assume 5005.

## Discovery (UDP, ASCII, to 5006)

PC broadcasts every 2 s:

```
PW2 ANNOUNCE PC <name> <pairPort> [<ipv6>] [in=<inputPort>]
```

Phone broadcasts:

```
PW2 ANNOUNCE PHONE <name> <pairPort> [<ipv6>]
```

or asks to be paired:

```
PW2 PAIRME <name> <pairPort>
```

`<name>` uses `_` for spaces (the UI shows spaces). A missing port defaults to
7507. The Windows build parsed the name from token 3 rather than token 2, so the
port accepts both shapes: `PW2 PAIRME <name> <pairPort> [<ipv6>]` and the
extra-token form that quirk implied.

The PC announces every 2 s to `255.255.255.255:5006` and to a per-interface
broadcast address (recomputed every 4 s), and forgets a phone after 9 s of
silence. Peer addresses are stored as plain IPv4 even though the pairing
listener is dual-mode (IPv4-mapped `::ffff:a.b.c.d` is normalised).

## Pairing (TCP, ASCII lines, `\n` terminated)

The PC listens on the first free port in 5007–5011. Either side may initiate.

**Phone → PC:**

```
PAIR <phoneName>          →  OK <32 hex chars>   (user approved on the PC)
                          →  NO                 (declined / no approval handler)
```

**Reconnect (no approval needed):**

```
HELLO <token>             →  OK                 (token matches stored peer)
                          →  NO
```

**PC → phone** uses the same exchange: connect to `<phone>:<pairPort>`, send
`PAIR <pcName>`, expect `OK <token>`.

The token is 16 random bytes rendered as 32 lowercase hex characters. It is
stored in the settings file and used to authenticate UDP input; it is sent in
clear text over TCP, so pairing assumes a trusted LAN.

## UDP input packets

All integers little-endian. The final 4 bytes are a tag:

```
tag = first 4 bytes of SHA-256( ASCII(token) || packet[0 .. tagOffset) )
      interpreted as uint32 little-endian
```

The token is the ASCII bytes of the 32-hex-character string, not the raw 16
bytes. A packet with a bad tag is dropped and counted (`Wheel.Rejected`).

### Wheel, protocol 2 — magic 80, version 2, length 24

| Offset | Type | Field |
|---|---|---|
| 0 | u8 | magic `80` (0x50) |
| 1 | u8 | version `2` |
| 2 | u16 | sequence |
| 4 | i16 | steer (−32767..32767) |
| 6 | u16 | throttle (0..65535) |
| 8 | u16 | brake |
| 10 | u16 | clutch |
| 12 | u16 | handbrake |
| 14 | u16 | buttons |
| 16 | u32 | echo (phone timestamp, ms) |
| 20 | u32 | tag |

### Wheel with look, protocol 3 — magic 80, version 3, length 28

Same as above through offset 20, plus:

| Offset | Type | Field |
|---|---|---|
| 20 | i16 | look X (−32767..32767) |
| 22 | i16 | look Y |
| 24 | u32 | tag |

### Gamepad, protocol 3 — magic 71, version 3, length 28

| Offset | Type | Field |
|---|---|---|
| 0–1 | | magic `71` (0x47), version `3` |
| 2 | u16 | sequence |
| 4 | i16 | left X |
| 6 | i16 | left Y |
| 8 | i16 | right X |
| 10 | i16 | right Y |
| 12 | u16 | left trigger |
| 14 | u16 | right trigger |
| 16 | u16 | buttons |
| 18 | u8 | hat (0–7, 8 = neutral) |
| 20 | u32 | echo |
| 24 | u32 | tag |

### Settings — magic 83, version 4, length 20

| Offset | Type | Field | Scale |
|---|---|---|---|
| 2 | u16 | assist strength | /1000 |
| 4 | u16 | assist deadband (deg) | /10 |
| 6 | u16 | assist full-at (deg) | /10 |
| 8 | u16 | lock-to-lock (ms) | — |
| 10 | u16 | phone aspect ratio | /1000 |
| 12 | u16 | lock degrees | — |
| 14 | u16 | steering deadzone | /1000 |
| 16 | u32 | tag | |

### Hold gesture — magic 85, version 1, length 12

| Offset | Type | Field |
|---|---|---|
| 2 | u8 | what: 0 none, 1 mode, 2 setup, 3 arrange |
| 3 | u8 | progress 0–255 |
| 8 | u32 | tag |

### Face / layout config — magic 70

Versions 4, 3, 2, 1 with lengths 44, 20, 16, 12. Tag offset is length − 4.

| Offset | Field |
|---|---|
| 2 | face mode (0 accel, 1 gyro, 3 wheel) |
| 3 | 4 × 2-bit pad face permutation |
| 4 | 3 × 2-bit system face permutation |
| 5 | bit 0 top swapped, bit 1 cluster swapped |
| 6 | bit 0 wheel held |
| 7 | curve k, /200 |
| 8–11 | 4 × gamma, /50 |
| 12–13 | degrees per unit (u16) |
| 14 | backlash degrees, /20 |
| 15 | bit 0 cluster linked, bit 1 stick linked |
| 16–37 | 11 × i16 offsets, /10000 (v4 only) |

## Input handling rules

- Packets are accepted only from the stored peer IP; loopback is always allowed.
  If a packet arrives from a new IP while the last good packet is older than
  1500 ms, the peer address is re-bound (phone changed network).
- Duplicate `sequence` values are discarded.
- While more datagrams are queued, buttons are OR-accumulated and applied after
  the backlog drains, so short taps are never lost.
- Link is considered dead after 2000 ms without a packet (`LINK_DEAD_MS`).
- A mode switch zeroes the previous input model before routing.
- Shift up/down has a 120 ms guard (`SHIFT_MS`) to debounce paddle shifts.

## Telemetry, PC → phone (UDP, 50 ms cadence)

Magic 84, version 5, length 28, sent to the peer's input endpoint. Not tagged.

| Offset | Type | Field |
|---|---|---|
| 2 | u8 | gear (255 = unknown) |
| 3 | u8 | flags: 1 redline, 2 traction, 4 ABS |
| 4 | u16 | RPM |
| 6 | u16 | speed, km/h × 10 |
| 8 | u8 | haptic id: 0 none, 1 gear, 2 traction, 3 redline, 4 ABS, 5 test |
| 9 | u8 | haptic strength 0–255 |
| 10 | u32 | echo of last phone timestamp |
| 14 | i16 | slip angle, deg × 100 |
| 16 | i16 | yaw rate, deg/s × 10 |
| 18 | u8 | flags2: 1 sim live, 2 using pad, 4 camera stick wanted |
| 19 | u8 | pedal capability bits |
| 20–23 | 4 × u8 | car name, ASCII, zero-padded |
| 24 | u16 | dead button mask |
| 26 | u8 | pedal labels (clutch/handbrake) |
| 27 | u8 | pedal labels 2 (throttle/brake) |

## Button bits

Input button mask (`P.B_*`):

| Bit | Meaning | Bit | Meaning |
|---|---|---|---|
| 0 | up | 8 | D-pad up |
| 1 | down | 9 | D-pad left |
| 2 | ignition | 10 | D-pad down |
| 3 | lights | 11 | D-pad right |
| 4 | horn | 12 | face triangle |
| 5 | camera | 13 | face square |
| 6 | look | 14 | face X |
| 7 | map | 15 | face circle |

Xbox 360 mask used by the ViGEm path (`ViGEmBridge`): D-pad 1/2/4/8, start 16,
back 32, L3 64, R3 128, LB 256, RB 512, guide 1024, A 4096, B 8192, X 16384,
Y 32768.

## Phase 2 acceptance

1. Golden fixtures: capture real phone → PC traffic for wheel v2, wheel v3, pad
   v3, settings, hold, face v4, and telemetry v5.
2. Harness replays input fixtures into a fake bridge and asserts decoded values.
3. Harness feeds the Windows build first (proves fixtures), then the port.
4. Negative tests: bad tag, stale sequence, wrong peer, short packet, unknown
   magic — all must be ignored without state change.
