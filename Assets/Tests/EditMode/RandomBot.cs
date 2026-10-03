using System;
using System.Collections.Generic;
using System.Linq;

namespace Catan.Core.Tests
{
    /// <summary>
    /// Picks a random command that the engine's own legal-move queries say is allowed. If the engine then
    /// rejects it, the queries and the rules have drifted apart, which is exactly what the fuzz test hunts for.
    /// </summary>
    public static class RandomBot
    {
        public static Command Choose(Game g, Random rnd)
        {
            int me = g.CurrentPlayer;
            Player p = g.Players[me];

            switch (g.Phase)
            {
                case Phase.SetupSettlement:
                    return new SetupSettlement(me, Pick(g.LegalSetupVertices().ToList(), rnd));

                case Phase.SetupRoad:
                    return new SetupRoad(me, Pick(g.LegalSetupRoadEdges().ToList(), rnd));

                case Phase.Roll:
                    return new RollDice(me);

                case Phase.Discard:
                {
                    var owing = g.PendingDiscards.First();
                    return new DiscardCards(owing.Key, RandomCards(g.Players[owing.Key].Hand, owing.Value, rnd));
                }

                case Phase.MoveRobber:
                    return new MoveRobber(me, Pick(g.LegalRobberHexes().ToList(), rnd));

                case Phase.Steal:
                    return new StealFrom(me, Pick(g.StealCandidates.ToList(), rnd));

                case Phase.RoadBuilding:
                    return new BuildRoad(me, Pick(g.LegalRoadEdges(me).ToList(), rnd));
            }

            // Main phase: top up now and then so the bot can actually build.
            if (rnd.Next(3) == 0)
            {
                ResourceSet gift = ResourceSet.Of(ResourceSet.Types[rnd.Next(5)], 1 + rnd.Next(3));
                if (g.Bank.Contains(gift)) g.GrantResources(me, gift);
            }

            var options = new List<Command>();
            if (p.Hand.Contains(Costs.City) && p.CitiesLeft > 0 && g.LegalCityVertices(me).Any())
                options.Add(new BuildCity(me, Pick(g.LegalCityVertices(me).ToList(), rnd)));
            if (p.Hand.Contains(Costs.Settlement) && p.SettlementsLeft > 0 && g.LegalSettlementVertices(me).Any())
                options.Add(new BuildSettlement(me, Pick(g.LegalSettlementVertices(me).ToList(), rnd)));
            if (p.Hand.Contains(Costs.Road) && p.RoadsLeft > 0 && g.LegalRoadEdges(me).Any())
                options.Add(new BuildRoad(me, Pick(g.LegalRoadEdges(me).ToList(), rnd)));
            if (p.Hand.Contains(Costs.DevCard) && g.DevDeckCount > 0)
                options.Add(new BuyDevCard(me));

            if (!g.DevCardPlayedThisTurn || !g.Rules.OneDevCardPerTurn)
            {
                if (p.DevCardsUsable(DevCard.Knight) > 0) options.Add(new PlayKnight(me));
                if (p.DevCardsUsable(DevCard.RoadBuilding) > 0) options.Add(new PlayRoadBuilding(me));
                if (p.DevCardsUsable(DevCard.Monopoly) > 0)
                    options.Add(new PlayMonopoly(me, ResourceSet.Types[rnd.Next(5)]));
                if (p.DevCardsUsable(DevCard.YearOfPlenty) > 0)
                {
                    Resource a = ResourceSet.Types[rnd.Next(5)], b = ResourceSet.Types[rnd.Next(5)];
                    if (g.Bank.Contains(ResourceSet.Of(a).With(b, 1))) options.Add(new PlayYearOfPlenty(me, a, b));
                }
            }

            foreach (Resource give in ResourceSet.Types)
            {
                if (p.Hand[give] < g.GetBankRatio(me, give)) continue;
                var gets = ResourceSet.Types.Where(r => r != give && g.Bank[r] > 0).ToList();
                if (gets.Count > 0) options.Add(new BankTrade(me, give, Pick(gets, rnd)));
            }

            // Ending the turn is always available and should happen regularly.
            options.Add(new EndTurn(me));
            options.Add(new EndTurn(me));
            return Pick(options, rnd);
        }

        static T Pick<T>(IList<T> items, Random rnd) => items[rnd.Next(items.Count)];

        static ResourceSet RandomCards(ResourceSet hand, int count, Random rnd)
        {
            ResourceSet chosen = ResourceSet.Empty;
            for (int i = 0; i < count; i++)
            {
                var available = ResourceSet.Types.Where(r => hand[r] - chosen[r] > 0).ToList();
                chosen = chosen.With(Pick(available, rnd), 1);
            }
            return chosen;
        }
    }
}
