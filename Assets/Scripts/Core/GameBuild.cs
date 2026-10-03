using System.Linq;

namespace Catan.Core
{
    public sealed partial class Game
    {
        // ---- Building ------------------------------------------------------------------------------

        string DoBuildRoad(BuildRoad c)
        {
            bool free = Phase == Phase.RoadBuilding;
            if (Phase != Phase.Main && !free) return $"You can't do that during {Phase}.";
            string err = NotYourTurn(c);
            if (err != null) return err;

            Player p = _players[c.Player];
            if (!Board.HasEdge(c.Edge)) return "That edge is not on the board.";
            if (_roads.ContainsKey(c.Edge)) return "There is already a road there.";
            if (p.RoadsLeft <= 0) return "You have no roads left.";
            if (!IsRoadConnected(c.Player, c.Edge)) return "A road must connect to your network.";
            if (!free && !p.Hand.Contains(Costs.Road)) return "You can't afford a road.";

            if (!free) Pay(p, Costs.Road);
            PlaceRoad(p, c.Edge);
            Log($"{p.Name} built a road.");

            if (free)
            {
                _freeRoads--;
                if (_freeRoads <= 0 || p.RoadsLeft <= 0 || !LegalRoadEdges(c.Player).Any()) Phase = Phase.Main;
            }
            return null;
        }

        string DoBuildSettlement(BuildSettlement c)
        {
            string err = RequirePhase(Phase.Main, c);
            if (err != null) return err;

            Player p = _players[c.Player];
            if (!Board.HasVertex(c.Vertex)) return "That corner is not on the board.";
            if (_buildings.ContainsKey(c.Vertex)) return "That corner is already built on.";
            if (!IsSettlementSpotFree(c.Vertex)) return "Too close to another settlement.";
            if (!HasOwnRoadAt(c.Player, c.Vertex)) return "A settlement must connect to your road.";
            if (p.SettlementsLeft <= 0) return "You have no settlements left.";
            if (!p.Hand.Contains(Costs.Settlement)) return "You can't afford a settlement.";

            Pay(p, Costs.Settlement);
            PlaceSettlement(p, c.Vertex);
            Log($"{p.Name} built a settlement.");
            return null;
        }

        string DoBuildCity(BuildCity c)
        {
            string err = RequirePhase(Phase.Main, c);
            if (err != null) return err;

            Player p = _players[c.Player];
            if (!p.SettlementSet.Contains(c.Vertex)) return "You need one of your settlements there.";
            if (p.CitiesLeft <= 0) return "You have no cities left.";
            if (!p.Hand.Contains(Costs.City)) return "You can't afford a city.";

            Pay(p, Costs.City);
            PlaceCity(p, c.Vertex);
            Log($"{p.Name} upgraded to a city.");
            return null;
        }

        // ---- Development cards ---------------------------------------------------------------------

        string DoBuyDevCard(BuyDevCard c)
        {
            string err = RequirePhase(Phase.Main, c);
            if (err != null) return err;

            Player p = _players[c.Player];
            if (_deck.Count == 0) return "The development deck is empty.";
            if (!p.Hand.Contains(Costs.DevCard)) return "You can't afford a development card.";

            Pay(p, Costs.DevCard);
            DevCard card = _deck[_deck.Count - 1];
            _deck.RemoveAt(_deck.Count - 1);

            if (Rules.PlayDevCardOnPurchaseTurn) p.Dev[(int)card]++;
            else p.DevNew[(int)card]++;
            Log($"{p.Name} bought a development card.");
            return null;
        }

        /// <summary>Common checks for playing a card: phase, turn, ownership, and the one-per-turn limit.</summary>
        string BeginPlayCard(Command c, DevCard card, bool allowBeforeRoll)
        {
            bool phaseOk = Phase == Phase.Main || (allowBeforeRoll && Phase == Phase.Roll);
            if (!phaseOk) return $"You can't play that during {Phase}.";
            string err = NotYourTurn(c);
            if (err != null) return err;
            if (_players[c.Player].Dev[(int)card] <= 0) return "You don't have a playable copy of that card.";
            if (Rules.OneDevCardPerTurn && _devPlayedThisTurn) return "You already played a development card this turn.";
            return null;
        }

        void SpendCard(Player p, DevCard card)
        {
            p.Dev[(int)card]--;
            _devPlayedThisTurn = true;
        }

        string DoPlayKnight(PlayKnight c)
        {
            string err = BeginPlayCard(c, DevCard.Knight, allowBeforeRoll: true);
            if (err != null) return err;

            Player p = _players[c.Player];
            SpendCard(p, DevCard.Knight);
            p.KnightsPlayed++;
            Log($"{p.Name} played a Knight.");
            BeginRobber(Phase); // back to Roll or Main afterwards
            return null;
        }

