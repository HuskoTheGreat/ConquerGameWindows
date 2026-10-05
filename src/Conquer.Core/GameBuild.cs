using System.Linq;

namespace Conquer.Core
{
    public sealed partial class Game
    {
        // ---- Building ------------------------------------------------------------------------------

        string DoBuildRoad(BuildRoad c)
        {
            bool free = Phase == Phase.Engineers;
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

        string DoBuildVillage(BuildVillage c)
        {
            string err = RequirePhase(Phase.Main, c);
            if (err != null) return err;

            Player p = _players[c.Player];
            if (!Board.HasVertex(c.Vertex)) return "That corner is not on the board.";
            if (_buildings.ContainsKey(c.Vertex)) return "That corner is already built on.";
            if (!IsVillageSpotFree(c.Vertex)) return "Too close to another village.";
            if (!HasOwnRoadAt(c.Player, c.Vertex)) return "A village must connect to your road.";
            if (p.VillagesLeft <= 0) return "You have no villages left.";
            if (!p.Hand.Contains(Costs.Village)) return "You can't afford a village.";

            Pay(p, Costs.Village);
            PlaceVillage(p, c.Vertex);
            Log($"{p.Name} built a village.");
            return null;
        }

        string DoBuildCity(BuildCity c)
        {
            string err = RequirePhase(Phase.Main, c);
            if (err != null) return err;

            Player p = _players[c.Player];
            if (!p.VillageSet.Contains(c.Vertex)) return "You need one of your villages there.";
            if (p.CitiesLeft <= 0) return "You have no cities left.";
            if (!p.Hand.Contains(Costs.City)) return "You can't afford a city.";

            Pay(p, Costs.City);
            PlaceCity(p, c.Vertex);
            Log($"{p.Name} upgraded to a city.");
            return null;
        }

        // ---- Action cards ---------------------------------------------------------------------

        string DoBuyActionCard(BuyActionCard c)
        {
            string err = RequirePhase(Phase.Main, c);
            if (err != null) return err;

            Player p = _players[c.Player];
            if (_deck.Count == 0) return "The action deck is empty.";
            if (!p.Hand.Contains(Costs.ActionCard)) return "You can't afford an action card.";

            Pay(p, Costs.ActionCard);
            DeckCard drawn = _deck[_deck.Count - 1];
            _deck.RemoveAt(_deck.Count - 1);

            if (drawn.IsEffect)
            {
                ResolveEffect(p, drawn.Effect);
                return null;
            }

            ActionCard card = drawn.Dev;
            if (Rules.PlayActionCardOnPurchaseTurn) p.Dev[(int)card]++;
            else p.DevNew[(int)card]++;
            Log($"{p.Name} bought an action card.");
            return null;
        }

        /// <summary>Common checks for playing a card: phase, turn, ownership, and the one-per-turn limit.</summary>
        string BeginPlayCard(Command c, ActionCard card, bool allowBeforeRoll)
        {
            bool phaseOk = Phase == Phase.Main || (allowBeforeRoll && Phase == Phase.Roll);
            if (!phaseOk) return $"You can't play that during {Phase}.";
            string err = NotYourTurn(c);
            if (err != null) return err;
            if (_players[c.Player].Dev[(int)card] <= 0) return "You don't have a playable copy of that card.";
            if (Rules.OneActionCardPerTurn && _devPlayedThisTurn) return "You already played an action card this turn.";
            return null;
        }

        void SpendCard(Player p, ActionCard card)
        {
            p.Dev[(int)card]--;
            _devPlayedThisTurn = true;
        }

        string DoPlaySoldier(PlaySoldier c)
        {
            string err = BeginPlayCard(c, ActionCard.Soldier, allowBeforeRoll: true);
            if (err != null) return err;

            Player p = _players[c.Player];
            SpendCard(p, ActionCard.Soldier);
            p.SoldiersPlayed++;
            Log($"{p.Name} played a Soldier.");
            BeginRaider(Phase); // back to Roll or Main afterwards
            return null;
        }

        string DoPlayEngineers(PlayEngineers c)
        {
            string err = BeginPlayCard(c, ActionCard.Engineers, allowBeforeRoll: false);
            if (err != null) return err;

            Player p = _players[c.Player];
            SpendCard(p, ActionCard.Engineers);
            Log($"{p.Name} played Engineers.");

            _freeRoads = System.Math.Min(2, p.RoadsLeft);
            if (_freeRoads > 0 && LegalRoadEdges(c.Player).Any()) Phase = Phase.Engineers;
            else _freeRoads = 0;
            return null;
        }

        string DoPlayHarvest(PlayHarvest c)
        {
            if (c.First == Resource.Wasteland || c.Second == Resource.Wasteland) return "Choose real resources.";
            string err = BeginPlayCard(c, ActionCard.Harvest, allowBeforeRoll: false);
            if (err != null) return err;

            ResourceSet take = ResourceSet.Of(c.First).With(c.Second, 1);
            if (!Bank.Contains(take)) return "The bank doesn't have those resources.";

            Player p = _players[c.Player];
            SpendCard(p, ActionCard.Harvest);
            Receive(p, take);
            Log($"{p.Name} played Harvest and took {c.First} and {c.Second}.");
            return null;
        }

        string DoPlayPlunder(PlayPlunder c)
        {
            if (c.Resource == Resource.Wasteland) return "Choose a real resource.";
            string err = BeginPlayCard(c, ActionCard.Plunder, allowBeforeRoll: false);
            if (err != null) return err;

            Player p = _players[c.Player];
            SpendCard(p, ActionCard.Plunder);

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
            Log($"{p.Name} played Plunder on {c.Resource} and took {total}.");
            return null;
        }

        // ---- Trading -------------------------------------------------------------------------------

        /// <summary>Normally trades happen in your own Main phase; with Trade Anytime, anyone may trade once dice are in play.</summary>
        string RequireTradeWindow(Command c)
        {
            if (Rules.TradeAnytime)
                return Phase == Phase.Main || Phase == Phase.Roll ? null : $"You can't trade during {Phase}.";
            return RequirePhase(Phase.Main, c);
        }

        string DoBankTrade(BankTrade c)
        {
            string err = RequireTradeWindow(c);
            if (err != null) return err;
            if (c.Give == Resource.Wasteland || c.Get == Resource.Wasteland) return "Choose real resources.";
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
            string err = RequireTradeWindow(c);
            if (err != null) return err;
            if (c.Give.HasNegative || c.Want.HasNegative || c.Give.IsEmpty || c.Want.IsEmpty)
                return "A trade must offer something and ask for something.";
            if (ResourceSet.Types.Any(r => c.Give[r] > 0 && c.Want[r] > 0))
                return "You can't offer and ask for the same resource.";
            if (!_players[c.Player].Hand.Contains(c.Give)) return "You don't have those cards to offer.";

            PendingTrade = new TradeOffer(c.Player, c.Give, c.Want);
            Log($"{_players[c.Player].Name} offers {c.Give.Describe()} for {c.Want.Describe()}.");
            return null;
        }

        string DoAcceptTrade(AcceptTrade c)
        {
            if (PendingTrade == null) return "There is no open trade offer.";
            bool open = Phase == Phase.Main || (Rules.TradeAnytime && Phase == Phase.Roll);
            if (!open) return $"You can't do that during {Phase}.";
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
