using System;
using System.Collections.Generic;
using System.Linq;
using Conquer.Core;
using Conquer.Core.Bots;

namespace Conquer.Client
{
    public enum Tool { None, Road, Village, City }

    public enum SpotKind { Vertex, Edge, Hex }

    /// <summary>A clickable place on the board, positioned in hex-layout plane coordinates (hex size 1).</summary>
    public sealed class Spot
    {
        public SpotKind Kind { get; init; }
        public Vertex Vertex { get; init; }
        public Edge Edge { get; init; }
        public Hex Hex { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
    }

    /// <summary>
    /// Local hot-seat game session. Owns the <see cref="Game"/> and everything the screen needs to be
    /// clickable, but no UI types: every action is a <see cref="Command"/> sent to the engine.
    /// </summary>
    public sealed partial class LocalGameController
    {
        readonly List<Spot> _spots = new List<Spot>();
        readonly List<string> _log = new List<string>();
        int _lastActor = -1;

        public Game Game { get; private set; }

        /// <summary>Goes up by one for every new game, so watchers can tell a fresh game from a change.</summary>
        public int GameNumber { get; private set; }
        public Tool Tool { get; private set; }
        public IReadOnlyList<Spot> Spots => _spots;
        public IReadOnlyList<string> Log => _log;
        public bool HideHands { get; private set; }

        /// <summary>True while the screen should hide hands and ask the players to pass the device.</summary>
        public bool HandoffPending { get; private set; }

        public string Toast { get; private set; }
        public event Action Changed;
        public event Action<string> ToastShown;

        // ---- Lifecycle -----------------------------------------------------------------------------

        /// <summary>Starts a local game, on <paramref name="board"/> if one was arranged (then <paramref name="radius"/> is ignored).</summary>
        public void NewGame(int players, int radius, int victoryPoints, bool hideHands, int? seed = null, IDice dice = null, Board board = null) =>
            NewGame(players, radius, victoryPoints, hideHands, seed, dice, board, null);

        void NewGame(int players, int radius, int victoryPoints, bool hideHands, int? seed, IDice dice, Board board, IList<string> names)
        {
            LeaveOnline();
            _arranging = false;
            var rng = seed.HasValue ? new Random(seed.Value) : new Random();
            var config = new GameConfig
            {
                PlayerCount = players,
                PlayerNames = names,
                Seed = rng.Next(),
                Board = new BoardConfig { Radius = board?.Radius ?? radius, Seed = rng.Next() },
                Rules = new HouseRules { VictoryPoints = victoryPoints },
            };
            Game = new Game(board ?? BoardGenerator.Generate(config.Board), config, dice);

            GameNumber++;
            _log.Clear();
            _log.Add("New game. Place your starting villages.");
            Tool = Tool.None;
            HideHands = hideHands;
            HandoffPending = false;
            _lastActor = -1;
            Refresh();
        }

        /// <summary>Whose input the game is waiting for (the discarder during a discard, else the current player).</summary>
        public int Actor =>
            IsOnline ? _online.Seat :
            Game.Phase == Phase.Discard && Game.PendingDiscards.Count > 0 ? Game.PendingDiscards.Keys.Min() : Game.CurrentPlayer;

        public Player ActorPlayer => Game.Players[Actor];

        /// <summary>
        /// Whose cards the screen shows: the actor, except while a computer player moves, when it stays on the
        /// person at the screen so a bot's hand, draws and steals are never revealed.
        /// </summary>
        public int Viewer =>
            IsOnline || !IsBot(Actor) ? Actor :
            _lastActor >= 0 && !IsBot(_lastActor) ? _lastActor :
            Enumerable.Range(0, Game.Players.Count).First(seat => !IsBot(seat));

        public Player ViewerPlayer => Game.Players[Viewer];

        public void AcknowledgeHandoff()
        {
            HandoffPending = false;
            Changed?.Invoke();
        }

        void ShowToast(string message)
        {
            Toast = message;
            ToastShown?.Invoke(message);
            Changed?.Invoke();
        }

        // ---- Sending commands ----------------------------------------------------------------------

        /// <summary>Sends a command to the engine. Returns true if it was accepted.</summary>
        public bool Send(Command command)
        {
            if (IsOnline) return SendOnline(command);

            string blocked = BotSeatBlocks(command);
            if (blocked != null)
            {
                ShowToast(blocked);
                return false;
            }

            ActionResult result = Game.Apply(command);
            if (!result.Ok)
            {
                ShowToast(result.Error);
                return false;
            }

            _log.AddRange(result.Events);
            if (_log.Count > 60) _log.RemoveRange(0, _log.Count - 60);
            if (command is BuildRoad || command is BuildVillage || command is BuildCity) Tool = Tool.None;
            Toast = null;
            Refresh();
            return true;
        }

