# Online play: design and threat model

Status: **designed, partly built**. The protocol, per-player snapshots, session logic and chat policy are
implemented in `src/Catan.Core/Net/` and covered by tests (`tests/Catan.Core.Tests/NetworkSecurityTests.cs` and
`ChatTests.cs`). There is **no server or network client yet**; the Avalonia client is hot-seat only.

## Architecture

A dedicated .NET server owns the only real `Game` and one `GameSession` per room. Clients send command bytes and
receive a private snapshot after every change. The planned transport is WebSockets over TLS (`wss`), which gives
encryption in transit without extra work; the session logic only deals in bytes, so the transport can change.

| Piece | Where | Status |
|---|---|---|
| Wire format, bounded decoders | `Net/Wire.cs` | Done, fuzz-tested |
| Command codec (no player id on the wire) | `Net/CommandCodec.cs` | Done |
| Per-viewer snapshots and read-only client mirror | `Net/Snapshot.cs`, `GameMirror.cs` | Done |
| Seats, join and reconnect, lockout, rate limits | `Net/GameSession.cs` | Done |
| Text chat policy and moderation | `Net/Chat.cs`, `GameSession` | Done |
| Sanitizing, rate limiter, secure random | `Net/Security.cs` | Done |
| Server host (WebSockets/TLS), room codes | planned | Not started |
| Online mode in the Avalonia client | planned | Not started |

## Threat model

| Threat | Mitigation |
|---|---|
| Client pretends to be another player | The acting seat comes from the connection, never from the payload (`CommandCodec` has no player field). |
| Client reads other players' cards | Snapshots are built per viewer: opponents' hands, dev cards, deck order and RNG seeds are never sent. |
| Client predicts dice or steals | Game and board seeds come from a CSPRNG on the server and are not in any message. |
| Illegal moves | The server's `Game` re-validates every command; clients only get a read-only mirror that refuses `Apply`. |
| Malformed, oversized or fuzzed packets | Hand-written bounded reader: size caps, enum and coordinate ranges, board-geometry checks, strict UTF-8, trailing bytes rejected. No BinaryFormatter or reflection. |
| Spam and flooding | Per-client token bucket; repeated violations disconnect the client. |
| Password guessing | Constant-time compare and a lockout after repeated failures. |
| Seat hijack on reconnect | Seats are reclaimed only with a random 128-bit token sent privately to that client. |
| Markup injection via names or chat | Names and log lines are stripped of `<`, `>`, `&` (chat keeps `&`) and control or format characters on send and on receive. |
| Eavesdropping | TLS (planned transport). |
| Late joiners and duplicate connections | Rejected once the game has started (token holders excepted); one seat per connection. |

## Chat

Text chat is **relayed by the server**, so each message is stamped with the sender's real seat, cleaned,
length-capped (200 chars), rate-limited (burst of 4, then 1 per second) and dropped for muted players. The host
(seat 0) can mute players, and mutes survive reconnects. Emoji and markup are stripped.

Voice chat is **not planned for the first online release**. The earlier Vivox adapter only worked inside Unity.
If voice is added later, WebRTC (for example LiveKit) is the likely route; any channel name or token must be a
server-issued secret given only to seated players.

## Known limits

- With a dedicated server, the operator can see everything it hosts. Players must trust the server.
- Seat tokens are meant to live in memory only; a client that restarts cannot reclaim its seat.
- Room codes are the main secret for joining; use the room password when sharing codes publicly.
- Behind a reverse proxy, the server must be told which forwarded headers to trust before per-IP limits mean anything.
