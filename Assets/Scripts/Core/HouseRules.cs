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

        public HouseRules Clone() => (HouseRules)MemberwiseClone();

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
            return null;
        }
    }
}
