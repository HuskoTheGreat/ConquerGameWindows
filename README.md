# Catan for Windows

![A game in progress: the Avalonia client with pieces on the board and the road tool active](docs/images/board.png)

## Goal

A fully fleshed-out Catan-style game for Windows, built in C#, with:

- a **new random board every game**, and boards you can **scale up** well beyond the classic 19 tiles;
- **multiplayer**, played over the internet through a dedicated, server-authoritative host;
- **text chat** (voice chat later);
- **house rules** that can be changed in the middle of a game;
- a polished **2D interface**, with 3D-style touches (card-pull animations and "scenes") rather than a real 3D game.

## Where it stands

**The local game is playable today.** Players share one screen and pass the device. The online pieces are
designed and the security-critical parts are built and tested, but there is no server or online client yet.

### Done

- **Procedural boards.** Radius 1-10 (7 to 331 tiles; the client offers 1-6). Resources, number tokens and ports
  scale from the classic 19-tile layout, and 6s/8s never touch. Boards are seeded, so a seed reproduces a board.
  The math is built on axial hex coordinates, in pure data, independent of any UI.
- **Full rules engine** (`Catan.Core`). Snake-draft setup, dice and production (including the bank-shortage rule),
  the robber, discards, roads/settlements/cities, bank and port trades, player trades, all five development
  cards, Longest Road, Largest Army, hidden victory-point cards, and winning on your own turn.
- **House rules**, changeable in-game: points to win, discard threshold, bank and port ratios, Longest Road and
  Largest Army minimums, friendly robber, no-7s-early, and dev-card timing.
- **2D Avalonia client**: click-to-build on glowing legal spots, trade and dev-card dialogs, a game log, and a
  pass-and-play screen that hides hands between turns.
- **Networking foundations** (no server yet): a strict, fuzz-tested wire protocol; per-player snapshots that never
  reveal other players' cards or RNG seeds; seats with reconnect tokens, rate limiting and lockouts; and
  server-relayed text chat with moderation. Details and threat model: [docs/NETWORKING.md](docs/NETWORKING.md).
- **125 automated tests**, including a random-play fuzz test of the rules and a headless UI test that clicks the
  real buttons and renders screenshots.

![A 61-tile board generated at radius 4](docs/images/large-board.png)

### Next

1. Polish the local game, including card-pull animations and scene transitions.
2. A dedicated server (WebSockets over TLS) hosting the existing `GameSession`, then an online mode in the client.
3. Text chat online. Voice chat is deferred: Vivox only worked inside Unity, so a replacement (for example
   WebRTC) is an open decision.

## Run it

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) on Windows.

```bash
dotnet run --project src/Catan.Client
```

Run the tests:

```bash
dotnet test Catan.sln
```

## Layout

```
Catan.sln
src/Catan.Core/     Engine-agnostic game logic (rules, board, networking protocol). No UI dependencies.
src/Catan.Client/   Avalonia desktop app (hot-seat)
tests/              NUnit tests for Core, plus headless UI tests for the client
docs/               Design notes and screenshots
```

`Catan.Core` is the heart of the project. The client sends **commands** to the engine (`Game.Apply`) and draws
the result; the engine validates everything. The same commands are what an online client will send to a server.

The UI tests save PNGs to `%TEMP%\catan-shots` (override with `CATAN_SHOT_DIR`), so the interface can be checked
without a display.

## History

The project started in Unity and moved to plain .NET with an Avalonia client. The Unity-era view layer,
Netcode/Relay adapter and Vivox adapter are preserved in the git tag `unity-final` (`git checkout unity-final`
to look at them).
