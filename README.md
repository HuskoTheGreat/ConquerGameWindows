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

**The game is playable today, on one computer or online.** The client opens on a start screen with two choices:

- **Single player** runs everything on this computer: you against computer players (two by default, Easy, Normal
  or Hard), or friends sharing the screen and passing the device.
- **Online** connects to the game server in `server/`. One player creates a room and shares its code; the others
  join with it, and the host starts the game. Computer players are single-player only for now. Each player sees only their
  own hand, there's chat next to the game log, and a dropped player can reconnect to their seat.

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
- **Animations**: tumbling dice and glowing tiles on a roll, resource cards thrown from the paying tile to each
  player, development-card pulls that flip to show the buyer what they drew, pieces that pop onto the board, a
  hopping robber, turn and award banners, a notice listing any house-rule change, and a victory screen. Hands and the bank are drawn as card stacks.
  Animations are worked out by comparing two views of the game, so they will play the same from server snapshots
  in online mode (see [Animations](#animations) below). They can be turned off on the new-game screen.
- **Online play**: the client's online mode (`src/Catan.Core/Net/OnlineSession.cs` and `WebSocketLink.cs`, with
  the lobby and chat in `MainWindow.Online.cs`) talks to the WebSocket server in `server/`. A test plays whole
  two-player games through it.
- **Networking foundations**: a strict, fuzz-tested wire protocol; per-player snapshots that never
  reveal other players' cards or RNG seeds; seats with reconnect tokens, rate limiting and lockouts; and
  server-relayed text chat with moderation. Details and threat model: [docs/NETWORKING.md](docs/NETWORKING.md).
- **Over 200 automated tests**, including a random-play fuzz test of the rules and a headless UI test that clicks the
  real buttons and renders screenshots.

![A 61-tile board generated at radius 4](docs/images/large-board.png)

### Next

1. More polish for the local game, such as scene transitions and sound.
2. Online polish: bots proposing trades, turn timers, and letting the host remove a player.
3. Voice chat is deferred: Vivox only worked inside Unity, so a replacement (for example
   WebRTC) is an open decision.

## Run it

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) on Windows.

```bash
dotnet run --project src/Catan.Client
```

To play on one computer with no server at all (and start games straight from the command line for testing), use
the launcher in [`local/`](local/README.md): double-click `local\play.cmd`, or run `local\play.cmd --players 4 --seed 42`.

To play online, choose **Online** and type the server's address. For a hosted server that's just its domain
(the client adds `wss://` and `/ws`); to try it on one machine, start `dotnet run --project server/src/Catan.Server`
and use `127.0.0.1:5080`. The address and your name are remembered for next time.

Run the tests:

```bash
dotnet test Catan.sln
```

## Layout

```
Catan.sln
src/Catan.Core/     Engine-agnostic game logic (rules, board, networking protocol). No UI dependencies.
src/Catan.Client/   Avalonia desktop app (single player and online)
local/              One-computer launcher: no server, quick-start options, standalone .exe build
tests/              NUnit tests for Core, plus headless UI tests for the client
docs/               Design notes and screenshots
```

`Catan.Core` is the heart of the project. The client sends **commands** to the engine (`Game.Apply`) and draws
the result; the engine validates everything. The same commands are what an online client will send to a server.

The UI tests save PNGs to `%TEMP%\catan-shots` (override with `CATAN_SHOT_DIR`), so the interface can be checked
without a display.

## Animations

Animations live in `src/Catan.Client/Animation/` and never touch the rules engine. After every change the window
captures a `GameView` (the public state as one player sees it) and `VisualDiff` compares it with the previous one
to produce events such as `DiceRolled`, `CardsMoved` or `DevCardDrawn`. `AnimationLayer` plays those on its own
clock; the game never waits for it, and a skipped or merged change only means a skipped animation.

Because `GameView` reads only public queries, it works unchanged on the read-only mirror an online client rebuilds
from each server snapshot, and it respects the same privacy: other players' stolen or drawn cards fly face down.

## History

The project started in Unity and moved to plain .NET with an Avalonia client. The Unity-era view layer,
Netcode/Relay adapter and Vivox adapter are preserved in the git tag `unity-final` (`git checkout unity-final`
to look at them).
