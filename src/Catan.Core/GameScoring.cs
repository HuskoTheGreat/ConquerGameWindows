using System.Collections.Generic;
using System.Linq;

namespace Catan.Core
{
    public sealed partial class Game
    {
        /// <summary>Points everyone can see: buildings plus Longest Road and Largest Army.</summary>
        public int PublicVictoryPoints(int playerId)
        {
            Player p = _players[playerId];
            int vp = p.SettlementSet.Count + 2 * p.CitySet.Count;
            if (LongestRoadHolder == playerId) vp += 2;
            if (LargestArmyHolder == playerId) vp += 2;
            return vp;
        }

        /// <summary>Public points plus hidden Victory Point cards.</summary>
        public int VictoryPoints(int playerId) =>
            PublicVictoryPoints(playerId) + _players[playerId].DevCardsTotal(DevCard.VictoryPoint);

        void CheckWinner()
        {
            if (Phase == Phase.SetupSettlement || Phase == Phase.SetupRoad || Phase == Phase.GameOver) return;
            if (VictoryPoints(CurrentPlayer) < Rules.VictoryPoints) return;

            Winner = CurrentPlayer;
            Phase = Phase.GameOver;
            Log($"{_players[Winner].Name} wins!");
        }

        /// <summary>Recomputes road lengths and the Longest Road / Largest Army holders.</summary>
        void RefreshAwards()
        {
            foreach (Player p in _players) p.LongestRoad = ComputeLongestRoad(p.Id);

            // Eliminated players can't hold awards.
            LongestRoadHolder = ResolveAward(LongestRoadHolder, Rules.LongestRoadMinimum, "Longest Road",
                pid => _players[pid].Eliminated ? 0 : _players[pid].LongestRoad);
            LargestArmyHolder = ResolveAward(LargestArmyHolder, Rules.LargestArmyMinimum, "Largest Army",
                pid => _players[pid].Eliminated ? 0 : _players[pid].KnightsPlayed);
        }

        /// <summary>
        /// The holder keeps an award while tied for the lead; otherwise it goes to the single leader at or
        /// above the minimum, or to nobody if the leaders are tied.
        /// </summary>
        int ResolveAward(int holder, int minimum, string name, System.Func<int, int> score)
        {
            int max = _players.Max(p => score(p.Id));
            if (holder >= 0 && score(holder) == max && max >= minimum) return holder;

            int next = -1;
            if (max >= minimum)
            {
                var leaders = _players.Where(p => score(p.Id) == max).ToList();
                if (leaders.Count == 1) next = leaders[0].Id;
            }

            if (next != holder && next >= 0) Log($"{_players[next].Name} takes {name}.");
            else if (next != holder) Log($"{name} is now unclaimed.");
            return next;
        }

        int ComputeLongestRoad(int playerId)
        {
            Player p = _players[playerId];
            if (p.RoadSet.Count == 0) return 0;

            var starts = new HashSet<Vertex>();
            foreach (Edge e in p.RoadSet)
            {
                foreach (Vertex v in e.Endpoints()) starts.Add(v);
            }

            int best = 0;
            var used = new HashSet<Edge>();
            foreach (Vertex v in starts) best = System.Math.Max(best, LongestFrom(playerId, v, used));
            return best;
        }

        /// <summary>Longest trail (no repeated edges) leaving <paramref name="at"/>; opponents' buildings stop it.</summary>
        int LongestFrom(int playerId, Vertex at, HashSet<Edge> used)
        {
            int best = 0;
            foreach (Edge edge in Board.EdgesOf(at))
            {
                if (used.Contains(edge) || !_roads.TryGetValue(edge, out int owner) || owner != playerId) continue;

                Vertex other = edge.Endpoints().First(v => v != at);
                int length = 1;
                bool blocked = _buildings.TryGetValue(other, out Building b) && b.Owner != playerId;
                if (!blocked)
                {
                    used.Add(edge);
                    length += LongestFrom(playerId, other, used);
                    used.Remove(edge);
                }
                best = System.Math.Max(best, length);
            }
            return best;
        }
    }
}
