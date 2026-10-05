# Online play: design and threat model

Status: **server and client built**. The protocol, per-player snapshots, session logic and chat policy are
implemented in `src/Conquer.Core/Net/` and covered by tests (`tests/Conquer.Core.Tests/NetworkSecurityTests.cs` and
`ChatTests.cs`). The WebSocket server lives in `server/` (see `server/README.md`). The Avalonia client's online mode is
`Net/OnlineSession.cs` (the client side of a room, no UI) over `Net/WebSocketLink.cs`, shown by `MainWindow.Online.cs`.

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
| Server host (WebSockets/TLS), room codes, bots | `server/` | Done |
| Online mode in the Avalonia client | `Net/OnlineSession.cs`, `Net/WebSocketLink.cs`, `MainWindow.Online.cs` | Done |

## Threat model

| Threat | Mitigation |
|---|---|
| Client pretends to be another player | The acting seat comes from the connection, never from the payload (`CommandCodec` has no player field). |
| Client reads other players' cards | Snapshots are built per viewer: opponents' hands, action cards, deck order and RNG seeds are never sent. |
| Client predicts dice or steals | Online games draw dice, the action-card deck and steals straight from a CSPRNG (`SecureRng`); no seed exists to recover. |
| Illegal moves | The server's `Game` re-validates every command; clients only get a read-only mirror that refuses `Apply`. |
| Malformed, oversized or fuzzed packets | Hand-written bounded reader: size caps, enum and coordinate ranges, board-geometry checks, strict UTF-8, trailing bytes rejected. No BinaryFormatter or reflection. |
| Spam and flooding | Per-client token bucket; repeated violations disconnect the client. |
| Filling the server with idle sockets | Connections must create or join a room within 30 seconds (`JoinDeadlineSeconds`); Caddy drops clients that send headers slowly (`read_header 10s`). |
| A web page turning visitors into connections | Requests with an `Origin` header (sent by browsers, never by the game) are refused with 403. |
| Password guessing | Constant-time compare and a per-IP lockout after repeated failures (a reconnect token bypasses it, so a troll can't lock real players out). |
| Seat hijack on reconnect | Seats are reclaimed only with a random 128-bit token sent privately to that client. |
| Markup injection via names or chat | Names and log lines are stripped of `<`, `>`, `&` (chat keeps `&`) and control or format characters on send and on receive. |
| Eavesdropping | TLS (planned transport). |
| Late joiners and duplicate connections | Rejected once the game has started (token holders excepted); one seat per connection. |

## Chat

Text chat is **relayed by the server**, so each message is stamped with the sender's real seat, cleaned,
length-capped (200 chars), rate-limited (burst of 4, then 1 per second) and dropped for muted players. The host
(seat 0) can mute players, and mutes survive reconnects. Emoji and markup are stripped.

Voice chat is **not planned for the first online release**. If voice is added later, WebRTC (for example LiveKit) is the likely route; any channel name or token must be a
server-issued secret given only to seated players.

## Local network games

A player can host from inside the client (`MainWindow.Lan.cs`). It starts `LanServer`, which runs the exact server
pipeline from `ServerApp` in-process on port 47620 (or the next free one up to 47629), listening on every IPv4
interface with no TLS, one room, no chat bots and boards up to radius 6. The public server's other protections
still apply, including refusing browser-origin connections (so a web page open on the host can't join) and the
join deadline for idle sockets. The host's own window joins it over loopback
like any client, so the room, lobby, snapshots and limits are the same code as online.

While the room is in its lobby, the host broadcasts a small UDP announcement on port 47621 every 1.5 seconds
(`LanDiscovery`): port, room code, host name and seat counts. The Local network screen lists what it hears.
Announcements are untrusted: they are size-capped and validated, and the address joined is always the packet's
sender, never anything the packet claims. Anyone on the network can read the room code from the announcement, so
on a LAN the room code is an address, not a secret; a room password still keeps strangers out. Leaving the game or
closing the window stops the server and the announcements. Windows asks the first time whether the game may use the
network; allowing it on private networks only is enough.

An arranged board (board setup screen) is sent with Start, after the rules, and validated with the same checks as a
board in a snapshot. Servers older than this change reject a Start that carries a board, so the public server
needs redeploying before hosts there can use arranged boards; random boards work either way.

## Known limits

- With a dedicated server, the operator can see everything it hosts. Players must trust the server.
- Seat tokens are meant to live in memory only; a client that restarts cannot reclaim its seat.
- Room codes are the main secret for joining; use the room password when sharing codes publicly.
- Behind a reverse proxy, the server must be told which forwarded headers to trust before per-IP limits mean anything.
