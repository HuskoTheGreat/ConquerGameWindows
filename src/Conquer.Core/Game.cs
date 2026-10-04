using System;
using System.Collections.Generic;
using System.Linq;

namespace Conquer.Core
{
    public sealed class GameConfig
    {
        public int PlayerCount { get; set; } = 3;
        public BoardConfig Board { get; set; } = new BoardConfig();

        /// <summary>Seeds dice, the action-card deck and steals. Same seed + same commands = same game.</summary>
        public int Seed { get; set; }

        /// <summary>
        /// Optional randomness for dice, the action-card deck and steals. When set (online games use
        /// <see cref="Net.SecureRng"/>), <see cref="Seed"/> is ignored for those, so they can't be predicted.
        /// </summary>
        public IRandom Random { get; set; }

        /// <summary>Optional display names, by seat. Missing entries default to "Player N".</summary>
        public IList<string> PlayerNames { get; set; }

        /// <summary>Starting house rules; the host can change them later with <see cref="SetHouseRules"/>.</summary>
        public HouseRules Rules { get; set; } = new HouseRules();
    }

    public interface IDice
    {
        /// <summary>Total of two six-sided dice (2-12).</summary>
        int Roll();
    }

    public sealed class RngDice : IDice
    {
        readonly IRandom _rng;
        public RngDice(IRandom rng) => _rng = rng;
        public int Roll() => _rng.Next(6) + _rng.Next(6) + 2;
    }

    public sealed class ActionResult
    {
        public bool Ok { get; }
        public string Error { get; }
        public IReadOnlyList<string> Events { get; }

        ActionResult(bool ok, string error, IReadOnlyList<string> events)
        {
            Ok = ok;
            Error = error;
            Events = events;
        }

        internal static ActionResult Fail(string error) => new ActionResult(false, error, Array.Empty<string>());
        internal static ActionResult Success(IReadOnlyList<string> events) => new ActionResult(true, null, events);
    }

    /// <summary>
    /// The authoritative rules engine: owns all game state and mutates it only through <see cref="Apply"/>.
    /// Pure data and logic, so it runs identically offline (hot-seat) and on a Netcode host.
    /// </summary>
    public sealed partial class Game
    {
        /// <summary>The player allowed to change house rules (the lobby host).</summary>
        public const int HostPlayer = 0;

        readonly IRandom _rng;
        readonly IDice _dice;
        readonly List<Player> _players = new List<Player>();
        readonly Dictionary<Vertex, Building> _buildings = new Dictionary<Vertex, Building>();
        readonly Dictionary<Edge, int> _roads = new Dictionary<Edge, int>();
        readonly List<DeckCard> _deck = new List<DeckCard>();
        readonly List<string> _events = new List<string>();
        readonly List<int> _setupOrder = new List<int>();
        readonly Dictionary<int, int> _discards = new Dictionary<int, int>();
        List<int> _stealCandidates = new List<int>();

        int _mirrorDeckCount = -1;
        int _setupIndex;
        Vertex _lastSetupVertex;
        Phase _raiderReturn = Phase.Main;
        bool _devPlayedThisTurn;
        int _freeRoads;

        public Board Board { get; }
        public GameConfig Config { get; }

        /// <summary>True for a client-side copy rebuilt from a snapshot. It can't apply commands.</summary>
        public bool IsMirror { get; private set; }

        /// <summary>The rules currently in force (a private copy; change them via <see cref="SetHouseRules"/>).</summary>
        public HouseRules Rules { get; private set; }

        public IReadOnlyList<Player> Players => _players;
        public IReadOnlyDictionary<Vertex, Building> Buildings => _buildings;
        public IReadOnlyDictionary<Edge, int> RoadOwners => _roads;

        public ResourceSet Bank { get; private set; }
        public Phase Phase { get; private set; }
        public int CurrentPlayer { get; private set; }
        public int Turn { get; private set; }
        public int LastRoll { get; private set; }
        public Hex RaiderHex { get; private set; }
        public int Winner { get; private set; } = -1;
        public int GreatRoadHolder { get; private set; } = -1;
        public int GrandArmyHolder { get; private set; } = -1;
        public TradeOffer PendingTrade { get; private set; }
        public int DevDeckCount => _mirrorDeckCount >= 0 ? _mirrorDeckCount : _deck.Count;

        /// <summary>Players who still owe a discard after a 7, and how many cards each owes.</summary>
        public IReadOnlyDictionary<int, int> PendingDiscards => _discards;

        /// <summary>Players the mover may steal from during the Steal phase.</summary>
        public IReadOnlyList<int> StealCandidates => _stealCandidates;

