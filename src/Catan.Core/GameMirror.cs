using Catan.Core.Net;

namespace Catan.Core
{
    public sealed partial class Game
    {
        internal static Game FromMirror(MirrorData m) => new Game(m);

        /// <summary>Builds the read-only client copy. Queries and scoring work; <see cref="Apply"/> is refused.</summary>
        Game(MirrorData m)
        {
            IsMirror = true;
            Board = m.Board;
            Config = new GameConfig { PlayerCount = m.Players.Count, Rules = m.Rules.Clone() };
            Rules = m.Rules.Clone();
            _rng = new Rng(0); // unused: a mirror never rolls or shuffles
            _dice = null;

            for (int i = 0; i < m.Players.Count; i++)
            {
                MirrorPlayer d = m.Players[i];
                bool mine = i == m.ViewerSeat;
                var p = new Player(i, d.Name)
                {
                    Hand = d.Hand,
                    KnightsPlayed = d.Knights,
                    LongestRoad = d.LongestRoad,
                };
                p.MirrorHandCount = mine ? -1 : d.HandCount;
                p.MirrorDevCount = mine ? -1 : d.DevCount;
                for (int k = 0; k < 5; k++)
                {
                    p.Dev[k] = d.Dev[k];
                    p.DevNew[k] = d.DevNew[k];
                }

                foreach (Vertex v in d.Settlements)
                {
                    p.SettlementSet.Add(v);
                    _buildings[v] = new Building(i, false);
                }
                foreach (Vertex v in d.Cities)
                {
                    p.CitySet.Add(v);
                    _buildings[v] = new Building(i, true);
                }
                foreach (Edge e in d.Roads)
                {
                    p.RoadSet.Add(e);
                    _roads[e] = i;
                }
                _players.Add(p);
            }

            Bank = m.Bank;
            Phase = m.Phase;
            CurrentPlayer = m.Current;
            Turn = m.Turn;
            LastRoll = m.LastRoll;
            RobberHex = m.Robber;
            Winner = m.Winner;
            LongestRoadHolder = m.LongestHolder;
            LargestArmyHolder = m.ArmyHolder;
            PendingTrade = m.Trade;
            _mirrorDeckCount = m.DeckCount;
            _devPlayedThisTurn = m.DevPlayed;
            _freeRoads = m.FreeRoads;
            foreach (var kv in m.Discards) _discards[kv.Key] = kv.Value;
            _stealCandidates = m.Steal;

            // During setup the road must touch the settlement just placed: the one with no road yet.
            if (Phase == Phase.SetupRoad)
            {
                foreach (Vertex v in _players[CurrentPlayer].SettlementSet)
                {
                    if (HasOwnRoadAt(CurrentPlayer, v)) continue;
                    _lastSetupVertex = v;
                    break;
                }
            }
        }
    }
}
