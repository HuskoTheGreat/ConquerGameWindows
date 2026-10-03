# Catan for Windows

A Catan-style game for Windows, written in C# on .NET 8. Every game generates a new board, boards can be
scaled up, and the rules can be tuned with house rules, even in the middle of a game.

Right now you can play a **local hot-seat game**: players share one screen and pass the device. Online play
is designed and the security-critical parts are built and tested, but there is no server or network client yet.

## Run it

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) on Windows.

```bash
dotnet run --project src/Catan.Client
```

Run the tests:

```bash
dotnet test Catan.sln
```

## What works

- **Procedural boards.** Radius 1-10 (7 to 331 tiles; the client offers 1-6). Resources, number tokens and ports
  scale from the classic 19-tile layout, and 6s/8s never touch. Boards are seeded, so a seed reproduces a board.
- **Full rules engine.** Snake-draft setup, dice and production (including the bank-shortage rule), the robber,
  discards, roads/settlements/cities, bank and port trades, player trades, all five development cards,
  Longest Road, Largest Army, hidden victory-point cards, and winning on your own turn.
- **House rules**, changeable in-game: points to win, discard threshold, bank/port ratios, Longest Road and
  Largest Army minimums, friendly robber, no-7s-early, and dev-card timing.
- **2D Avalonia client** with click-to-build on glowing legal spots, trade and dev-card dialogs, a game log and
  a pass-and-play screen that hides hands between turns.

## Layout

```
Catan.sln
src/Catan.Core/     Engine-agnostic game logic (rules, board, networking protocol). No UI dependencies.
src/Catan.Client/   Avalonia desktop app (hot-seat)
tests/              NUnit tests for Core, plus headless UI tests for the client
docs/               Design notes
Assets/             Legacy Unity project files, kept only until removed (see History)
```

`Catan.Core` is the heart of the project. The client sends **commands** to the engine (`Game.Apply`) and draws
the result; the engine validates everything. The same commands are what an online client will send to a server.

## Tests

- `Catan.Core.Tests`: rules, board generation, a random-play fuzz test that checks resource conservation, and
  the network security tests (codec fuzzing, impersonation, flooding, privacy of snapshots, chat policy).
- `Catan.Client.Tests`: controller logic and a headless Avalonia window that clicks real buttons. The screenshot
  tests save PNGs to `%TEMP%\catan-shots` (override with `CATAN_SHOT_DIR`) so the UI can be checked without a display.

## Roadmap

1. Polish the local game: card-pull animations and "scenes" (mostly 2D, with 3D-style card flips) are planned.
2. Dedicated server (WebSockets over TLS) hosting `GameSession`, then an online mode in the client.
3. Text chat online (the host-relayed chat logic already exists). Voice chat is deferred; Vivox was Unity-only, so a
   replacement (for example WebRTC) is an open decision.

See [docs/NETWORKING.md](docs/NETWORKING.md) for the online design and threat model.

## History

The project started in Unity and moved to plain .NET. The Unity-era view layer, Netcode/Relay adapter and Vivox
adapter are preserved in the git tag `unity-final`. The `Assets/` folder is a leftover copy of that layout and is
safe to delete.
