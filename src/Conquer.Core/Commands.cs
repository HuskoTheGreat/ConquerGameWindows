namespace Conquer.Core
{
    public enum Phase
    {
        SetupVillage,
        SetupRoad,
        Roll,
        Discard,
        MoveRaider,
        Steal,
        Main,
        Engineers,
        GameOver,
    }

    public enum ActionCard
    {
        Soldier,
        VictoryPoint,
        Engineers,
        Harvest,
        Plunder,
    }

    public readonly struct Building
    {
        public readonly int Owner;
        public readonly bool IsCity;

        public Building(int owner, bool isCity)
        {
            Owner = owner;
            IsCity = isCity;
        }
    }

    public sealed class TradeOffer
    {
        public int From { get; }
        public ResourceSet Give { get; }
        public ResourceSet Want { get; }

        public TradeOffer(int from, ResourceSet give, ResourceSet want)
        {
            From = from;
            Give = give;
            Want = want;
        }
    }

    /// <summary>
    /// A request from a player. Clients only ever send commands; the host's <see cref="Game"/> validates and
    /// applies them, which is the shape Netcode RPCs will carry later.
    /// </summary>
    public abstract class Command
    {
        public int Player { get; }
        protected Command(int player) => Player = player;
    }

    public sealed class SetupVillage : Command
    {
        public Vertex Vertex { get; }
        public SetupVillage(int player, Vertex vertex) : base(player) => Vertex = vertex;
    }

    public sealed class SetupRoad : Command
    {
        public Edge Edge { get; }
        public SetupRoad(int player, Edge edge) : base(player) => Edge = edge;
    }

    public sealed class RollDice : Command
    {
        public RollDice(int player) : base(player) { }
    }

    public sealed class DiscardCards : Command
    {
        public ResourceSet Cards { get; }
        public DiscardCards(int player, ResourceSet cards) : base(player) => Cards = cards;
    }

    public sealed class MoveRaider : Command
    {
        public Hex To { get; }
        public MoveRaider(int player, Hex to) : base(player) => To = to;
    }

    public sealed class StealFrom : Command
    {
        public int Victim { get; }
        public StealFrom(int player, int victim) : base(player) => Victim = victim;
    }

    public sealed class BuildRoad : Command
    {
        public Edge Edge { get; }
        public BuildRoad(int player, Edge edge) : base(player) => Edge = edge;
    }

    public sealed class BuildVillage : Command
    {
        public Vertex Vertex { get; }
        public BuildVillage(int player, Vertex vertex) : base(player) => Vertex = vertex;
    }

    public sealed class BuildCity : Command
    {
        public Vertex Vertex { get; }
        public BuildCity(int player, Vertex vertex) : base(player) => Vertex = vertex;
    }

    public sealed class BuyActionCard : Command
    {
        public BuyActionCard(int player) : base(player) { }
    }

    public sealed class PlaySoldier : Command
    {
        public PlaySoldier(int player) : base(player) { }
    }

    public sealed class PlayEngineers : Command
    {
        public PlayEngineers(int player) : base(player) { }
    }

    public sealed class PlayHarvest : Command
    {
        public Resource First { get; }
        public Resource Second { get; }

        public PlayHarvest(int player, Resource first, Resource second) : base(player)
        {
            First = first;
            Second = second;
        }
    }

    public sealed class PlayPlunder : Command
    {
        public Resource Resource { get; }
        public PlayPlunder(int player, Resource resource) : base(player) => Resource = resource;
    }

    /// <summary>Trade with the bank; the ratio (4:1, 3:1 or 2:1) comes from the player's ports.</summary>
    public sealed class BankTrade : Command
    {
        public Resource Give { get; }
        public Resource Get { get; }

        public BankTrade(int player, Resource give, Resource get) : base(player)
        {
            Give = give;
            Get = get;
        }
    }

    public sealed class ProposeTrade : Command
    {
        public ResourceSet Give { get; }
        public ResourceSet Want { get; }

        public ProposeTrade(int player, ResourceSet give, ResourceSet want) : base(player)
        {
            Give = give;
            Want = want;
        }
    }

    public sealed class AcceptTrade : Command
    {
        public AcceptTrade(int player) : base(player) { }
    }

    public sealed class CancelTrade : Command
    {
        public CancelTrade(int player) : base(player) { }
    }

    /// <summary>Host-only: replace the active house rules. Takes effect immediately for all players.</summary>
    public sealed class SetHouseRules : Command
    {
        public HouseRules Rules { get; }
        public SetHouseRules(int player, HouseRules rules) : base(player) => Rules = rules;
    }

    public sealed class EndTurn : Command
    {
        public EndTurn(int player) : base(player) { }
    }
}
