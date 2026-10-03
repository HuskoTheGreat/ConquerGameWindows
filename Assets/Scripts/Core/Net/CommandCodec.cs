using System;

namespace Catan.Core.Net
{
    /// <summary>
    /// Wire format for <see cref="Command"/>s. The player id is deliberately NOT on the wire: the host assigns
    /// it from the connection the bytes arrived on, so a client can never act as someone else.
    /// Only whitelisted command types decode; anything else throws <see cref="WireException"/>.
    /// </summary>
    public static class CommandCodec
    {
        public const int MaxBytes = 256;

        enum Id : byte
        {
            SetupSettlement = 1, SetupRoad, RollDice, DiscardCards, MoveRobber, StealFrom,
            BuildRoad, BuildSettlement, BuildCity, BuyDevCard, PlayKnight, PlayRoadBuilding,
            PlayYearOfPlenty, PlayMonopoly, BankTrade, ProposeTrade, AcceptTrade, CancelTrade,
            EndTurn, SetHouseRules,
        }

        public static byte[] Encode(Command command)
        {
            var w = new WireWriter();
            switch (command)
            {
                case SetupSettlement c: w.Byte((int)Id.SetupSettlement); w.Write(c.Vertex); break;
                case SetupRoad c: w.Byte((int)Id.SetupRoad); w.Write(c.Edge); break;
                case RollDice _: w.Byte((int)Id.RollDice); break;
                case DiscardCards c: w.Byte((int)Id.DiscardCards); w.Write(c.Cards); break;
                case MoveRobber c: w.Byte((int)Id.MoveRobber); w.Write(c.To); break;
                case StealFrom c: w.Byte((int)Id.StealFrom); w.Byte(c.Victim); break;
                case BuildRoad c: w.Byte((int)Id.BuildRoad); w.Write(c.Edge); break;
                case BuildSettlement c: w.Byte((int)Id.BuildSettlement); w.Write(c.Vertex); break;
                case BuildCity c: w.Byte((int)Id.BuildCity); w.Write(c.Vertex); break;
                case BuyDevCard _: w.Byte((int)Id.BuyDevCard); break;
                case PlayKnight _: w.Byte((int)Id.PlayKnight); break;
                case PlayRoadBuilding _: w.Byte((int)Id.PlayRoadBuilding); break;
                case PlayYearOfPlenty c: w.Byte((int)Id.PlayYearOfPlenty); w.Write(c.First); w.Write(c.Second); break;
                case PlayMonopoly c: w.Byte((int)Id.PlayMonopoly); w.Write(c.Resource); break;
                case BankTrade c: w.Byte((int)Id.BankTrade); w.Write(c.Give); w.Write(c.Get); break;
                case ProposeTrade c: w.Byte((int)Id.ProposeTrade); w.Write(c.Give); w.Write(c.Want); break;
                case AcceptTrade _: w.Byte((int)Id.AcceptTrade); break;
                case CancelTrade _: w.Byte((int)Id.CancelTrade); break;
                case EndTurn _: w.Byte((int)Id.EndTurn); break;
                case SetHouseRules c: w.Byte((int)Id.SetHouseRules); w.Write(c.Rules); break;
                default: throw new ArgumentException("Unsupported command type.");
            }
            return w.ToArray();
        }

        /// <summary>Decodes a command and stamps it with <paramref name="seat"/>, the host-assigned player id.</summary>
        public static Command Decode(byte[] data, int seat)
        {
            var r = new WireReader(data, MaxBytes);
            Command command;
            switch ((Id)r.Byte())
            {
                case Id.SetupSettlement: command = new SetupSettlement(seat, r.ReadVertex()); break;
                case Id.SetupRoad: command = new SetupRoad(seat, r.ReadEdge()); break;
                case Id.RollDice: command = new RollDice(seat); break;
                case Id.DiscardCards: command = new DiscardCards(seat, r.ReadResourceSet()); break;
                case Id.MoveRobber: command = new MoveRobber(seat, r.ReadHex()); break;
                case Id.StealFrom: command = new StealFrom(seat, r.Byte(5)); break;
                case Id.BuildRoad: command = new BuildRoad(seat, r.ReadEdge()); break;
                case Id.BuildSettlement: command = new BuildSettlement(seat, r.ReadVertex()); break;
                case Id.BuildCity: command = new BuildCity(seat, r.ReadVertex()); break;
                case Id.BuyDevCard: command = new BuyDevCard(seat); break;
                case Id.PlayKnight: command = new PlayKnight(seat); break;
                case Id.PlayRoadBuilding: command = new PlayRoadBuilding(seat); break;
                case Id.PlayYearOfPlenty: command = new PlayYearOfPlenty(seat, r.ReadResource(), r.ReadResource()); break;
                case Id.PlayMonopoly: command = new PlayMonopoly(seat, r.ReadResource()); break;
                case Id.BankTrade: command = new BankTrade(seat, r.ReadResource(), r.ReadResource()); break;
                case Id.ProposeTrade: command = new ProposeTrade(seat, r.ReadResourceSet(), r.ReadResourceSet()); break;
                case Id.AcceptTrade: command = new AcceptTrade(seat); break;
                case Id.CancelTrade: command = new CancelTrade(seat); break;
                case Id.EndTurn: command = new EndTurn(seat); break;
                case Id.SetHouseRules: command = new SetHouseRules(seat, r.ReadHouseRules()); break;
                default: throw new WireException("Unknown command.");
            }
            r.End();
            return command;
        }
    }
}
