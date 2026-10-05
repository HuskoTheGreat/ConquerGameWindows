# Conquer server

The online game server, built to run on an **Oracle Cloud Always Free** VM. It hosts rooms over WebSockets,
owns the only real copy of each game (clients just send commands and get their own private snapshot back),
and can optionally add **bots**: small open-source language models that comment on the game and chat with
players. Bots never play and never see hidden cards.

Idle, the server uses about 65 MB of RAM. The game itself is tiny; the limits below are what keep one person
from filling the VM.

```
server/
  Conquer.Server.sln
  src/Conquer.Server/        ASP.NET Core WebSocket server (references src/Conquer.Core)
    Bots/                  commentary/chat bots and the model backend
  tests/Conquer.Server.Tests/  unit tests + in-memory WebSocket integration tests
  deploy/                  Caddy, systemd units, and a one-shot Oracle setup script
```

## How it works

- **One queue per room.** Joins, commands, chat, disconnects, bot lines and timer ticks all go through a
  single queue that one loop works through, so `GameSession` and `Game` (which aren't thread-safe) only ever
  see one action at a time.
- **Unpredictable dice.** Online games draw dice, the action-card deck and steals from the OS CSPRNG
  (`SecureRng`), not the 32-bit seeded PRNG, which could be brute-forced from the rolls everyone sees.
- **Room codes** are 9 characters from `23456789ABCDEFGHJKMNPQRSTUVWXYZ` (about 44 bits) from a CSPRNG.
  Creating and joining share a slow per-IP budget (10 a minute), so codes can't be guessed.
- **Per-IP join lockout.** Wrong passwords lock out that IP only, not the whole room; a valid reconnect token
  always gets back in. A generous room-wide cap stays as a backstop.
- **Limits** (all in `ServerOptions.cs`, overridable): 200 connections, 6 per IP (IPv6 counted per /64),
  40 rooms, 2 per IP, 2 KB max message (bigger frames drop the client before being buffered), 20 frames a
  second per connection, a 64-frame send queue per client (a client that can't keep up is dropped instead of
  buffered), board size up to 3 rings, and rooms close when empty (5 min lobby, 15 min game), 10 minutes
  after a game ends, or after 8 hours.
- **Dead connections** are dropped after 40 seconds of silence, freeing the seat for a reconnect. Clients
  send a heartbeat frame every 15 seconds.
- Kestrel listens on `127.0.0.1` only; Caddy in front handles HTTPS. `X-Forwarded-For` is trusted from the
  loopback proxy and nowhere else.
- Nothing logs passwords, tokens or payloads. Default log level is Warning.

## Computer players

The host can seat computer players in the lobby (`AddBot` with Easy, Normal or Hard; `RemoveBot`). They're
real players: they take a seat, play through the same rules engine as everyone else, and only use what their
seat may know (their own hand, the board, public counts). The room runs them on its own queue with a short
pause between moves (`BotMoveDelayMs`, default 900 ms) so people can follow. They cost almost nothing: no
model, just a few heuristics.

- **Easy:** random spots and raider targets, builds when it can, rarely trades.
- **Normal:** picks strong starting spots, builds roads toward its next village, trades with the bank to
  finish a build, robs the leader, discards sensibly, and accepts player trades that help it.
- **Hard:** also weighs scarce resources and ports, plays Harvest, Plunder and Engineers at the
  right time, and pushes for Grand Army.

In tests, Hard beat Easy in 40 of 40 two-player games, Normal beat Easy in 36 of 40, and Hard won 29 of 60
three-player games against two Normal bots.

## Commentator bots

Commentator bots are optional and off by default. When on, the host picks 0 to 2 per room (at creation, or later with
`SetBots`). Each has a persona (Captain Brick, Professor Hex, Steady Sam; edit them in config). They:

- comment on highlights (a 7, a steal, a city, Great Road or Grand Army changing hands, a Plunder, a
  win) with a 45-second cooldown per room, and always congratulate the winner;
- reply when a player addresses them by name (`hey professor, odds?`) or writes `@bot`.

The game never waits on them. Requests go to one background worker with a 4-slot queue that drops when
full, a global cap of 12 model calls a minute, a 20-second timeout, and at most one request in flight per
room. If the model is down, the bots just go quiet. The prompt holds only the public log and public chat,
so a bot can't leak anyone's hand, and replies are cleaned and capped like player chat.

The backend is any **OpenAI-compatible** endpoint (`/v1/chat/completions`): llama.cpp's `llama-server`
(what the setup script installs), Ollama, LocalAI or vLLM. To use something else, implement `ILlmBackend`.

Recommended models (GGUF, Q4_K_M) for the Always Free Ampere shape:

| Model | Size | Notes |
|---|---|---|
| Qwen2.5 1.5B Instruct (default) | ~1.0 GB | Apache 2.0, good at staying in character |
| Llama 3.2 1B Instruct | ~0.8 GB | Llama community license |
| Qwen2.5 0.5B Instruct | ~0.4 GB | for the 1 GB AMD shape, noticeably sillier |

On 2 Ampere cores a 1.5B model writes a one-sentence quip in a few seconds. The LLM service runs at low CPU
priority with a 2-core and 2 GB cap, so the game always comes first.

Config (`appsettings.json` or environment variables like `Conquer__Bots__Enabled=true`):

```json
"Conquer": {
  "Bots": {
    "Enabled": true,
    "BaseUrl": "http://127.0.0.1:8081",
    "Model": "local",
    "Personas": [ { "Name": "Captain Brick", "Style": "a cheerful old sea captain..." } ]
  }
}
```

For Ollama: `BaseUrl` `http://127.0.0.1:11434` and `Model` e.g. `qwen2.5:1.5b`.

## Protocol

Binary WebSocket frames at `wss://<host>/ws`. Byte 0 is the type, the rest reuses the bounded codecs in
`Conquer.Core.Net`. `src/Conquer.Core/Net/Protocol.cs` has encoders and decoders for both directions, so the
client can share them. Protocol version 4 (snapshot version 2) carries the new house rules and effect cards;
older clients get "Game version mismatch".

| Client sends | Payload |
|---|---|
| `0x01` CreateRoom | max players, bot count, `JoinRequest` (its password becomes the room password) |
| `0x02` JoinRoom | room code, `JoinRequest` (with the seat token to reconnect) |
| `0x03` Command | `CommandCodec` |
| `0x04` Chat | `ChatCodec.EncodeSend` |
| `0x05` Start | board radius, `HouseRules` (host only) |
| `0x06` Mute | seat, muted (host only) |
| `0x07` SetBots | count (host only) |
| `0x08` Heartbeat | empty, every 15 s |
| `0x09` AddBot | difficulty: 0 Easy, 1 Normal, 2 Hard (host only, lobby) |
| `0x0A` RemoveBot | seat (host only, lobby) |

| Server sends | Payload |
|---|---|
| `0x80` RoomCreated | room code |
| `0x81` Welcome | your seat, reconnect token and the roster (resent when seats change) |
| `0x83` Snapshot | your private `SnapshotCodec` view |
| `0x84` Log | `LogCodec` public log lines |
| `0x85` ChatLine | `ChatCodec` broadcast (seat-stamped) |
| `0x86` BotChat | bot name, text |
| `0x87` Error | message |
| `0x88` Bots | the room's bot names |

## Build and run locally

```
dotnet test server/Conquer.Server.sln
dotnet run --project server/src/Conquer.Server     # ws://127.0.0.1:5080/ws
```

## Deploy to Oracle Cloud Always Free

1. Create an **Ampere A1** instance (arm64, Ubuntu 24.04). 1 OCPU / 6 GB is plenty with bots; the 1 GB AMD
   shape works without bots. Point a DNS name at its public IP.
2. In the console, add ingress rules for TCP **80** and **443** to the subnet's security list (or an NSG).
   Restrict 22 to your own IP if you can.
3. Build and copy:
   ```
   dotnet publish server/src/Conquer.Server -c Release -r linux-arm64 --self-contained false -o server/publish
   scp -r server ubuntu@<vm>:~/conquer-server
   ```
4. On the VM, after checking SSH with your key works:
   ```
   cd ~/conquer-server/deploy
   sudo CONQUER_DOMAIN=conquer.example.com BOTS=1 ./setup-oracle.sh
   ```
   This turns on automatic security updates (including the .NET runtime), sets SSH to keys only, opens
   80/443 in the VM's iptables, adds swap, installs Caddy for HTTPS, and runs the server as a locked-down
   systemd service (`conquer-server`) under its own user. With `BOTS=1` it also builds llama.cpp, downloads
   the model and runs it on loopback only (`conquer-llm`).
5. To update: publish again, copy `publish/` to `/opt/conquer/server/`, `sudo systemctl restart conquer-server`.

Oracle may reclaim Always Free instances it considers idle, so keep this folder (it's the whole config) and
don't store anything on the VM you can't lose. The server keeps no data on disk.

## Not done yet

- From the security review, still open: trade offer ids, turn timers / AFK handling, and host kick.
