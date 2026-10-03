# Networking

Peer-hosted, host-authoritative multiplayer over Unity Relay using Netcode for GameObjects (NGO).

## Layout

| Part | Where | Needs Unity packages? | Tested here |
|---|---|---|---|
| Wire protocol, command codec, snapshots, seats, rate limiting | `Assets/Scripts/Core/Net/` | No | Yes (`NetworkSecurityTests`) |
| Read-only client mirror (`Game.IsMirror`) | `Assets/Scripts/Core/GameMirror.cs` | No | Yes |
| UI remote mode (`IGameLink`, `HotSeatController.AttachLink`) | `Assets/Scripts/View/` | No | Compiles only |
| NGO + Relay adapter | `Assets/Scripts/Net/` | Yes | **Not compiled against the real packages** |

The adapter assembly (`Catan.Net`) only builds once NGO, Relay and Authentication are installed
(it is gated by `versionDefines` + `defineConstraints`), so the rest of the project compiles without them.

## Setup (Unity 6 / 2022.3+)

1. Package Manager: install **Netcode for GameObjects**, **Relay**, **Authentication** (Unity Services Core comes with them).
2. Link the project to Unity Gaming Services (Project Settings > Services) and enable Relay.
3. Empty scene > empty GameObject > add `NetworkManager`, `UnityTransport`, `HotSeatController` and `NetworkGameManager`.
4. Press Play: one instance hosts and shares the join code; others join with it, then the host starts the game.

The relay calls use `new RelayServerData(allocation, "dtls")`. Newer transport versions moved this helper;
if it doesn't compile, use the equivalent in your installed `com.unity.services.relay` / `com.unity.transport`.

## Threat model

| Threat | Mitigation |
|---|---|
| Client pretends to be another player | The acting seat comes from the transport sender id, never from the payload (`CommandCodec` has no player field). |
| Client reads other players' cards | Snapshots are built per viewer: opponents' hands, dev cards, deck order and RNG seeds are never sent. |
| Client predicts dice / steals | Game and board seeds come from a CSPRNG on the host and are not in any message. |
| Illegal moves | The host's `Game` re-validates every command; clients only get a read-only mirror that refuses `Apply`. |
| Malformed / oversized / fuzzed packets | Hand-written bounded reader: size caps, enum and coordinate range checks, geometry checks, strict UTF-8, trailing bytes rejected. No BinaryFormatter or reflection. Fuzz-tested. |
| Spam / flooding | Per-client token bucket; repeated violations disconnect the client. |
| Password guessing | Constant-time compare; join lockout after repeated failures. |
| Seat hijack on reconnect | Seats are reclaimed only with a random 128-bit token sent privately to that client. |
| UI markup injection via names or log text | Names and log lines are stripped of `<`, `>`, `&` and control/format characters on send and on receive. |
| Eavesdropping | Relay with DTLS. |
| Late joiners / duplicate connections | Rejected once the game has started (token holders excepted); one seat per connection. |

## Known limits

- **The host sees everything it hosts.** A modified host can cheat. Fixing that needs a dedicated server.
- The seat token lives in memory only. If a client restarts it cannot reclaim its seat (by design for now).
- Join codes are the main secret for joining; use the room password if you share them publicly.
- No voice/text chat yet (Vivox is the next step); chat text will need the same sanitising and rate limits.