        public int FreeRoadsLeft => _freeRoads;

        /// <summary>True once the current player has played an action card this turn.</summary>
        public bool ActionCardPlayedThisTurn => _devPlayedThisTurn;

        public Game(GameConfig config, IDice dice = null)
            : this(BoardGenerator.Generate(config.Board), config, dice)
        {
        }

        public Game(Board board, GameConfig config, IDice dice = null)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.PlayerCount < 2 || config.PlayerCount > 6)
                throw new ArgumentOutOfRangeException(nameof(config), "PlayerCount must be 2-6.");

            HouseRules rules = (config.Rules ?? new HouseRules()).Clone();
            string rulesError = rules.Validate();
            if (rulesError != null) throw new ArgumentException(rulesError, nameof(config));

            Board = board;
            Config = config;
            Rules = rules;
            _rng = config.Random ?? new Rng(config.Seed);
            _dice = dice ?? new RngDice(_rng);

            for (int i = 0; i < config.PlayerCount; i++)
            {
                string name = config.PlayerNames != null && i < config.PlayerNames.Count ? config.PlayerNames[i] : null;
                _players.Add(new Player(i, string.IsNullOrWhiteSpace(name) ? $"Player {i + 1}" : name));
            }

            // Bank and action deck scale up with board size so bigger boards don't run dry.
            int scale = Math.Max(1, (int)Math.Ceiling(board.Tiles.Count / 19.0));
            Bank = new ResourceSet(19 * scale, 19 * scale, 19 * scale, 19 * scale, 19 * scale);
            AddCards(ActionCard.Soldier, 14 * scale);
            AddCards(ActionCard.VictoryPoint, 5 * scale);
            AddCards(ActionCard.Engineers, 2 * scale);
            AddCards(ActionCard.Harvest, 2 * scale);
            AddCards(ActionCard.Plunder, 2 * scale);
            AddEffectCards();
            _rng.Shuffle(_deck);

            // Snake draft: 0..n-1 then n-1..0.
            for (int i = 0; i < config.PlayerCount; i++) _setupOrder.Add(i);
            for (int i = config.PlayerCount - 1; i >= 0; i--) _setupOrder.Add(i);

