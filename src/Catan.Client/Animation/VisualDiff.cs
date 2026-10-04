using System.Collections.Generic;
using System.Linq;
using Catan.Core;

namespace Catan.Client.Animation
{
    /// <summary>
    /// Works out what to animate by comparing two <see cref="GameView"/>s. It never looks at commands or engine
    /// internals, so the same code serves a local game and an online one where each server snapshot becomes a
    /// new view. If two snapshots arrive at once, or one is missed, the worst case is a skipped animation; the
    /// board itself always shows the latest state.
    /// </summary>
    public static class VisualDiff
    {
        sealed class Unit
        {
            public Place Place;
            public Resource? Kind;
        }

        public static List<VisualEvent> Between(GameView before, GameView after)
        {
            var events = new List<VisualEvent>();
            if (before == null || after == null || before.Seats.Count != after.Seats.Count) return events;
            int viewer = before.Viewer;

            // ---- Turn and dice ----------------------------------------------------------------------
            bool turnChanged = after.Turn != before.Turn || after.CurrentPlayer != before.CurrentPlayer;
            if (turnChanged && after.Phase != Phase.GameOver) events.Add(new TurnStarted(after.CurrentPlayer, after.Turn));

            bool rolled = before.Phase == Phase.Roll && after.Phase != Phase.Roll && after.Turn == before.Turn
                && after.LastRoll >= 2 && after.Seats[after.CurrentPlayer].Knights == before.Seats[before.CurrentPlayer].Knights
                && (after.Phase == Phase.Main || after.Phase == Phase.Discard || after.Phase == Phase.MoveRobber);
            if (rolled) events.Add(new DiceRolled(after.CurrentPlayer, after.LastRoll));

            // ---- Development cards played --------------------------------------------------------------
            var drawn = new List<VisualEvent>();
            for (int i = 0; i < after.Seats.Count; i++)
            {
                SeatView a = before.Seats[i], b = after.Seats[i];
                int delta = b.DevCount - a.DevCount;
                for (int k = 0; k < -delta; k++)
                {
                    DevCard? card = b.Knights > a.Knights ? DevCard.Knight : ChangedCard(a.Dev, b.Dev, decreased: true);
                    events.Add(new DevCardPlayed(i, card));
                }
                for (int k = 0; k < delta; k++)
                {
                    DevCard? card = i == viewer ? ChangedCard(a.Dev, b.Dev, decreased: false) : null;
                    drawn.Add(new DevCardDrawn(i, card));
                }
            }

            // ---- Board ----------------------------------------------------------------------------------
            if (after.Robber != before.Robber) events.Add(new RobberMoved(before.Robber, after.Robber));

            var newSettlements = new List<(int Owner, Vertex At)>();
            foreach (var kv in after.Buildings)
            {
                before.Buildings.TryGetValue(kv.Key, out Building old);
                bool existed = before.Buildings.ContainsKey(kv.Key);
                if (kv.Value.IsCity && (!existed || !old.IsCity)) events.Add(new CityPlaced(kv.Value.Owner, kv.Key));
                else if (!kv.Value.IsCity && !existed)
                {
                    events.Add(new SettlementPlaced(kv.Value.Owner, kv.Key));
                    newSettlements.Add((kv.Value.Owner, kv.Key));
                }
            }
            foreach (var kv in after.Roads)
            {
                if (!before.Roads.ContainsKey(kv.Key)) events.Add(new RoadPlaced(kv.Value, kv.Key));
            }

            // ---- Cards ----------------------------------------------------------------------------------
            events.AddRange(CardFlows(before, after, rolled, newSettlements, viewer));
            events.AddRange(drawn);

            // ---- House rules --------------------------------------------------------------------------------
            List<string> ruleChanges = RuleChanges(before.Rules, after.Rules);
            if (ruleChanges.Count > 0) events.Add(new RulesChanged(ruleChanges));

            // ---- Awards and the end -----------------------------------------------------------------------
            if (after.LongestRoad >= 0 && after.LongestRoad != before.LongestRoad) events.Add(new AwardTaken(Award.LongestRoad, after.LongestRoad));
            if (after.LargestArmy >= 0 && after.LargestArmy != before.LargestArmy) events.Add(new AwardTaken(Award.LargestArmy, after.LargestArmy));
            if (after.Winner >= 0 && before.Winner < 0) events.Add(new GameWon(after.Winner));

            return events;
        }

