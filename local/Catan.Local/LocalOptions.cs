using System;
using System.Globalization;
using Catan.Core;

namespace Catan.Local
{
    /// <summary>
    /// Command-line options for a one-computer game. With no options the normal new-game screen opens; with any
    /// game option the game starts straight away, which makes repeat testing quick (a seed replays the same board).
    /// </summary>
    public sealed class LocalOptions
    {
        public int Players { get; private set; } = 3;
        public int Radius { get; private set; } = 2;
        public int VictoryPoints { get; private set; } = 10;
        public bool HideHands { get; private set; } = true;
        public int? Seed { get; private set; }
        public bool Animations { get; private set; } = true;

        /// <summary>True when a game option was given, so the new-game screen is skipped.</summary>
        public bool QuickStart { get; private set; }

        public bool ShowHelp { get; private set; }

        public const string Usage =
@"CatanLocal: Catan on one computer, no server needed.

  CatanLocal                      open the new-game screen
  CatanLocal [options]            start a game straight away

Options:
  --players N      number of players, 2-6 (default 3)
  --radius R       board radius, 1-6 (default 2, the classic 19 tiles)
  --vp N           points to win, 3-20 (default 10)
  --seed S         fixed seed: the same seed gives the same board and dice
  --show-hands     don't hide hands between turns (handy when testing alone)
  --no-animations  turn animations off
  --help           show this text";

        public static LocalOptions Parse(string[] args)
        {
            var o = new LocalOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                switch (a)
                {
                    case "--players": o.Players = Number(args, ref i, 2, 6); o.QuickStart = true; break;
                    case "--radius": o.Radius = Number(args, ref i, BoardGenerator.MinRadius, 6); o.QuickStart = true; break;
                    case "--vp": o.VictoryPoints = Number(args, ref i, 3, 20); o.QuickStart = true; break;
                    case "--seed": o.Seed = Number(args, ref i, int.MinValue, int.MaxValue); o.QuickStart = true; break;
                    case "--show-hands": o.HideHands = false; o.QuickStart = true; break;
                    case "--no-animations": o.Animations = false; break;
                    case "--help": case "-h": case "/?": o.ShowHelp = true; break;
                    default: throw new ArgumentException($"Unknown option '{args[i]}'. Run with --help to see the options.");
                }
            }
            return o;
        }

        static int Number(string[] args, ref int i, int min, int max)
        {
            string name = args[i];
            if (i + 1 >= args.Length) throw new ArgumentException($"{name} needs a number.");
            string text = args[++i];
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                throw new ArgumentException($"{name} needs a number, not '{text}'.");
            if (v < min || v > max) throw new ArgumentException($"{name} must be between {min} and {max}.");
            return v;
        }
    }
}
