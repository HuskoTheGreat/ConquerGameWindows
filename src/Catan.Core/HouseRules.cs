using System;
using System.Collections.Generic;

namespace Catan.Core
{
    /// <summary>
    /// Tunable rule variants. Defaults are the standard rules. Changed mid-game via <see cref="SetHouseRules"/>
    /// (host only), so every client sees the same rules because the host is authoritative.
    /// </summary>
    public sealed class HouseRules
    {
        /// <summary>Victory points needed to win on your own turn.</summary>
        public int VictoryPoints { get; set; } = 10;

        /// <summary>A 7 makes players holding MORE than this many cards discard half.</summary>
        public int DiscardThreshold { get; set; } = 7;

        /// <summary>Bank trade ratios: no port, generic port, matching resource port.</summary>
        public int BankRatio { get; set; } = 4;
        public int GenericPortRatio { get; set; } = 3;
        public int ResourcePortRatio { get; set; } = 2;

        /// <summary>Minimum continuous road length for Longest Road.</summary>
        public int LongestRoadMinimum { get; set; } = 5;

        /// <summary>Minimum knights played for Largest Army.</summary>
        public int LargestArmyMinimum { get; set; } = 3;

        /// <summary>Friendly robber: can't be moved onto, or steal from, a player with 2 or fewer visible VP.</summary>
        public bool FriendlyRobber { get; set; }

        /// <summary>Rolls of 7 are re-rolled during the first N full rounds (0 = off).</summary>
        public int NoSevenRounds { get; set; }

        /// <summary>Standard rules allow one non-VP development card per turn.</summary>
        public bool OneDevCardPerTurn { get; set; } = true;

        /// <summary>If true, a bought development card can be played the same turn.</summary>
        public bool PlayDevCardOnPurchaseTurn { get; set; }

        /// <summary>Anyone may trade, with other players or the bank, during anyone's turn (not just their own).</summary>
        public bool TradeAnytime { get; set; }

        /// <summary>Every player gets this many of each resource when setup ends (0 = standard).</summary>
        public int StartingResources { get; set; }

        /// <summary>
        /// How many of each <see cref="EffectCard"/> to shuffle into the development deck, indexed by the enum.
        /// They take effect the moment they're bought. Only changeable before the first roll.
        /// </summary>
        public int[] EffectCards { get; set; } = new int[EffectCardInfo.Count];

        public int EffectCardCount(EffectCard e) =>
            EffectCards != null && (int)e < EffectCards.Length ? EffectCards[(int)e] : 0;

        public HouseRules Clone()
        {
            var copy = (HouseRules)MemberwiseClone();
            copy.EffectCards = (int[])(EffectCards ?? new int[EffectCardInfo.Count]).Clone();
            return copy;
        }

        /// <summary>True if the two rule sets build the same deck and starting hands (the parts fixed at the first roll).</summary>
        public bool SameSetup(HouseRules other)
        {
            if (StartingResources != other.StartingResources) return false;
            for (int i = 0; i < EffectCardInfo.Count; i++)
                if (EffectCardCount((EffectCard)i) != other.EffectCardCount((EffectCard)i)) return false;
            return true;
        }

        /// <summary>Plain-English list of what changed, so everyone sees exactly what the host did.</summary>
        public static List<string> DescribeChanges(HouseRules before, HouseRules after)
        {
            var changes = new List<string>();
            void Num(string name, int a, int b) { if (a != b) changes.Add($"{name} {a} to {b}"); }
            void Flag(string name, bool a, bool b) { if (a != b) changes.Add($"{name} {(b ? "on" : "off")}"); }

            Num("points to win", before.VictoryPoints, after.VictoryPoints);
            Num("discard limit", before.DiscardThreshold, after.DiscardThreshold);
            Num("bank ratio", before.BankRatio, after.BankRatio);
            Num("generic port ratio", before.GenericPortRatio, after.GenericPortRatio);
            Num("resource port ratio", before.ResourcePortRatio, after.ResourcePortRatio);
            Num("longest road minimum", before.LongestRoadMinimum, after.LongestRoadMinimum);
            Num("largest army minimum", before.LargestArmyMinimum, after.LargestArmyMinimum);
            Num("no-7 rounds", before.NoSevenRounds, after.NoSevenRounds);
            Num("starting cards of each resource", before.StartingResources, after.StartingResources);
            Flag("friendly robber", before.FriendlyRobber, after.FriendlyRobber);
            Flag("one dev card per turn", before.OneDevCardPerTurn, after.OneDevCardPerTurn);
            Flag("dev cards playable when bought", before.PlayDevCardOnPurchaseTurn, after.PlayDevCardOnPurchaseTurn);
            Flag("trading anytime", before.TradeAnytime, after.TradeAnytime);
            for (int i = 0; i < EffectCardInfo.Count; i++)
            {
                var e = (EffectCard)i;
                Num(EffectCardInfo.Name(e) + " cards", before.EffectCardCount(e), after.EffectCardCount(e));
            }
            return changes;
        }

