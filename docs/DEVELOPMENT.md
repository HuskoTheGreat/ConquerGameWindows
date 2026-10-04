# Developing Conquer

## Requirements

- The [.NET 8 SDK](https://dotnet.microsoft.com/download).
- Windows to play. The rules engine, server and every test (including the headless UI tests) also build and run on
  Linux and macOS.

## Run it

```bash
dotnet run --project src/Conquer.Client
```

To play on one computer with no server, and start games straight from the command line, use the launcher in
[`local/`](../local/README.md): double-click `local\play.cmd`, or run `local\play.cmd --players 4 --seed 42`.

To try online play on one machine, start the server and connect to `127.0.0.1:5080`:

```bash
dotnet run --project server/src/Conquer.Server
```

For a hosted server, players type just its domain; the client adds `wss://` and `/ws`. Hosting is covered in
[server/README.md](../server/README.md).

## Test it

```bash
dotnet test Conquer.sln
dotnet test server/Conquer.Server.sln
```

The suites cover the rules (including a random-play fuzz test), board generation, the wire protocol and its
security, the bots, the server, and a headless UI test that clicks the real buttons. The UI tests save screenshots to
`%TEMP%\conquer-shots` (override with `CONQUER_SHOT_DIR`), so the interface can be checked without a display.

Every push and pull request runs both suites in the **CI** workflow.

## Layout

```
Conquer.sln
src/Conquer.Core/     Rules engine, board generation, bots and the network protocol. No UI dependencies.
src/Conquer.Client/   Avalonia desktop app (single player and online)
local/                One-computer launcher: no server, quick-start options, standalone .exe build
server/               ASP.NET Core WebSocket game server and deployment scripts
installer/            Self-updating launcher and the Windows setup
tests/                NUnit tests for Core, plus headless UI tests for the client
docs/                 Rules, design notes and screenshots
```

`Conquer.Core` is the heart of the project. The client sends **commands** to the engine (`Game.Apply`) and draws the
result; the engine validates everything. Online, the same commands go to the server, which owns the only real game
and sends each player a private snapshot back. See [NETWORKING.md](NETWORKING.md) for the protocol and threat model.

## Animations

Animations live in `src/Conquer.Client/Animation/` and never touch the rules engine. After every change the window
captures a `GameView` (the public state as one player sees it) and `VisualDiff` compares it with the previous one to
produce events such as `DiceRolled`, `CardsMoved` or `ActionCardDrawn`. `AnimationLayer` plays those on its own clock;
the game never waits for it, and a skipped or merged change only means a skipped animation.

Because `GameView` reads only public queries, it works unchanged on the read-only mirror an online client rebuilds
from each server snapshot, and it respects the same privacy: other players' stolen or drawn cards fly face down.

## Releases

The **Windows installer** workflow publishes `Conquer-win-x64.zip`, `latest.json` and `ConquerSetup.exe` to the
`game-latest` release on every push to `main`. Installed launchers check that release on start, so every push to
`main` reaches players without a reinstall. To publish builds to a different public repository, set the Actions
variable `GAME_RELEASES_REPO` (`owner/name`) and a `GAME_RELEASES_TOKEN` secret that can write its releases.

To build the setup yourself on Windows, install [Inno Setup 6](https://jrsoftware.org/isdl.php)
(`winget install JRSoftware.InnoSetup`) and run `installer\build-installer.cmd`. The launcher's tests run with
`dotnet test installer/Installer.sln`.

## Contributing

Bug reports and ideas are welcome as [issues](https://github.com/HuskoTheGreat/ConquerGameWindows/issues). For code,
open a pull request against `main` with tests for any rule or protocol change; CI must be green before merging.
