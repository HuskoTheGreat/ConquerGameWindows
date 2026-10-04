using System.Collections.Generic;
using Catan.Core;

namespace Catan.Client.Animation
{
    /// <summary>Somewhere a card can fly from or to.</summary>
    public enum PlaceKind { Bank, DevDeck, Player, Tile, Table }

    /// <summary>A place on screen, named by meaning rather than pixels. The window resolves it to a position.</summary>
    public readonly record struct Place(PlaceKind Kind, int Player = -1, Hex Hex = default)
    {
        public static Place Bank => new Place(PlaceKind.Bank);
        public static Place DevDeck => new Place(PlaceKind.DevDeck);
        public static Place Table => new Place(PlaceKind.Table);
        public static Place Seat(int id) => new Place(PlaceKind.Player, id);
        public static Place Tile(Hex h) => new Place(PlaceKind.Tile, -1, h);
    }

    /// <summary>
    /// Something the screen should animate. These describe what changed in the game, not how to draw it,
    /// and carry no timing: the <see cref="Animator"/> decides that on its own clock.
    /// </summary>
    public abstract record VisualEvent;

    /// <summary>The dice landed on <paramref name="Total"/>.</summary>
    public sealed record DiceRolled(int Player, int Total) : VisualEvent;

    /// <summary>
    /// <paramref name="Count"/> cards moved between two places. <paramref name="Resource"/> is null when this
    /// viewer may not know what they were, and the cards fly face down.
    /// </summary>
    public sealed record CardsMoved(Place From, Place To, Resource? Resource, int Count) : VisualEvent;

    /// <summary>A development card was pulled from the deck. <paramref name="Card"/> is null unless the viewer drew it.</summary>
    public sealed record DevCardDrawn(int Player, DevCard? Card) : VisualEvent;

    /// <summary>A development card was played face up. <paramref name="Card"/> is null if it can't be told apart.</summary>
    public sealed record DevCardPlayed(int Player, DevCard? Card) : VisualEvent;

    public sealed record SettlementPlaced(int Player, Vertex At) : VisualEvent;

    public sealed record CityPlaced(int Player, Vertex At) : VisualEvent;

    public sealed record RoadPlaced(int Player, Edge At) : VisualEvent;

    public sealed record RobberMoved(Hex From, Hex To) : VisualEvent;

    public sealed record TurnStarted(int Player, int Turn) : VisualEvent;

    public enum Award { LongestRoad, LargestArmy }

    public sealed record AwardTaken(Award Award, int Player) : VisualEvent;

    public sealed record GameWon(int Player) : VisualEvent;

    /// <summary>House rules changed. Each entry reads like "Points to win: 10 → 8".</summary>
    public sealed record RulesChanged(IReadOnlyList<string> Changes) : VisualEvent;
}
