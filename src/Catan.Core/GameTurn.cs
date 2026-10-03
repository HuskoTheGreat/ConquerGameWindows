using System.Collections.Generic;
using System.Linq;

namespace Catan.Core
{
    public sealed partial class Game
    {
        // ---- Setup (snake draft) -------------------------------------------------------------------

        string DoSetupSettlement(SetupSettlement c)
        {
            string err = RequirePhase(Phase.SetupSettlement, c);
            if (err != null) return err;
            if (!Board.HasVertex(c.Vertex)) return "That corner is not on the board.";
            if (_buildings.ContainsKey(c.Vertex)) return "That corner is already built on.";
            if (!IsSettlementSpotFree(c.Vertex)) return "Too close to another settlement.";

            Player p = _players[c.Player];
            PlaceSettlement(p, c.Vertex);
            _lastSetupVertex = c.Vertex;
            Log($"{p.Name} placed a starting settlement.");

            // The second settlement pays out one card per adjacent producing tile.
            if (_setupIndex >= _players.Count)
            {
                ResourceSet gain = ResourceSet.Empty;
                foreach (Tile t in Board.TilesAround(c.Vertex))
                {
                    if (!t.IsDesert) gain = gain.With(t.Resource, 1);
                }
                Receive(p, gain);
                if (!gain.IsEmpty) Log($"{p.Name} collected {gain.Describe()} from their second settlement.");
            }

            Phase = Phase.SetupRoad;
            return null;
        }

        string DoSetupRoad(SetupRoad c)
        {
            string err = RequirePhase(Phase.SetupRoad, c);
            if (err != null) return err;
            if (!Board.HasEdge(c.Edge)) return "That edge is not on the board.";
            if (_roads.ContainsKey(c.Edge)) return "There is already a road there.";
            if (!c.Edge.Endpoints().Contains(_lastSetupVertex)) return "The road must touch the settlement you just placed.";

            Player p = _players[c.Player];
            PlaceRoad(p, c.Edge);
            Log($"{p.Name} placed a starting road.");

            _setupIndex++;
            if (_setupIndex >= _setupOrder.Count)
            {
                Phase = Phase.Roll;
                CurrentPlayer = 0;
                Turn = 1;
                Log("Setup complete. Player 1 rolls first.");
            }
            else
            {
                Phase = Phase.SetupSettlement;
                CurrentPlayer = _setupOrder[_setupIndex];
            }
            return null;
        }

        // ---- Rolling and production ----------------------------------------------------------------

        string DoRoll(RollDice c)
        {
            string err = RequirePhase(Phase.Roll, c);
            if (err != null) return err;

            int roll = _dice.Roll();
            int rerolls = 0;
            while (roll == 7 && Rules.NoSevenRounds > 0 && Turn <= Rules.NoSevenRounds * _players.Count && rerolls++ < 50)
            {
                Log("A 7 was rolled, but sevens are off for now. Rolling again.");
                roll = _dice.Roll();
            }

            LastRoll = roll;
            Log($"{_players[c.Player].Name} rolled {roll}.");

            if (roll != 7)
            {
                Distribute(roll);
                Phase = Phase.Main;
                return null;
            }

            _discards.Clear();
            foreach (Player p in _players)
            {
                if (p.Hand.Total > Rules.DiscardThreshold) _discards[p.Id] = p.Hand.Total / 2;
            }

            if (_discards.Count > 0)
            {
                Phase = Phase.Discard;
                Log("Players with too many cards must discard.");
            }
            else
            {
                BeginRobber(Phase.Main);
            }
            return null;
        }

        void Distribute(int roll)
        {
            var owed = new Dictionary<int, ResourceSet>();
            foreach (Tile tile in Board.Tiles)
            {
                if (tile.Number != roll || tile.Hex == RobberHex) continue;
                for (int i = 0; i < 6; i++)
                {
                    if (!_buildings.TryGetValue(Vertex.OfCorner(tile.Hex, i), out Building b)) continue;
                    owed.TryGetValue(b.Owner, out ResourceSet current);
                    owed[b.Owner] = current.With(tile.Resource, b.IsCity ? 2 : 1);
                }
            }

            // Bank shortage rule: if a resource can't cover everyone, only a lone claimant gets the remainder.
            foreach (Resource r in ResourceSet.Types)
            {
                int demand = owed.Values.Sum(s => s[r]);
                if (demand == 0) continue;

                var claimants = owed.Where(kv => kv.Value[r] > 0).Select(kv => kv.Key).ToList();
                if (Bank[r] >= demand)
                {
                    foreach (int id in claimants) GiveResource(id, r, owed[id][r]);
                }
                else if (claimants.Count == 1)
                {
                    GiveResource(claimants[0], r, Bank[r]);
                }
                else
                {
                    Log($"The bank is short on {r}; nobody collects it.");
                }
            }
        }

        void GiveResource(int playerId, Resource r, int amount)
        {
            if (amount <= 0) return;
            Receive(_players[playerId], ResourceSet.Of(r, amount));
            Log($"{_players[playerId].Name} collected {amount} {r}.");
        }

        // ---- Robber --------------------------------------------------------------------------------