        public void SelectTool(Tool tool)
        {
            if (Tool == tool)
            {
                Tool = Tool.None;
            }
            else
            {
                ResourceSet cost = tool == Tool.Road ? Costs.Road : tool == Tool.Village ? Costs.Village : Costs.City;
                if (!Game.Players[Game.CurrentPlayer].Hand.Contains(cost))
                {
                    ShowToast("You can't afford that yet.");
                    return;
                }
                Tool = tool;
            }
            RebuildSpots();
            Changed?.Invoke();
        }

        public void ClickSpot(Spot spot)
        {
            if (_arranging)
            {
                TileClicked?.Invoke(spot.Hex);
                return;
            }
            int me = Game.CurrentPlayer;
            switch (Game.Phase)
            {
                case Phase.SetupVillage: Send(new SetupVillage(me, spot.Vertex)); break;
                case Phase.SetupRoad: Send(new SetupRoad(me, spot.Edge)); break;
                case Phase.Engineers: Send(new BuildRoad(me, spot.Edge)); break;
                case Phase.MoveRaider: Send(new MoveRaider(me, spot.Hex)); break;
                case Phase.Main:
                    if (Tool == Tool.Road) Send(new BuildRoad(me, spot.Edge));
                    else if (Tool == Tool.Village) Send(new BuildVillage(me, spot.Vertex));
                    else if (Tool == Tool.City) Send(new BuildCity(me, spot.Vertex));
                    break;
            }
        }

        // ---- Derived UI state ----------------------------------------------------------------------

        void Refresh()
        {
            RebuildSpots();

            if (IsOnline)
            {
                Changed?.Invoke();
                return;
            }

            int actor = Game.Phase == Phase.GameOver ? Game.Winner : Actor;
            // Only hand off between people: a bot's move doesn't need the screen hidden.
            if (!IsBot(actor))
            {
                if (HideHands && actor != _lastActor && Game.Phase != Phase.GameOver) HandoffPending = true;
                _lastActor = actor;
            }
            Changed?.Invoke();
        }

        void RebuildSpots()
        {
            _spots.Clear();
            if (_arranging)
            {
                AddAllTiles();
                return;
            }
            int me = Game.CurrentPlayer;
            if (IsOnline && me != _online.Seat) return;

            switch (Game.Phase)
            {
                case Phase.SetupVillage: AddVertices(Game.LegalSetupVertices()); break;
                case Phase.SetupRoad: AddEdges(Game.LegalSetupRoadEdges()); break;
                case Phase.Engineers: AddEdges(Game.LegalRoadEdges(me)); break;
                case Phase.MoveRaider:
                    foreach (Hex h in Game.LegalRaiderHexes())
                    {
                        var p = HexLayout.ToPlane(h);
                        _spots.Add(new Spot { Kind = SpotKind.Hex, Hex = h, X = p.X, Y = p.Y });
                    }
                    break;
                case Phase.Main:
                    if (Tool == Tool.Road) AddEdges(Game.LegalRoadEdges(me));
                    else if (Tool == Tool.Village) AddVertices(Game.LegalVillageVertices(me));
                    else if (Tool == Tool.City) AddVertices(Game.LegalCityVertices(me));
                    break;
            }
        }

        void AddVertices(IEnumerable<Vertex> vertices)
        {
            foreach (Vertex v in vertices)
            {
                var p = HexLayout.ToPlane(v);
                _spots.Add(new Spot { Kind = SpotKind.Vertex, Vertex = v, X = p.X, Y = p.Y });
            }
        }

        void AddEdges(IEnumerable<Edge> edges)
        {
            foreach (Edge e in edges)
            {
                var p = HexLayout.ToPlane(e);
                _spots.Add(new Spot { Kind = SpotKind.Edge, Edge = e, X = p.X, Y = p.Y });
            }
        }

        public string Prompt()
        {
            string online = IsOnline ? OnlinePrompt() : null;
            if (online != null) return online;

            if (Game.Phase != Phase.GameOver && IsBot(Actor))
                return $"{ActorPlayer.Name} ({BotPlayer.Describe(_bots[Actor].Difficulty)} bot) is thinking...";

            switch (Game.Phase)
            {
                case Phase.SetupVillage: return "Place a starting village on a glowing corner.";
                case Phase.SetupRoad: return "Place a road next to your new village.";
                case Phase.Roll: return "Roll the dice.";
                case Phase.Discard: return $"{Game.Players[Actor].Name} must discard {Game.PendingDiscards[Actor]} cards.";
                case Phase.MoveRaider: return "Move the raider: click a hex.";
                case Phase.Steal: return "Choose a player to steal a card from.";
                case Phase.Engineers: return $"Engineers: place {Game.FreeRoadsLeft} free road(s).";
                case Phase.GameOver: return $"{Game.Players[Game.Winner].Name} wins!";
                default:
                    return Tool == Tool.None
                        ? "Build, trade, or end your turn."
                        : $"Click a glowing spot to build a {Tool.ToString().ToLower()}.";
            }
        }
    }
}