        string DoPlayRoadBuilding(PlayRoadBuilding c)
        {
            string err = BeginPlayCard(c, DevCard.RoadBuilding, allowBeforeRoll: false);
            if (err != null) return err;

            Player p = _players[c.Player];
            SpendCard(p, DevCard.RoadBuilding);
            Log($"{p.Name} played Road Building.");

            _freeRoads = System.Math.Min(2, p.RoadsLeft);
            if (_freeRoads > 0 && LegalRoadEdges(c.Player).Any()) Phase = Phase.RoadBuilding;
            else _freeRoads = 0;
            return null;
        }

        string DoPlayYearOfPlenty(PlayYearOfPlenty c)
        {
            if (c.First == Resource.Desert || c.Second == Resource.Desert) return "Choose real resources.";
            string err = BeginPlayCard(c, DevCard.YearOfPlenty, allowBeforeRoll: false);
            if (err != null) return err;

            ResourceSet take = ResourceSet.Of(c.First).With(c.Second, 1);
            if (!Bank.Contains(take)) return "The bank doesn't have those resources.";

            Player p = _players[c.Player];
            SpendCard(p, DevCard.YearOfPlenty);
            Receive(p, take);
            Log($"{p.Name} played Year of Plenty and took {c.First} and {c.Second}.");
            return null;
        }

        string DoPlayMonopoly(PlayMonopoly c)
        {
            if (c.Resource == Resource.Desert) return "Choose a real resource.";
            string err = BeginPlayCard(c, DevCard.Monopoly, allowBeforeRoll: false);
            if (err != null) return err;

            Player p = _players[c.Player];
            SpendCard(p, DevCard.Monopoly);

            int total = 0;
            foreach (Player other in _players)
            {
                if (other.Id == p.Id) continue;
                int n = other.Hand[c.Resource];
                if (n == 0) continue;
                other.Hand = other.Hand.With(c.Resource, -n);
                p.Hand = p.Hand.With(c.Resource, n);
                total += n;
            }
            Log($"{p.Name} played Monopoly on {c.Resource} and took {total}.");
            return null;
        }

        // ---- Trading -------------------------------------------------------------------------------

        string DoBankTrade(BankTrade c)
        {
            string err = RequirePhase(Phase.Main, c);
            if (err != null) return err;
            if (c.Give == Resource.Desert || c.Get == Resource.Desert) return "Choose real resources.";
            if (c.Give == c.Get) return "Pick two different resources.";

            Player p = _players[c.Player];
            int ratio = GetBankRatio(c.Player, c.Give);
            if (p.Hand[c.Give] < ratio) return $"You need {ratio} {c.Give} for that trade.";
            if (Bank[c.Get] < 1) return $"The bank is out of {c.Get}.";

            Pay(p, ResourceSet.Of(c.Give, ratio));
            Receive(p, ResourceSet.Of(c.Get));
            Log($"{p.Name} traded {ratio} {c.Give} for 1 {c.Get} with the bank.");
            return null;
        }

        string DoProposeTrade(ProposeTrade c)
        {
            string err = RequirePhase(Phase.Main, c);
            if (err != null) return err;
            if (c.Give.HasNegative || c.Want.HasNegative || c.Give.IsEmpty || c.Want.IsEmpty)
                return "A trade must offer something and ask for something.";
            if (!_players[c.Player].Hand.Contains(c.Give)) return "You don't have those cards to offer.";

            PendingTrade = new TradeOffer(c.Player, c.Give, c.Want);
            Log($"{_players[c.Player].Name} offers {c.Give} for {c.Want}.");
            return null;
        }

        string DoAcceptTrade(AcceptTrade c)
        {
            if (PendingTrade == null) return "There is no open trade offer.";
            if (Phase != Phase.Main) return $"You can't do that during {Phase}.";
            if (c.Player == PendingTrade.From) return "You can't accept your own offer.";

            Player from = _players[PendingTrade.From];
            Player to = _players[c.Player];
            if (!to.Hand.Contains(PendingTrade.Want)) return "You don't have the cards they want.";
            if (!from.Hand.Contains(PendingTrade.Give)) return "The offer is no longer valid.";

            from.Hand = from.Hand - PendingTrade.Give + PendingTrade.Want;
            to.Hand = to.Hand - PendingTrade.Want + PendingTrade.Give;
            Log($"{to.Name} accepted {from.Name}'s trade.");
            PendingTrade = null;
            return null;
        }

        string DoCancelTrade(CancelTrade c)
        {
            if (PendingTrade == null) return "There is no open trade offer.";
            if (c.Player != PendingTrade.From) return "Only the offering player can cancel.";
            PendingTrade = null;
            Log("The trade offer was withdrawn.");
            return null;
        }
    }
}
