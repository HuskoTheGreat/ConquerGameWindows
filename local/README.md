# Local play (one computer, no server)

Everything in this folder runs Catan on a single computer. It never connects to the online server, so you can
test the game, the rules and new house rules without starting or deploying anything.

It is not a copy of the game: it runs the same rules engine (`src/Catan.Core`) and the same window
(`src/Catan.Client`) as the main client, so anything added there shows up here too.

## Play

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download). From the repository root, either double-click
`local\play.cmd` or run:

```bat
local\play.cmd
```

or, from any shell:

```bash
dotnet run --project local/Catan.Local
```

That opens the new-game screen. Players share the screen and pass it between turns; hands are hidden at each hand-off.

### Start a game straight away

Give any game option and the new-game screen is skipped:

```bat
local\play.cmd --players 4 --radius 3 --seed 42 --show-hands
```

| Option | What it does |
| --- | --- |
| `--players N` | Number of players, 2-6 (default 3) |
| `--radius R` | Board radius, 1-6 (default 2, the classic 19 tiles) |
| `--vp N` | Points to win, 3-20 (default 10) |
| `--seed S` | Fixed seed: the same seed gives the same board and dice, so a bug can be replayed |
| `--show-hands` | Don't hide hands between turns (handy when you are playing every seat yourself) |
| `--no-animations` | Turn animations off |
| `--help` | List the options |

With `dotnet run`, put the options after `--`: `dotnet run --project local/Catan.Local -- --players 4`.

House rules are changed in-game from the **House Rules** button, the same as in the main client.

## Make a standalone .exe

```bat
local\publish.cmd
```

This builds `local\dist\CatanLocal.exe`, a single file that runs on any 64-bit Windows PC without .NET installed
(about 100 MB, since it carries the .NET runtime). Copy it anywhere and double-click it.

## Tests

```bash
dotnet test local/Catan.Local.sln
```

`local/Catan.Local.sln` holds only what local play needs (Core, Client and this launcher), so it builds without the
server. The launcher and its tests are also in the root `Catan.sln`.
