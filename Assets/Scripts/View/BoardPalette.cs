using Catan.Core;
using UnityEngine;

namespace Catan.View
{
    public static class BoardPalette
    {
        public static readonly Color Sea = new Color(0.16f, 0.38f, 0.62f);
        public static readonly Color Token = new Color(0.96f, 0.92f, 0.8f);
        public static readonly Color TokenText = new Color(0.12f, 0.1f, 0.08f);
        public static readonly Color TokenHotText = new Color(0.75f, 0.1f, 0.1f);
        public static readonly Color Robber = new Color(0.1f, 0.1f, 0.1f);
        public static readonly Color GenericPort = new Color(0.85f, 0.85f, 0.88f);

        public static Color For(Resource r)
        {
            switch (r)
            {
                case Resource.Wood: return new Color(0.16f, 0.42f, 0.2f);
                case Resource.Brick: return new Color(0.72f, 0.32f, 0.2f);
                case Resource.Sheep: return new Color(0.56f, 0.8f, 0.36f);
                case Resource.Wheat: return new Color(0.93f, 0.78f, 0.25f);
                case Resource.Ore: return new Color(0.5f, 0.52f, 0.58f);
                default: return new Color(0.86f, 0.79f, 0.56f); // desert
            }
        }

        static readonly Color[] PlayerColors =
        {
            new Color(0.86f, 0.2f, 0.2f),  // red
            new Color(0.2f, 0.45f, 0.9f),  // blue
            new Color(0.95f, 0.95f, 0.95f), // white
            new Color(0.95f, 0.55f, 0.1f), // orange
            new Color(0.2f, 0.7f, 0.35f),  // green
            new Color(0.65f, 0.3f, 0.8f),  // purple
        };

        public static Color Player(int id) => PlayerColors[id % PlayerColors.Length];

        public static string ShortName(Resource r) => r == Resource.Desert ? "Any" : r.ToString();

        /// <summary>A simple lit material that works on both URP and the built-in pipeline.</summary>
        public static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            return new Material(shader) { color = color };
        }
    }
}
