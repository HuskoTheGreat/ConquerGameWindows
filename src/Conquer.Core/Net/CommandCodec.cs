using System;

namespace Conquer.Core.Net
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
            SetupVillage = 1, SetupRoad, RollDice, DiscardCards, MoveRaider, StealFrom,
            BuildRoad, BuildVillage, BuildCity, BuyActionCard, PlaySoldier, PlayEngineers,
            PlayHarvest, PlayPlunder, BankTrade, ProposeTrade, AcceptTrade, CancelTrade,
            EndTurn, SetHouseRules,
        }

        public static byte[] Encode(Command command)
        {
            var w = new WireWriter();
            switch (command)
            {
                case SetupVillage c: w.Byte((int)Id.SetupVillage); w.Write(c.Vertex); break;
                case SetupRoad c: w.Byte((int)Id.SetupRoad); w.Write(c.Edge); break;
                case RollDice _: w.Byte((int)Id.RollDice); break;
                case DiscardCards c: w.Byte((int)Id.DiscardCards); w.Write(c.Cards); break;
                case MoveRaider c: w.Byte((int)Id.MoveRaider); w.Write(c.To); break;
                case StealFrom c: w.Byte((int)Id.StealFrom); w.Byte(c.Victim); break;
                case BuildRoad c: w.Byte((int)Id.BuildRoad); w.Write(c.Edge); break;
                case BuildVillage c: w.Byte((int)Id.BuildVillage); w.Write(c.Vertex); break;
                case BuildCity c: w.Byte((int)Id.BuildCity); w.Write(c.Vertex); break;
                case BuyActionCard _: w.Byte((int)Id.BuyActionCard); break;
                case PlaySoldier _: w.Byte((int)Id.PlaySoldier); break;
                case PlayEngineers _: w.Byte((int)Id.PlayEngineers); break;
                case PlayHarvest c: w.Byte((int)Id.PlayHarvest); w.Write(c.First); w.Write(c.Second); break;
                case PlayPlunder c: w.Byte((int)Id.PlayPlunder); w.Write(c.Resource); break;
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
                case Id.SetupVillage: command = new SetupVillage(seat, r.ReadVertex()); break;
                case Id.SetupRoad: command = new SetupRoad(seat, r.ReadEdge()); break;
                case Id.RollDice: command = new RollDice(seat); break;
                case Id.DiscardCards: command = new DiscardCards(seat, r.ReadResourceSet()); break;
                case Id.MoveRaider: command = new MoveRaider(seat, r.ReadHex()); break;
                case Id.StealFrom: command = new StealFrom(seat, r.Byte(5)); break;
                case Id.BuildRoad: command = new BuildRoad(seat, r.ReadEdge()); break;
                case Id.BuildVillage: command = new BuildVillage(seat, r.ReadVertex()); break;
                case Id.BuildCity: command = new BuildCity(seat, r.ReadVertex()); break;
                case Id.BuyActionCard: command = new BuyActionCard(seat); break;
                case Id.PlaySoldier: command = new PlaySoldier(seat); break;
                case Id.PlayEngineers: command = new PlayEngineers(seat); break;
                case Id.PlayHarvest: command = new PlayHarvest(seat, r.ReadResource(), r.ReadResource()); break;
                case Id.PlayPlunder: command = new PlayPlunder(seat, r.ReadResource()); break;
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