            RaiderHex = board.RaiderStart;
            Phase = Phase.SetupVillage;
            CurrentPlayer = _setupOrder[0];
            Turn = 0;
        }

        void AddCards(ActionCard card, int count)
        {
            for (int i = 0; i < count; i++) _deck.Add(DeckCard.Of(card));
        }

        // ---- Command entry point -------------------------------------------------------------------

        public ActionResult Apply(Command command)
        {
            if (IsMirror) return ActionResult.Fail("This is a read-only view of the game.");
            _events.Clear();
            string error = Dispatch(command);
            if (error != null) return ActionResult.Fail(error);

            RefreshAwards();
            CheckWinner();
            return ActionResult.Success(_events.ToArray());
        }

        string Dispatch(Command command)
        {
            if (command == null) return "No command.";
            if (Phase == Phase.GameOver) return "The game is over.";
            if (command.Player < 0 || command.Player >= _players.Count) return "Unknown player.";
            if (_players[command.Player].Eliminated) return "You're out of the game.";

            switch (command)
            {
                case SetupVillage c: return DoSetupVillage(c);
                case SetupRoad c: return DoSetupRoad(c);
                case RollDice c: return DoRoll(c);
                case DiscardCards c: return DoDiscard(c);
                case MoveRaider c: return DoMoveRaider(c);
                case StealFrom c: return DoSteal(c);
                case BuildRoad c: return DoBuildRoad(c);
                case BuildVillage c: return DoBuildVillage(c);
                case BuildCity c: return DoBuildCity(c);
                case BuyActionCard c: return DoBuyActionCard(c);
                case PlaySoldier c: return DoPlaySoldier(c);
                case PlayEngineers c: return DoPlayEngineers(c);
                case PlayHarvest c: return DoPlayHarvest(c);
                case PlayPlunder c: return DoPlayPlunder(c);
                case BankTrade c: return DoBankTrade(c);
                case ProposeTrade c: return DoProposeTrade(c);
                case AcceptTrade c: return DoAcceptTrade(c);
                case CancelTrade c: return DoCancelTrade(c);
                case EndTurn c: return DoEndTurn(c);
                case SetHouseRules c: return DoSetHouseRules(c);
                default: return "Unsupported command.";
            }
        }

        // ---- Shared helpers ------------------------------------------------------------------------

        void Log(string message) => _events.Add(message);

        string NotYourTurn(Command c) => c.Player == CurrentPlayer ? null : "It is not your turn.";

        string RequirePhase(Phase expected, Command c)
        {
            if (Phase != expected) return $"You can't do that during {Phase}.";
            return NotYourTurn(c);
        }

        /// <summary>Hand -> bank.</summary>
        void Pay(Player p, ResourceSet cost)
        {
            p.Hand -= cost;
            Bank += cost;
        }

        /// <summary>Bank -> hand.</summary>
        void Receive(Player p, ResourceSet amount)
        {
            Bank -= amount;
            p.Hand += amount;
        }

        void PlaceRoad(Player p, Edge edge)
        {
            _roads[edge] = p.Id;
            p.RoadSet.Add(edge);
        }

        void PlaceVillage(Player p, Vertex v)
        {
            _buildings[v] = new Building(p.Id, false);
            p.VillageSet.Add(v);
        }

        void PlaceCity(Player p, Vertex v)
        {
            _buildings[v] = new Building(p.Id, true);
            p.VillageSet.Remove(v);
            p.CitySet.Add(v);
        }

        // ---- Legal-move queries (for UI highlighting and bots) -------------------------------------

        public bool IsVillageSpotFree(Vertex v) =>
            Board.HasVertex(v) && !_buildings.ContainsKey(v) && !Board.AdjacentVertices(v).Any(_buildings.ContainsKey);

        public IEnumerable<Vertex> LegalSetupVertices() => Board.Vertices.Where(IsVillageSpotFree);

        public IEnumerable<Edge> LegalSetupRoadEdges() =>
            Board.EdgesOf(_lastSetupVertex).Where(e => !_roads.ContainsKey(e));

        public IEnumerable<Vertex> LegalVillageVertices(int playerId) =>
            Board.Vertices.Where(v => IsVillageSpotFree(v) && HasOwnRoadAt(playerId, v));

        public IEnumerable<Edge> LegalRoadEdges(int playerId) =>
            Board.Edges.Where(e => !_roads.ContainsKey(e) && IsRoadConnected(playerId, e));

        public IEnumerable<Vertex> LegalCityVertices(int playerId) => _players[playerId].VillageSet;

        bool HasOwnRoadAt(int playerId, Vertex v) =>
            Board.EdgesOf(v).Any(e => _roads.TryGetValue(e, out int owner) && owner == playerId);

        /// <summary>
        /// A new road must touch one of the player's buildings, or continue one of their roads through a
        /// corner that no opponent has built on.
        /// </summary>
        bool IsRoadConnected(int playerId, Edge edge)
        {
            foreach (Vertex v in edge.Endpoints())
            {
                if (_buildings.TryGetValue(v, out Building b))
                {
                    if (b.Owner == playerId) return true;
                    continue; // opponent building blocks passage through this corner
                }

                foreach (Edge other in Board.EdgesOf(v))
                {
                    if (other != edge && _roads.TryGetValue(other, out int owner) && owner == playerId) return true;
                }
            }
            return false;
        }

        /// <summary>Best bank ratio for trading away <paramref name="give"/> (defaults 4, or 3 / 2 with a generic / matching port).</summary>
        public int GetBankRatio(int playerId, Resource give)
        {
            int ratio = Rules.BankRatio;
            Player p = _players[playerId];
            foreach (Vertex v in p.VillageSet.Concat(p.CitySet))
            {
                Port port = Board.PortAt(v);
                if (port == null) continue;
                if (port.IsGeneric) ratio = Math.Min(ratio, Rules.GenericPortRatio);
                else if (port.Resource == give) ratio = Math.Min(ratio, Rules.ResourcePortRatio);
            }
            return ratio;
        }

        // ---- Test hooks (internal; visible to the test assembly only) ------------------------------

        internal void GrantResources(int playerId, ResourceSet amount) => Receive(_players[playerId], amount);

        internal void GrantActionCard(int playerId, ActionCard card)
        {
            _players[playerId].Dev[(int)card]++;
            int at = _deck.FindIndex(d => !d.IsEffect && d.Dev == card);
            if (at >= 0) _deck.RemoveAt(at);
        }

        internal void ForceVillage(int playerId, Vertex v) => PlaceVillage(_players[playerId], v);
        internal void ForceCity(int playerId, Vertex v) => PlaceCity(_players[playerId], v);
        internal void ForceRoad(int playerId, Edge e) => PlaceRoad(_players[playerId], e);
        internal void ForcePhase(Phase phase) => Phase = phase;
        internal void PutOnDeck(EffectCard card) => _deck.Add(DeckCard.Of(card));
        internal void PutOnDeck(ActionCard card) => _deck.Add(DeckCard.Of(card));
        internal void ForceRefresh() => RefreshAwards();
    }
}
