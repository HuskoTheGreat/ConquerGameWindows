using System.Collections.Generic;

namespace Catan.Core
{
    public sealed class Player
    {
        public const int MaxSettlements = 5;
        public const int MaxCities = 4;
        public const int MaxRoads = 15;

        public int Id { get; }
        public string Name { get; internal set; }
        public ResourceSet Hand { get; internal set; }
        public int KnightsPlayed { get; internal set; }

        /// <summary>Length of this player's longest continuous road (refreshed after every action).</summary>
        public int LongestRoad { get; internal set; }

        internal readonly HashSet<Vertex> SettlementSet = new HashSet<Vertex>();
        internal readonly HashSet<Vertex> CitySet = new HashSet<Vertex>();
        internal readonly HashSet<Edge> RoadSet = new HashSet<Edge>();

        // Dev cards in hand: usable now vs. bought this turn (usable from next turn).
        internal readonly int[] Dev = new int[5];
        internal readonly int[] DevNew = new int[5];

        public IReadOnlyCollection<Vertex> Settlements => SettlementSet;
        public IReadOnlyCollection<Vertex> Cities => CitySet;
        public IReadOnlyCollection<Edge> Roads => RoadSet;

        public int SettlementsLeft => MaxSettlements - SettlementSet.Count;
        public int CitiesLeft => MaxCities - CitySet.Count;
        public int RoadsLeft => MaxRoads - RoadSet.Count;

        public int DevCardsUsable(DevCard c) => Dev[(int)c];
        public int DevCardsTotal(DevCard c) => Dev[(int)c] + DevNew[(int)c];

        // In a client-side mirror, other players' hands are unknown; only these counts are sent. -1 = real data.
        internal int MirrorHandCount = -1;
        internal int MirrorDevCount = -1;

        /// <summary>Cards in hand. Works for everyone, including opponents in a networked mirror.</summary>
        public int HandCount => MirrorHandCount >= 0 ? MirrorHandCount : Hand.Total;

        /// <summary>Development cards held (usable and new).</summary>
        public int DevCardCount
        {
            get
            {
                if (MirrorDevCount >= 0) return MirrorDevCount;
                int n = 0;
                for (int i = 0; i < Dev.Length; i++) n += Dev[i] + DevNew[i];
                return n;
            }
        }

        internal Player(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}