        /// <summary>
        /// Lists every house rule that differs, by reading the rule properties rather than naming them, so rules
        /// added later (or by the server) show up without changes here.
        /// </summary>
        static List<string> RuleChanges(HouseRules a, HouseRules b)
        {
            var changes = new List<string>();
            if (a == null || b == null) return changes;
            foreach (System.Reflection.PropertyInfo p in typeof(HouseRules).GetProperties())
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                object x = p.GetValue(a), y = p.GetValue(b);
                if (Equals(x, y) || (x is int[] xs && y is int[] ys && xs.SequenceEqual(ys))) continue;
                changes.Add($"{Words(p.Name)}: {Show(x)} → {Show(y)}");
            }
            return changes;
        }

        static string Show(object v) =>
            v is bool on ? (on ? "on" : "off") :
            v is int[] counts ? string.Join(" ", counts) :
            v?.ToString() ?? "none";

        /// <summary>"VictoryPoints" → "Victory points".</summary>
        static string Words(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ').Append(char.ToLowerInvariant(c));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static DevCard? ChangedCard(int[] before, int[] after, bool decreased)
        {
            if (before == null || after == null) return null;
            for (int k = 0; k < before.Length; k++)
            {
                if (decreased ? after[k] < before[k] : after[k] > before[k]) return (DevCard)k;
            }
            return null;
        }

        /// <summary>
        /// Splits every hand and bank change into single cards leaving somewhere ("sources") and arriving
        /// somewhere ("sinks"), then pairs them up. Production is matched first because the board says exactly
        /// which tile paid out, so those cards can fly from the tile itself.
        /// </summary>
        static IEnumerable<VisualEvent> CardFlows(GameView before, GameView after, bool rolled, List<(int Owner, Vertex At)> newSettlements, int viewer)
        {
            var sources = new List<Unit>();
            var sinks = new List<Unit>();

            void Add(List<Unit> list, Place place, Resource? kind, int count)
            {
                for (int i = 0; i < count; i++) list.Add(new Unit { Place = place, Kind = kind });
            }

            for (int i = 0; i < after.Seats.Count; i++)
            {
                SeatView a = before.Seats[i], b = after.Seats[i];
                if (a.Hand.HasValue && b.Hand.HasValue)
                {
                    foreach (Resource r in ResourceSet.Types)
                    {
                        int d = b.Hand.Value[r] - a.Hand.Value[r];
                        Add(d < 0 ? sources : sinks, Place.Seat(i), r, System.Math.Abs(d));
                    }
                }
                else
                {
                    int d = b.HandCount - a.HandCount;
                    Add(d < 0 ? sources : sinks, Place.Seat(i), null, System.Math.Abs(d));
                }
            }
            foreach (Resource r in ResourceSet.Types)
            {
                int d = after.Bank[r] - before.Bank[r];
                Add(d < 0 ? sources : sinks, Place.Bank, r, System.Math.Abs(d));
            }

            var flights = new List<(Place From, Place To, Resource? Kind)>();

            // Takes one matching unit out of the list, preferring an exact resource match.
            Unit Take(List<Unit> list, System.Func<Unit, bool> where)
            {
                Unit u = list.FirstOrDefault(where);
                if (u != null) list.Remove(u);
                return u;
            }

            bool IsBank(Unit u, Resource r) => u.Place.Kind == PlaceKind.Bank && u.Kind == r;
            bool IsSeat(Unit u, int seat, Resource r) => u.Place.Kind == PlaceKind.Player && u.Place.Player == seat && (u.Kind == r || u.Kind == null);

            // Production: tile -> player, for each card the bank actually paid out.
            if (rolled && after.LastRoll != 7)
            {
                foreach (Tile tile in after.Game.Board.Tiles)
                {
                    if (tile.Number != after.LastRoll || tile.Hex == before.Robber) continue;
                    for (int c = 0; c < 6; c++)
                    {
                        if (!before.Buildings.TryGetValue(Vertex.OfCorner(tile.Hex, c), out Building bld)) continue;
                        for (int n = 0; n < (bld.IsCity ? 2 : 1); n++)
                        {
                            Unit seat = sinks.FirstOrDefault(u => IsSeat(u, bld.Owner, tile.Resource));
                            Unit bank = sources.FirstOrDefault(u => IsBank(u, tile.Resource));
                            if (seat == null || bank == null) continue;
                            sinks.Remove(seat);
                            sources.Remove(bank);
                            flights.Add((Place.Tile(tile.Hex), seat.Place, tile.Resource));
                        }
                    }
                }
            }

            // Starting resources from the second setup settlement: tile -> player.
            foreach (var (owner, at) in newSettlements)
            {
                foreach (Tile tile in after.Game.Board.TilesAround(at))
                {
                    if (tile.IsDesert) continue;
                    Unit seat = sinks.FirstOrDefault(u => IsSeat(u, owner, tile.Resource));
                    Unit bank = sources.FirstOrDefault(u => IsBank(u, tile.Resource));
                    if (seat == null || bank == null) continue;
                    sinks.Remove(seat);
                    sources.Remove(bank);
                    flights.Add((Place.Tile(tile.Hex), seat.Place, tile.Resource));
                }
            }

            // Bank or port trade by a player whose hand we can't see (an opponent, online): we only know their
            // hand's net change, but the bank's counts are public. When the bank both gained and lost cards and
            // at most one hidden hand changed, that seat made the trade, so the bank's own changes say exactly
            // what went each way.
            if (sinks.Any(u => u.Place.Kind == PlaceKind.Bank) && sources.Any(u => u.Place.Kind == PlaceKind.Bank))
            {
                var hidden = after.Seats.Where(s => !s.Hand.HasValue).ToList();
                var changed = hidden.Where(s => s.HandCount != before.Seats[s.Id].HandCount).ToList();
                bool knownSeatMoved = sinks.Concat(sources).Any(u => u.Place.Kind == PlaceKind.Player && u.Kind.HasValue);
                int trader = changed.Count == 1 ? changed[0].Id
                    : changed.Count == 0 && hidden.Any(s => s.Id == after.CurrentPlayer) ? after.CurrentPlayer : -1;
                if (trader >= 0 && !knownSeatMoved)
                {
                    Place seat = Place.Seat(trader);
                    sinks.RemoveAll(u => u.Place == seat);
                    sources.RemoveAll(u => u.Place == seat);
                    foreach (Unit u in sinks.Where(u => u.Place.Kind == PlaceKind.Bank).ToList())
                    {
                        sinks.Remove(u);
                        flights.Add((seat, Place.Bank, u.Kind));
                    }
                    foreach (Unit u in sources.Where(u => u.Place.Kind == PlaceKind.Bank).ToList())
                    {
                        sources.Remove(u);
                        flights.Add((Place.Bank, seat, u.Kind));
                    }
                }
            }

            // Everything else: pair each arrival with a departure, exact resource first.
            foreach (Unit sink in sinks.ToList())
            {
                Unit src = sink.Kind.HasValue
                    ? Take(sources, u => u.Kind == sink.Kind && u.Place != sink.Place) ?? Take(sources, u => u.Kind == null && u.Place != sink.Place)
                    : Take(sources, u => u.Place.Kind == PlaceKind.Bank) ?? Take(sources, u => u.Place != sink.Place);
                if (src == null) continue;

                Resource? kind = sink.Kind ?? src.Kind;
                bool betweenPlayers = src.Place.Kind == PlaceKind.Player && sink.Place.Kind == PlaceKind.Player;
                if (betweenPlayers && src.Place.Player != viewer && sink.Place.Player != viewer) kind = null; // a private swap
                flights.Add((src.Place, sink.Place, kind));
            }

            return flights
                .GroupBy(f => f)
                .Select(g => (VisualEvent)new CardsMoved(g.Key.From, g.Key.To, g.Key.Kind, g.Count()));
        }
    }
}
