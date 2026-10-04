using System;

namespace Conquer.Core
{
    /// <summary>An immutable bag of the five tradeable resources (a hand, a bank, a cost, a trade side).</summary>
    public readonly struct ResourceSet : IEquatable<ResourceSet>
    {
        public static readonly Resource[] Types =
            { Resource.Timber, Resource.Clay, Resource.Livestock, Resource.Grain, Resource.Iron };

        public static readonly ResourceSet Empty = default;

        public readonly int Timber;
        public readonly int Clay;
        public readonly int Livestock;
        public readonly int Grain;
        public readonly int Iron;

        public ResourceSet(int timber = 0, int clay = 0, int livestock = 0, int grain = 0, int iron = 0)
        {
            Timber = timber;
            Clay = clay;
            Livestock = livestock;
            Grain = grain;
            Iron = iron;
        }

        public int this[Resource r]
        {
            get
            {
                switch (r)
                {
                    case Resource.Timber: return Timber;
                    case Resource.Clay: return Clay;
                    case Resource.Livestock: return Livestock;
                    case Resource.Grain: return Grain;
                    case Resource.Iron: return Iron;
                    default: return 0;
                }
            }
        }

        public int Total => Timber + Clay + Livestock + Grain + Iron;
        public bool IsEmpty => Total == 0;
        public bool HasNegative => Timber < 0 || Clay < 0 || Livestock < 0 || Grain < 0 || Iron < 0;

        public static ResourceSet Of(Resource r, int count = 1) => Empty.With(r, count);

        /// <summary>A copy with <paramref name="delta"/> added to one resource (Wasteland is ignored).</summary>
        public ResourceSet With(Resource r, int delta)
        {
            switch (r)
            {
                case Resource.Timber: return new ResourceSet(Timber + delta, Clay, Livestock, Grain, Iron);
                case Resource.Clay: return new ResourceSet(Timber, Clay + delta, Livestock, Grain, Iron);
                case Resource.Livestock: return new ResourceSet(Timber, Clay, Livestock + delta, Grain, Iron);
                case Resource.Grain: return new ResourceSet(Timber, Clay, Livestock, Grain + delta, Iron);
                case Resource.Iron: return new ResourceSet(Timber, Clay, Livestock, Grain, Iron + delta);
                default: return this;
            }
        }

        /// <summary>True if this set has at least everything in <paramref name="need"/>.</summary>
        public bool Contains(ResourceSet need) => !(this - need).HasNegative;

        public static ResourceSet operator +(ResourceSet a, ResourceSet b) =>
            new ResourceSet(a.Timber + b.Timber, a.Clay + b.Clay, a.Livestock + b.Livestock, a.Grain + b.Grain, a.Iron + b.Iron);

        public static ResourceSet operator -(ResourceSet a, ResourceSet b) =>
            new ResourceSet(a.Timber - b.Timber, a.Clay - b.Clay, a.Livestock - b.Livestock, a.Grain - b.Grain, a.Iron - b.Iron);

        public static bool operator ==(ResourceSet a, ResourceSet b) => a.Equals(b);
        public static bool operator !=(ResourceSet a, ResourceSet b) => !a.Equals(b);

        public bool Equals(ResourceSet o) =>
            Timber == o.Timber && Clay == o.Clay && Livestock == o.Livestock && Grain == o.Grain && Iron == o.Iron;

        public override bool Equals(object obj) => obj is ResourceSet o && Equals(o);
        public override int GetHashCode() => (((((Timber * 31) + Clay) * 31 + Livestock) * 31 + Grain) * 31) + Iron;
        public override string ToString() => $"W{Timber} B{Clay} S{Livestock} H{Grain} O{Iron}";

        /// <summary>Human-readable form for logs and UI, e.g. "2 Timber, 1 Iron" (or "nothing").</summary>
        public string Describe()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (Resource r in Types)
            {
                if (this[r] != 0) parts.Add($"{this[r]} {r}");
            }
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }
    }

    /// <summary>Standard build costs.</summary>
    public static class Costs
    {
        public static readonly ResourceSet Road = new ResourceSet(timber: 1, clay: 1);
        public static readonly ResourceSet Village = new ResourceSet(timber: 1, clay: 1, livestock: 1, grain: 1);
        public static readonly ResourceSet City = new ResourceSet(grain: 2, iron: 3);
        public static readonly ResourceSet ActionCard = new ResourceSet(livestock: 1, grain: 1, iron: 1);
    }
}
