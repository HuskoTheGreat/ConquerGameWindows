using System;

namespace Conquer.Core
{
    /// <summary>An immutable bag of the five tradeable resources (a hand, a bank, a cost, a trade side).</summary>
    public readonly struct ResourceSet : IEquatable<ResourceSet>
    {
        public static readonly Resource[] Types =
            { Resource.Wood, Resource.Brick, Resource.Sheep, Resource.Wheat, Resource.Stone };

        public static readonly ResourceSet Empty = default;

        public readonly int Wood;
        public readonly int Brick;
        public readonly int Sheep;
        public readonly int Wheat;
        public readonly int Stone;

        public ResourceSet(int wood = 0, int brick = 0, int sheep = 0, int wheat = 0, int stone = 0)
        {
            Wood = wood;
            Brick = brick;
            Sheep = sheep;
            Wheat = wheat;
            Stone = stone;
        }

        public int this[Resource r]
        {
            get
            {
                switch (r)
                {
                    case Resource.Wood: return Wood;
                    case Resource.Brick: return Brick;
                    case Resource.Sheep: return Sheep;
                    case Resource.Wheat: return Wheat;
                    case Resource.Stone: return Stone;
                    default: return 0;
                }
            }
        }

        public int Total => Wood + Brick + Sheep + Wheat + Stone;
        public bool IsEmpty => Total == 0;
        public bool HasNegative => Wood < 0 || Brick < 0 || Sheep < 0 || Wheat < 0 || Stone < 0;

        public static ResourceSet Of(Resource r, int count = 1) => Empty.With(r, count);

        /// <summary>A copy with <paramref name="delta"/> added to one resource (Wasteland is ignored).</summary>
        public ResourceSet With(Resource r, int delta)
        {
            switch (r)
            {
                case Resource.Wood: return new ResourceSet(Wood + delta, Brick, Sheep, Wheat, Stone);
                case Resource.Brick: return new ResourceSet(Wood, Brick + delta, Sheep, Wheat, Stone);
                case Resource.Sheep: return new ResourceSet(Wood, Brick, Sheep + delta, Wheat, Stone);
                case Resource.Wheat: return new ResourceSet(Wood, Brick, Sheep, Wheat + delta, Stone);
                case Resource.Stone: return new ResourceSet(Wood, Brick, Sheep, Wheat, Stone + delta);
                default: return this;
            }
        }

        /// <summary>True if this set has at least everything in <paramref name="need"/>.</summary>
        public bool Contains(ResourceSet need) => !(this - need).HasNegative;

        public static ResourceSet operator +(ResourceSet a, ResourceSet b) =>
            new ResourceSet(a.Wood + b.Wood, a.Brick + b.Brick, a.Sheep + b.Sheep, a.Wheat + b.Wheat, a.Stone + b.Stone);

        public static ResourceSet operator -(ResourceSet a, ResourceSet b) =>
            new ResourceSet(a.Wood - b.Wood, a.Brick - b.Brick, a.Sheep - b.Sheep, a.Wheat - b.Wheat, a.Stone - b.Stone);

        public static bool operator ==(ResourceSet a, ResourceSet b) => a.Equals(b);
        public static bool operator !=(ResourceSet a, ResourceSet b) => !a.Equals(b);

        public bool Equals(ResourceSet o) =>
            Wood == o.Wood && Brick == o.Brick && Sheep == o.Sheep && Wheat == o.Wheat && Stone == o.Stone;

        public override bool Equals(object obj) => obj is ResourceSet o && Equals(o);
        public override int GetHashCode() => (((((Wood * 31) + Brick) * 31 + Sheep) * 31 + Wheat) * 31) + Stone;
        public override string ToString() => $"W{Wood} B{Brick} S{Sheep} H{Wheat} O{Stone}";

        /// <summary>Human-readable form for logs and UI, e.g. "2 Wood, 1 Stone" (or "nothing").</summary>
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
        public static readonly ResourceSet Road = new ResourceSet(wood: 1, brick: 1);
        public static readonly ResourceSet Village = new ResourceSet(wood: 1, brick: 1, sheep: 1, wheat: 1);
        public static readonly ResourceSet City = new ResourceSet(wheat: 2, stone: 3);
        public static readonly ResourceSet ActionCard = new ResourceSet(sheep: 1, wheat: 1, stone: 1);
    }
}
