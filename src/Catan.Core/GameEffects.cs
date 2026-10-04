using System.Collections.Generic;
using System.Linq;

namespace Catan.Core
{
    /// <summary>One card in the development deck: a normal development card or a house-rule effect card.</summary>
    internal readonly struct DeckCard
    {
        public readonly bool IsEffect;
        public readonly DevCard Dev;
        public readonly EffectCard Effect;

        DeckCard(bool isEffect, DevCard dev, EffectCard effect)
        {
            IsEffect = isEffect;
            Dev = dev;
            Effect = effect;
        }

        public static DeckCard Of(DevCard card) => new DeckCard(false, card, default);
        public static DeckCard Of(EffectCard card) => new DeckCard(true, default, card);
    }

    public sealed partial class Game
    {
        /// <summary>Players still in the game.</summary>
        public IEnumerable<Player> ActivePlayers => _players.Where(p => !p.Eliminated);

        void AddEffectCards()
        {
            for (int i = 0; i < EffectCardInfo.Count; i++)
            {
                var e = (EffectCard)i;
                for (int n = 0; n < Rules.EffectCardCount(e); n++) _deck.Add(DeckCard.Of(e));
            }
        }

        /// <summary>Swaps the effect cards in the (untouched, pre-roll) deck for a new mix and reshuffles.</summary>
        void RebuildEffectCards(HouseRules rules)
        {
            _deck.RemoveAll(d => d.IsEffect);
            for (int i = 0; i < EffectCardInfo.Count; i++)
            {
                var e = (EffectCard)i;
                for (int n = 0; n < rules.EffectCardCount(e); n++) _deck.Add(DeckCard.Of(e));
            }
            _rng.Shuffle(_deck);
        }

        void GiveStartingResources()
        {
            int n = Rules.StartingResources;
            if (n <= 0) return;
            foreach (Player p in _players)
            {
                ResourceSet gift = ResourceSet.Empty;
                foreach (Resource r in ResourceSet.Types) gift = gift.With(r, System.Math.Min(n, Bank[r]));
                Receive(p, gift);
            }
            Log($"House rule: everyone starts with {n} of each resource.");
        }

        void ResolveEffect(Player p, EffectCard effect)
        {
            Log($"{p.Name} drew {EffectCardInfo.Name(effect)}! {EffectCardInfo.Description(effect)}");
            switch (effect)
            {
                case EffectCard.InstantLoss:
                    Eliminate(p);
                    break;

                case EffectCard.InstantWin:
                    Winner = p.Id;
                    Phase = Phase.GameOver;
                    Log($"{p.Name} wins!");
                    break;

                case EffectCard.Bankruptcy:
                    if (p.Hand.Total > 0) Log($"{p.Name} lost {p.Hand.Total} cards to the bank.");
                    Pay(p, p.Hand);
                    break;

                case EffectCard.Windfall:
                {
                    ResourceSet gain = ResourceSet.Empty;
                    for (int i = 0; i < 3; i++)
                    {
                        var available = ResourceSet.Types.Where(r => Bank[r] - gain[r] > 0).ToList();
                        if (available.Count == 0) break;
                        gain = gain.With(available[_rng.Next(available.Count)], 1);
                    }
                    Receive(p, gain);
                    if (!gain.IsEmpty) Log($"{p.Name} took {gain.Describe()} from the bank.");
                    break;
                }

                case EffectCard.Plague:
                    foreach (Player other in ActivePlayers.Where(o => o.Id != p.Id))
                    {
                        int lose = other.Hand.Total / 2;
                        if (lose == 0) continue;
                        Pay(other, RandomCards(other.Hand, lose));
                        Log($"{other.Name} lost {lose} cards to the plague.");
                    }
                    break;

                case EffectCard.Tribute:
                    foreach (Player other in ActivePlayers.Where(o => o.Id != p.Id && o.Hand.Total > 0))
                    {
                        ResourceSet card = RandomCards(other.Hand, 1);
                        other.Hand -= card;
                        p.Hand += card;
                        Log($"{other.Name} paid tribute to {p.Name}.");
                    }
                    break;

                case EffectCard.Earthquake:
                    if (p.RoadSet.Count > 0)
                    {
                        Edge lost = p.RoadSet.ElementAt(_rng.Next(p.RoadSet.Count));
                        p.RoadSet.Remove(lost);
                        _roads.Remove(lost);
                        Log($"An earthquake destroyed one of {p.Name}'s roads.");
                    }
                    break;

                case EffectCard.Bandits:
                    BeginRobber(Phase.Main);
                    break;
            }
        }

        /// <summary><paramref name="count"/> cards picked uniformly at random from <paramref name="hand"/>.</summary>
        ResourceSet RandomCards(ResourceSet hand, int count)
        {
            ResourceSet picked = ResourceSet.Empty;
            for (int i = 0; i < count; i++)
            {
                ResourceSet left = hand - picked;
                if (left.Total == 0) break;
                int pick = _rng.Next(left.Total);
                foreach (Resource r in ResourceSet.Types)
                {
                    if (pick < left[r])
                    {
                        picked = picked.With(r, 1);
                        break;
                    }
                    pick -= left[r];
                }
            }
            return picked;
        }

        /// <summary>
        /// Takes a player out: their cards go back to the bank, their pieces stay as ruins that block but don't
        /// produce, and play moves on. If only one player remains, they win.
        /// </summary>
        void Eliminate(Player p)
        {
            Pay(p, p.Hand);
            for (int i = 0; i < p.Dev.Length; i++)
            {
                p.Dev[i] = 0;
                p.DevNew[i] = 0;
            }
            p.Eliminated = true;
            _discards.Remove(p.Id);
            if (PendingTrade != null && PendingTrade.From == p.Id) PendingTrade = null;
            Log($"{p.Name} is out of the game.");

            var left = ActivePlayers.ToList();
            if (left.Count == 1)
            {
                Winner = left[0].Id;
                Phase = Phase.GameOver;
                Log($"{left[0].Name} wins!");
                return;
            }
            if (CurrentPlayer == p.Id) AdvanceTurn();
        }
    }
}
