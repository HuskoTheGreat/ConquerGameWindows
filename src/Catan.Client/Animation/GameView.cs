using System.Collections.Generic;
using System.Linq;
using Catan.Core;

namespace Catan.Client.Animation
{
    /// <summary>What one player could see of a seat at one moment.</summary>
    public sealed class SeatView
    {
        public int Id { get; init; }
        public int HandCount { get; init; }

        /// <summary>The exact hand, or null when this viewer only knows the count (an opponent in an online game).</summary>
        public ResourceSet? Hand { get; init; }

        public int DevCount { get; init; }

        /// <summary>Development cards by type, or null when hidden from this viewer.</summary>
        public int[] Dev { get; init; }

        public int Knights { get; init; }
    }

    /// <summary>
    /// A frozen copy of the parts of a <see cref="Game"/> that animations care about, as one viewer sees it.
    /// It reads only public queries, so it works the same on the local engine and on the read-only mirror an
    /// online client rebuilds from each server snapshot. Animations are found by diffing two of these
    /// (<see cref="VisualDiff"/>), never by hooking into the rules engine.
    /// </summary>
    public sealed class GameView
    {
        public Game Game { get; private init; }
        public int Viewer { get; private init; }
        public Phase Phase { get; private init; }
        public int Turn { get; private init; }
        public int CurrentPlayer { get; private init; }
        public int LastRoll { get; private init; }
        public Hex Robber { get; private init; }
        public int Winner { get; private init; }
        public int LongestRoad { get; private init; }
        public int LargestArmy { get; private init; }
        public ResourceSet Bank { get; private init; }
        public int DevDeck { get; private init; }
        public IReadOnlyList<SeatView> Seats { get; private init; }
        public IReadOnlyDictionary<Vertex, Building> Buildings { get; private init; }
        public IReadOnlyDictionary<Edge, int> Roads { get; private init; }

        /// <param name="viewer">The seat whose eyes we're looking through: the local player online, or whoever
        /// holds the device in a hot-seat game.</param>
        public static GameView Capture(Game g, int viewer)
        {
            var seats = new List<SeatView>();
            foreach (Player p in g.Players)
            {
                // A mirror only carries the viewer's own hand; the local engine knows everything.
                bool known = !g.IsMirror || p.Id == viewer;
                seats.Add(new SeatView
                {
                    Id = p.Id,
                    HandCount = p.HandCount,
                    Hand = known ? p.Hand : null,
                    DevCount = p.DevCardCount,
                    Dev = known ? ((DevCard[])System.Enum.GetValues(typeof(DevCard))).Select(p.DevCardsTotal).ToArray() : null,
                    Knights = p.KnightsPlayed,
                });
            }

            return new GameView
            {
                Game = g,
                Viewer = viewer,
                Phase = g.Phase,
                Turn = g.Turn,
                CurrentPlayer = g.CurrentPlayer,
                LastRoll = g.LastRoll,
                Robber = g.RobberHex,
                Winner = g.Winner,
                LongestRoad = g.LongestRoadHolder,
                LargestArmy = g.LargestArmyHolder,
                Bank = g.Bank,
                DevDeck = g.DevDeckCount,
                Seats = seats,
                Buildings = new Dictionary<Vertex, Building>(g.Buildings),
                Roads = new Dictionary<Edge, int>(g.RoadOwners),
            };
        }
    }
}
