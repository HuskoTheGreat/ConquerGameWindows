using System;
using System.Collections.Generic;
using System.Linq;

namespace Catan.Core.Bots
{
    public enum BotDifficulty
    {
        /// <summary>Random legal moves. Builds when it can, never trades.</summary>
        Easy,

        /// <summary>Sensible: good starting spots, builds toward a target, uses the bank and robber well.</summary>
        Normal,

        /// <summary>Plans harder: weighs scarce resources and ports, plays development cards, drives Largest Army.</summary>
        Hard,
    }

    /// <summary>
    /// A computer player for one seat. Ask it <see cref="Decide"/> after every change; it returns the command it
    /// wants to send now (it plays through <see cref="Game.Apply"/> like anyone else), or null when it isn't its
    /// move. It only looks at what its own seat may know: its hand, the board, and public counts.
    /// </summary>
    public sealed class BotPlayer
    {
        const int MaxActionsPerTurn = 40;
        const int MaxBankTradesPerTurn = 3;

        readonly Random _rnd;
        int _turnSeen = -1;
        int _actions;
        int _bankTrades;
        TradeOffer _offerSeen;

        public int Seat { get; }
        public BotDifficulty Difficulty { get; }

        public BotPlayer(int seat, BotDifficulty difficulty, int? seed = null)
        {
            Seat = seat;
            Difficulty = difficulty;
            _rnd = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public static string Describe(BotDifficulty d) => d switch
        {
            BotDifficulty.Easy => "Easy",
            BotDifficulty.Hard => "Hard",
            _ => "Normal",
        };

        bool Smart => Difficulty != BotDifficulty.Easy;
        bool Hard => Difficulty == BotDifficulty.Hard;

        // ---- Entry points --------------------------------------------------------------------------

        /// <summary>The command this bot wants to send right now, or null if it has nothing to do.</summary>
        public Command Decide(Game g)
        {
            Player me = g.Players[Seat];
            if (g.Phase == Phase.GameOver || me.Eliminated) return null;

            if (g.Turn != _turnSeen)
            {
                _turnSeen = g.Turn;
                _actions = 0;
                _bankTrades = 0;
            }

            if (g.Phase == Phase.Discard)
                return g.PendingDiscards.TryGetValue(Seat, out int owe) ? new DiscardCards(Seat, ChooseDiscard(g, owe)) : null;

            if (g.PendingTrade != null && g.PendingTrade != _offerSeen && g.PendingTrade.From != Seat)
            {
                _offerSeen = g.PendingTrade;
                if (WantsTrade(g, g.PendingTrade)) return new AcceptTrade(Seat);
            }

            if (g.CurrentPlayer != Seat) return null;

            switch (g.Phase)
            {
                case Phase.SetupSettlement: return new SetupSettlement(Seat, ChooseSetupVertex(g));
                case Phase.SetupRoad: return new SetupRoad(Seat, ChooseRoad(g, g.LegalSetupRoadEdges().ToList()));
                case Phase.Roll:
                    if (Smart && ShouldPlayKnight(g, beforeRoll: true)) return new PlayKnight(Seat);
                    return new RollDice(Seat);
                case Phase.MoveRobber: return new MoveRobber(Seat, ChooseRobberHex(g));
                case Phase.Steal: return new StealFrom(Seat, ChooseVictim(g));
                case Phase.RoadBuilding:
                {
                    var edges = g.LegalRoadEdges(Seat).ToList();
                    return edges.Count > 0 ? new BuildRoad(Seat, ChooseRoad(g, edges)) : new EndTurn(Seat);
                }
                case Phase.Main:
                    if (++_actions > MaxActionsPerTurn) return new EndTurn(Seat);
                    return MainMove(g);
                default:
                    return null;
            }
        }

        /// <summary>A simple always-legal move, for when the engine refused what <see cref="Decide"/> chose.</summary>
        public Command Fallback(Game g)
        {
            if (g.Phase == Phase.Discard && g.PendingDiscards.TryGetValue(Seat, out int owe))
                return new DiscardCards(Seat, RandomCards(g.Players[Seat].Hand, owe));
            if (g.CurrentPlayer != Seat) return null;
            switch (g.Phase)
            {
                case Phase.SetupSettlement: return new SetupSettlement(Seat, Pick(g.LegalSetupVertices().ToList()));
                case Phase.SetupRoad: return new SetupRoad(Seat, Pick(g.LegalSetupRoadEdges().ToList()));
                case Phase.Roll: return new RollDice(Seat);
                case Phase.MoveRobber: return new MoveRobber(Seat, Pick(g.LegalRobberHexes().ToList()));
                case Phase.Steal: return new StealFrom(Seat, Pick(g.StealCandidates.ToList()));
                case Phase.RoadBuilding:
                {
                    var edges = g.LegalRoadEdges(Seat).ToList();
                    return edges.Count > 0 ? new BuildRoad(Seat, Pick(edges)) : null;
                }
                case Phase.Main: return new EndTurn(Seat);
                default: return null;
            }
        }

        // ---- Main phase ----------------------------------------------------------------------------

        Command MainMove(Game g)
        {
            Player me = g.Players[Seat];
            if (!Smart) return EasyMainMove(g, me);

            Command card = ChooseDevCardPlay(g, me);
            if (card != null) return card;

            if (me.Hand.Contains(Costs.City) && me.CitiesLeft > 0 && me.Settlements.Count > 0)
                return new BuildCity(Seat, me.Settlements.OrderByDescending(v => SpotValue(g, v)).First());

            var spots = me.SettlementsLeft > 0 ? g.LegalSettlementVertices(Seat).ToList() : new List<Vertex>();
            if (me.Hand.Contains(Costs.Settlement) && spots.Count > 0)
                return new BuildSettlement(Seat, spots.OrderByDescending(v => SpotValue(g, v)).First());

            // Roads only when they lead somewhere: no settlement spot open yet, and a good one in reach.
            if (me.Hand.Contains(Costs.Road) && me.RoadsLeft > 0 && spots.Count == 0 && me.SettlementsLeft > 0)
            {
                Edge? toward = RoadTowardTarget(g);
                if (toward.HasValue) return new BuildRoad(Seat, toward.Value);
            }

            if (me.Hand.Contains(Costs.DevCard) && g.DevDeckCount > 0 && WantsDevCard(g, me, spots.Count > 0))
                return new BuyDevCard(Seat);

            Command trade = ChooseBankTrade(g, me, spots.Count > 0);
            if (trade != null) return trade;

            return new EndTurn(Seat);
        }

        Command EasyMainMove(Game g, Player me)
        {
            // Easy still takes a city or settlement when it can afford one (just at a random spot), so games end.
            if (me.Hand.Contains(Costs.City) && me.CitiesLeft > 0 && me.Settlements.Count > 0)
                return new BuildCity(Seat, Pick(me.Settlements.ToList()));
            var spots = g.LegalSettlementVertices(Seat).ToList();
            if (me.Hand.Contains(Costs.Settlement) && me.SettlementsLeft > 0 && spots.Count > 0)
                return new BuildSettlement(Seat, Pick(spots));

            var options = new List<Command>();
            var roads = g.LegalRoadEdges(Seat).ToList();
            if (me.Hand.Contains(Costs.Road) && me.RoadsLeft > 0 && roads.Count > 0 && _rnd.Next(2) == 0)
                options.Add(new BuildRoad(Seat, Pick(roads)));
            if (me.Hand.Contains(Costs.DevCard) && g.DevDeckCount > 0 && _rnd.Next(2) == 0)
                options.Add(new BuyDevCard(Seat));
            if (CanPlayDevCard(g) && me.DevCardsUsable(DevCard.Knight) > 0 && _rnd.Next(3) == 0)
                options.Add(new PlayKnight(Seat));
            if (CanPlayDevCard(g) && me.DevCardsUsable(DevCard.RoadBuilding) > 0 && roads.Count > 0)
                options.Add(new PlayRoadBuilding(Seat));

            if (options.Count > 0 && _rnd.Next(10) < 8) return Pick(options);
            if (_rnd.Next(2) == 0)
            {
                Command trade = ChooseBankTrade(g, me, spots.Count > 0);
                if (trade != null) return trade;
            }
            return new EndTurn(Seat);
        }

        bool CanPlayDevCard(Game g) => !(g.Rules.OneDevCardPerTurn && g.DevCardPlayedThisTurn);

        Command ChooseDevCardPlay(Game g, Player me)
        {
            if (!CanPlayDevCard(g)) return null;

            if (me.DevCardsUsable(DevCard.Knight) > 0 && ShouldPlayKnight(g, beforeRoll: false)) return new PlayKnight(Seat);

            if (me.DevCardsUsable(DevCard.RoadBuilding) > 0 && me.RoadsLeft > 0 && me.SettlementsLeft > 0 &&
                RoadTowardTarget(g).HasValue)
                return new PlayRoadBuilding(Seat);

            if (me.DevCardsUsable(DevCard.YearOfPlenty) > 0)
            {
                ResourceSet missing = Missing(me.Hand, Goal(g, me));
                if (missing.Total > 0 && missing.Total <= 2)
                {
                    var need = Expand(missing);
                    Resource a = need[0], b = need.Count > 1 ? need[1] : need[0];
                    if (g.Bank.Contains(ResourceSet.Of(a).With(b, 1))) return new PlayYearOfPlenty(Seat, a, b);
                }
            }

            if (Hard && me.DevCardsUsable(DevCard.Monopoly) > 0)
            {
                int othersCards = g.ActivePlayers.Where(p => p.Id != Seat).Sum(p => p.HandCount);
                ResourceSet missing = Missing(me.Hand, Goal(g, me));
                if (othersCards >= 7 && missing.Total > 0)
                    return new PlayMonopoly(Seat, ResourceSet.Types.OrderByDescending(r => missing[r]).First());
            }
            return null;
        }

        bool ShouldPlayKnight(Game g, bool beforeRoll)
        {
            Player me = g.Players[Seat];
            if (me.DevCardsUsable(DevCard.Knight) == 0 || !CanPlayDevCard(g)) return false;
            if (TouchesMine(g, g.RobberHex)) return true;
            if (!Hard || beforeRoll) return false;

            // Hard pushes for Largest Army when one more knight would take or secure it.
            int best = g.ActivePlayers.Where(p => p.Id != Seat).Select(p => p.KnightsPlayed).DefaultIfEmpty(0).Max();
            return g.LargestArmyHolder != Seat && me.KnightsPlayed + 1 >= g.Rules.LargestArmyMinimum && me.KnightsPlayed + 1 > best;
        }

        bool WantsDevCard(Game g, Player me, bool settlementOpen)
        {
            // Normal buys cards when there's nothing better to save for; Hard also buys them with spare ore.
            if (me.CitiesLeft > 0 && me.Settlements.Count > 0 && Missing(me.Hand - Costs.DevCard, Costs.City).Total <= 1 && !Hard)
                return false;
            if (settlementOpen && me.SettlementsLeft > 0) return Hard && me.Hand[Resource.Ore] >= 2;
            return true;
        }

        /// <summary>One bank trade that gets the next build within reach. Bounded per turn, so it can't loop.</summary>
        Command ChooseBankTrade(Game g, Player me, bool settlementOpen)
        {
            if (_bankTrades >= MaxBankTradesPerTurn) return null;
            if (!g.Rules.TradeAnytime && g.CurrentPlayer != Seat) return null;

            ResourceSet goal = Goal(g, me, settlementOpen);
            ResourceSet missing = Missing(me.Hand, goal);
            if (missing.Total == 0 || missing.Total > (Hard ? 2 : 1)) return null;

            Resource want = ResourceSet.Types.First(r => missing[r] > 0);
            if (g.Bank[want] == 0) return null;

            foreach (Resource give in ResourceSet.Types.OrderByDescending(r => me.Hand[r] - goal[r]))
            {
                if (give == want) continue;
                int ratio = g.GetBankRatio(Seat, give);
                if (me.Hand[give] - goal[give] >= ratio)
                {
                    _bankTrades++;
                    return new BankTrade(Seat, give, want);
                }
            }
            return null;
        }

        /// <summary>The next thing worth saving for: a city, else a settlement, else a development card.</summary>
        ResourceSet Goal(Game g, Player me, bool? settlementOpen = null)
        {
            bool open = settlementOpen ?? (me.SettlementsLeft > 0 && g.LegalSettlementVertices(Seat).Any());
            if (me.CitiesLeft > 0 && me.Settlements.Count > 0 && (Hard || !open)) return Costs.City;
            if (open) return Costs.Settlement;
            if (me.CitiesLeft > 0 && me.Settlements.Count > 0) return Costs.City;
            return Costs.DevCard;
        }

        // ---- Trading with players ------------------------------------------------------------------

        bool WantsTrade(Game g, TradeOffer offer)
        {
            if (!Smart) return false;
            Player me = g.Players[Seat];
            if (!me.Hand.Contains(offer.Want)) return false;

            // Never help someone about to win.
            if (g.PublicVictoryPoints(offer.From) >= g.Rules.VictoryPoints - 2) return false;
            if (offer.Give.Total + 1 < offer.Want.Total) return false;

            ResourceSet goal = Goal(g, me);
            double gain = Value(me.Hand, goal, offer.Give, receiving: true);
            double cost = Value(me.Hand, goal, offer.Want, receiving: false);
            return gain > cost + (Hard ? 0.5 : 0.0);
        }

        static double Value(ResourceSet hand, ResourceSet goal, ResourceSet cards, bool receiving)
        {
            double v = 0;
            foreach (Resource r in ResourceSet.Types)
            {
                int n = cards[r];
                if (n == 0) continue;
                int shortBy = goal[r] - hand[r];
                // Cards toward the goal are worth double; spares half.
                for (int i = 0; i < n; i++)
                {
                    bool useful = receiving ? shortBy - i > 0 : hand[r] - i <= goal[r];
                    v += useful ? 2.0 : 0.5;
                }
            }
            return v;
        }

        // ---- Placement -----------------------------------------------------------------------------

        Vertex ChooseSetupVertex(Game g)
        {
            var spots = g.LegalSetupVertices().ToList();
            if (!Smart) return Pick(spots);
            return spots.OrderByDescending(v => SpotValue(g, v)).First();
        }

        /// <summary>How good a corner is for this bot: production, variety, and (Hard) scarcity and ports.</summary>
        double SpotValue(Game g, Vertex v)
        {
            var have = MyProduction(g);
            var seen = new HashSet<Resource>();
            double score = 0;
            foreach (Tile t in g.Board.TilesAround(v))
            {
                if (t.IsDesert) continue;
                double pips = t.Pips * (t.Hex == g.RobberHex ? 0.5 : 1.0);
                double weight = 1.0;
                if (Hard)
                {
                    weight = Scarcity(g, t.Resource);
                    if (have[t.Resource] == 0) weight += 0.3; // something we don't produce yet
                }
                score += pips * weight;
                seen.Add(t.Resource);
            }
            score += seen.Count * (Hard ? 1.5 : 1.0);

            if (Hard)
            {
                Port port = g.Board.PortAt(v);
                if (port != null) score += port.IsGeneric ? 1.5 : (have[port.Resource] >= 5 ? 3.0 : 1.0);
            }
            return score;
        }

        /// <summary>Rare resources on this board are worth more (relative to the average).</summary>
        static double Scarcity(Game g, Resource r)
        {
            var pips = ResourceSet.Types.ToDictionary(x => x, x => 0);
            foreach (Tile t in g.Board.Tiles)
                if (!t.IsDesert) pips[t.Resource] += t.Pips;
            double avg = pips.Values.Average();
            return Math.Clamp(avg / Math.Max(1, pips[r]), 0.7, 1.5);
        }

        Dictionary<Resource, int> MyProduction(Game g)
        {
            var have = ResourceSet.Types.ToDictionary(r => r, r => 0);
            Player me = g.Players[Seat];
            foreach (Vertex v in me.Settlements.Concat(me.Cities))
            {
                int mult = me.Cities.Contains(v) ? 2 : 1;
                foreach (Tile t in g.Board.TilesAround(v))
                    if (!t.IsDesert) have[t.Resource] += t.Pips * mult;
            }
            return have;
        }

        Edge ChooseRoad(Game g, List<Edge> legal)
        {
            if (!Smart || legal.Count == 1) return Pick(legal);
            Edge? toward = RoadTowardTarget(g, legal);
            if (toward.HasValue) return toward.Value;
            // Nothing in reach: head for the best open area.
            return legal.OrderByDescending(e => e.Endpoints().Max(v => NearbyPotential(g, v))).First();
        }

        double NearbyPotential(Game g, Vertex v)
        {
            double best = g.IsSettlementSpotFree(v) ? SpotValue(g, v) : 0;
            foreach (Vertex w in g.Board.AdjacentVertices(v))
                if (g.IsSettlementSpotFree(w)) best = Math.Max(best, SpotValue(g, w) - 1);
            return best;
        }

        /// <summary>
        /// First road on the way to the best free settlement spot within three roads of our network (a
        /// breadth-first search that can't pass through opponents' buildings or roads).
        /// </summary>
        Edge? RoadTowardTarget(Game g, List<Edge> legal = null)
        {
            var legalSet = new HashSet<Edge>(legal ?? g.LegalRoadEdges(Seat));
            if (legalSet.Count == 0) return null;

            var firstStep = new Dictionary<Vertex, Edge>();
            var depth = new Dictionary<Vertex, int>();
            var queue = new Queue<Vertex>();
            foreach (Edge e in legalSet)
            {
                foreach (Vertex v in e.Endpoints())
                {
                    if (depth.ContainsKey(v) || IsMineOrConnected(g, v)) continue;
                    depth[v] = 1;
                    firstStep[v] = e;
                    queue.Enqueue(v);
                }
            }

            Vertex? best = null;
            double bestScore = double.MinValue;
            while (queue.Count > 0)
            {
                Vertex v = queue.Dequeue();
                if (g.IsSettlementSpotFree(v))
                {
                    double s = SpotValue(g, v) - 2.0 * depth[v];
                    if (s > bestScore)
                    {
                        bestScore = s;
                        best = v;
                    }
                }
                if (depth[v] >= 3 || g.Buildings.ContainsKey(v)) continue; // can't build past a building
                foreach (Edge e in g.Board.EdgesOf(v))
                {
                    if (g.RoadOwners.ContainsKey(e)) continue;
                    Vertex w = e.Endpoints().First(x => x != v);
                    if (depth.ContainsKey(w) || IsMineOrConnected(g, w)) continue;
                    depth[w] = depth[v] + 1;
                    firstStep[w] = firstStep[v];
                    queue.Enqueue(w);
                }
            }
            return best.HasValue ? firstStep[best.Value] : (Edge?)null;
        }

        /// <summary>A corner already on our network (a building of ours or the end of one of our roads).</summary>
        bool IsMineOrConnected(Game g, Vertex v)
        {
            if (g.Buildings.TryGetValue(v, out Building b) && b.Owner == Seat) return true;
            return g.Board.EdgesOf(v).Any(e => g.RoadOwners.TryGetValue(e, out int o) && o == Seat);
        }

        // ---- Robber and discards -------------------------------------------------------------------

        Hex ChooseRobberHex(Game g)
        {
            var hexes = g.LegalRobberHexes().ToList();
            if (!Smart) return Pick(hexes);
            return hexes.OrderByDescending(h => RobberValue(g, h)).First();
        }

        double RobberValue(Game g, Hex h)
        {
            Tile tile = g.Board.Tiles.First(t => t.Hex == h);
            double score = 0;
            bool canSteal = false;
            for (int i = 0; i < 6; i++)
            {
                if (!g.Buildings.TryGetValue(Vertex.OfCorner(h, i), out Building b)) continue;
                Player owner = g.Players[b.Owner];
                if (owner.Eliminated) continue;
                double hit = tile.Pips * (b.IsCity ? 2 : 1);
                if (b.Owner == Seat)
                {
                    score -= hit * 3;
                    continue;
                }
                score += hit * (1.0 + g.PublicVictoryPoints(b.Owner) / 4.0);
                if (owner.HandCount > 0) canSteal = true;
            }
            return score + (canSteal ? 2 : 0);
        }

        int ChooseVictim(Game g)
        {
            var candidates = g.StealCandidates.ToList();
            if (!Smart) return Pick(candidates);
            return candidates
                .OrderByDescending(id => g.PublicVictoryPoints(id))
                .ThenByDescending(id => g.Players[id].HandCount)
                .First();
        }

        ResourceSet ChooseDiscard(Game g, int owe)
        {
            ResourceSet hand = g.Players[Seat].Hand;
            if (!Smart) return RandomCards(hand, owe);

            ResourceSet goal = Goal(g, g.Players[Seat]);
            ResourceSet drop = ResourceSet.Empty;
            for (int i = 0; i < owe; i++)
            {
                ResourceSet left = hand - drop;
                // Drop from whatever we hold the most of beyond what the goal needs.
                Resource r = ResourceSet.Types.Where(x => left[x] > 0).OrderByDescending(x => left[x] - goal[x]).First();
                drop = drop.With(r, 1);
            }
            return drop;
        }

        // ---- Helpers -------------------------------------------------------------------------------

        bool TouchesMine(Game g, Hex h)
        {
            for (int i = 0; i < 6; i++)
                if (g.Buildings.TryGetValue(Vertex.OfCorner(h, i), out Building b) && b.Owner == Seat) return true;
            return false;
        }

        static ResourceSet Missing(ResourceSet hand, ResourceSet goal)
        {
            ResourceSet missing = ResourceSet.Empty;
            foreach (Resource r in ResourceSet.Types)
                if (goal[r] > hand[r]) missing = missing.With(r, goal[r] - hand[r]);
            return missing;
        }

        static List<Resource> Expand(ResourceSet s)
        {
            var list = new List<Resource>();
            foreach (Resource r in ResourceSet.Types)
                for (int i = 0; i < s[r]; i++) list.Add(r);
            return list;
        }

        ResourceSet RandomCards(ResourceSet hand, int count)
        {
            var cards = Expand(hand);
            ResourceSet picked = ResourceSet.Empty;
            for (int i = 0; i < count && cards.Count > 0; i++)
            {
                int k = _rnd.Next(cards.Count);
                picked = picked.With(cards[k], 1);
                cards.RemoveAt(k);
            }
            return picked;
        }

        T Pick<T>(IReadOnlyList<T> items) => items[_rnd.Next(items.Count)];
    }
}