        /// <summary>Returns an error message, or null if the rules are playable.</summary>
        public string Validate()
        {
            if (VictoryPoints < 3 || VictoryPoints > 50) return "Victory points must be 3-50.";
            if (DiscardThreshold < 1 || DiscardThreshold > 50) return "Discard threshold must be 1-50.";
            if (BankRatio < 2 || BankRatio > 6) return "Bank ratio must be 2-6.";
            if (GenericPortRatio < 2 || GenericPortRatio > BankRatio) return "Generic port ratio must be 2 up to the bank ratio.";
            if (ResourcePortRatio < 1 || ResourcePortRatio > GenericPortRatio) return "Resource port ratio must be 1 up to the generic port ratio.";
            if (LongestRoadMinimum < 2 || LongestRoadMinimum > 15) return "Longest road minimum must be 2-15.";
            if (LargestArmyMinimum < 1 || LargestArmyMinimum > 14) return "Largest army minimum must be 1-14.";
            if (NoSevenRounds < 0 || NoSevenRounds > 20) return "No-seven rounds must be 0-20.";
            if (StartingResources < 0 || StartingResources > 5) return "Starting cards must be 0-5 of each resource.";
            if (EffectCards == null || EffectCards.Length != EffectCardInfo.Count) return "Bad effect card list.";
            for (int i = 0; i < EffectCards.Length; i++)
            {
                if (EffectCards[i] < 0 || EffectCards[i] > EffectCardInfo.MaxEach)
                    return $"{EffectCardInfo.Name((EffectCard)i)} cards must be 0-{EffectCardInfo.MaxEach}.";
            }
            return null;
        }
    }

    /// <summary>
    /// Optional house-rule cards shuffled into the development deck. Unlike development cards they aren't kept:
    /// whoever buys one gets its effect immediately, and everyone sees which one it was.
    /// </summary>
    public enum EffectCard
    {
        InstantLoss,
        InstantWin,
        Bankruptcy,
        Windfall,
        Plague,
        Tribute,
        Earthquake,
        Bandits,
    }

    public static class EffectCardInfo
    {
        public const int MaxEach = 5;
        public static readonly int Count = Enum.GetValues(typeof(EffectCard)).Length;

        public static string Name(EffectCard e)
        {
            switch (e)
            {
                case EffectCard.InstantLoss: return "Curse";
                case EffectCard.InstantWin: return "Golden Crown";
                case EffectCard.Bankruptcy: return "Bankruptcy";
                case EffectCard.Windfall: return "Windfall";
                case EffectCard.Plague: return "Plague";
                case EffectCard.Tribute: return "Tribute";
                case EffectCard.Earthquake: return "Earthquake";
                case EffectCard.Bandits: return "Bandits";
                default: return e.ToString();
            }
        }

        public static string Description(EffectCard e)
        {
            switch (e)
            {
                case EffectCard.InstantLoss: return "Instant loss: whoever draws it is out of the game.";
                case EffectCard.InstantWin: return "Instant win: whoever draws it wins on the spot.";
                case EffectCard.Bankruptcy: return "Lose every resource card in your hand.";
                case EffectCard.Windfall: return "Take 3 random resources from the bank.";
                case EffectCard.Plague: return "Everyone else loses half their cards.";
                case EffectCard.Tribute: return "Everyone else gives you 1 random card.";
                case EffectCard.Earthquake: return "One of your roads is destroyed.";
                case EffectCard.Bandits: return "Move the robber and steal, right now.";
                default: return "";
            }
        }
    }
}