        string DoDiscard(DiscardCards c)
        {
            if (Phase != Phase.Discard) return $"You can't do that during {Phase}.";
            if (!_discards.TryGetValue(c.Player, out int owe)) return "You don't need to discard.";

            ResourceSet cards = c.Cards;
            if (cards.HasNegative) return "Invalid discard.";
            if (cards.Total != owe) return $"You must discard exactly {owe} cards.";

            Player p = _players[c.Player];
            if (!p.Hand.Contains(cards)) return "You don't have those cards.";

            Pay(p, cards);
            _discards.Remove(c.Player);
            Log($"{p.Name} discarded {owe} cards.");

            if (_discards.Count == 0) BeginRobber(Phase.Main);
            return null;
        }

        void BeginRobber(Phase returnTo)
        {
            _robberReturn = returnTo;
            Phase = Phase.MoveRobber;
        }

        /// <summary>Hexes the robber may move to right now (honors the friendly-robber house rule).</summary>
        public IEnumerable<Hex> LegalRobberHexes()
        {
            var all = Board.Tiles.Select(t => t.Hex).Where(h => h != RobberHex).ToList();
            if (!Rules.FriendlyRobber) return all;

            var friendly = all.Where(h => !IsProtectedHex(h, CurrentPlayer)).ToList();
            return friendly.Count > 0 ? friendly : all;
        }

        /// <summary>True if a building of a player with 2 or fewer visible VP (other than the mover) touches the hex.</summary>
        bool IsProtectedHex(Hex hex, int mover)
        {
            for (int i = 0; i < 6; i++)
            {
                if (_buildings.TryGetValue(Vertex.OfCorner(hex, i), out Building b) &&
                    b.Owner != mover && PublicVictoryPoints(b.Owner) <= 2) return true;
            }
            return false;
        }

        string DoMoveRobber(MoveRobber c)
        {
            string err = RequirePhase(Phase.MoveRobber, c);
            if (err != null) return err;
            if (!Board.IsLand(c.To)) return "The robber must stay on the land.";
            if (c.To == RobberHex) return "You must move the robber to a different hex.";
            if (!LegalRobberHexes().Contains(c.To)) return "Friendly robber: you can't target a player with 2 or fewer points.";

            RobberHex = c.To;
            Log($"{_players[c.Player].Name} moved the robber.");

            _stealCandidates = GetStealCandidates(c.To, c.Player);
            if (_stealCandidates.Count == 0)
            {
                Phase = _robberReturn;
            }
            else if (_stealCandidates.Count == 1)
            {
                StealRandom(_players[c.Player], _players[_stealCandidates[0]]);
                _stealCandidates = new List<int>();
                Phase = _robberReturn;
            }
            else
            {
                Phase = Phase.Steal;
            }
            return null;
        }

        List<int> GetStealCandidates(Hex hex, int mover)
        {
            var found = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                if (_buildings.TryGetValue(Vertex.OfCorner(hex, i), out Building b) &&
                    b.Owner != mover && _players[b.Owner].Hand.Total > 0 && !found.Contains(b.Owner))
                    found.Add(b.Owner);
            }
            return found;
        }

        string DoSteal(StealFrom c)
        {
            string err = RequirePhase(Phase.Steal, c);
            if (err != null) return err;
            if (!_stealCandidates.Contains(c.Victim)) return "You can't steal from that player.";

            StealRandom(_players[c.Player], _players[c.Victim]);
            _stealCandidates = new List<int>();
            Phase = _robberReturn;
            return null;
        }

        void StealRandom(Player thief, Player victim)
        {
            int pick = _rng.Next(victim.Hand.Total);
            foreach (Resource r in ResourceSet.Types)
            {
                int n = victim.Hand[r];
                if (pick < n)
                {
                    victim.Hand = victim.Hand.With(r, -1);
                    thief.Hand = thief.Hand.With(r, 1);
                    Log($"{thief.Name} stole a card from {victim.Name}.");
                    return;
                }
                pick -= n;
            }
        }

        // ---- Turn end and rule changes -------------------------------------------------------------

        string DoEndTurn(EndTurn c)
        {
            string err = RequirePhase(Phase.Main, c);
            if (err != null) return err;

            Player p = _players[c.Player];
            for (int i = 0; i < p.Dev.Length; i++)
            {
                p.Dev[i] += p.DevNew[i];
                p.DevNew[i] = 0;
            }

            _devPlayedThisTurn = false;
            PendingTrade = null;
            CurrentPlayer = (CurrentPlayer + 1) % _players.Count;
            Turn++;
            Phase = Phase.Roll;
            Log($"{_players[CurrentPlayer].Name}'s turn.");
            return null;
        }

        string DoSetHouseRules(SetHouseRules c)
        {
            if (c.Player != HostPlayer) return "Only the host can change house rules.";
            if (c.Rules == null) return "No rules supplied.";

            bool safePhase = Phase == Phase.SetupSettlement || Phase == Phase.SetupRoad ||
                             Phase == Phase.Roll || Phase == Phase.Main;
            if (!safePhase) return "Finish the current robber/discard step before changing rules.";

            string err = c.Rules.Validate();
            if (err != null) return err;

            Rules = c.Rules.Clone();
            Log("The host changed the house rules.");
            return null;
        }
    }
}
