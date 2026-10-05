using System;
using System.Collections.Generic;
using System.Linq;

namespace Conquer.Core
{
    /// <summary>
    /// A board being arranged before a game: start from a random one, then change any tile's resource or number,
    /// shuffle, or reroll the harbors. <see cref="Build"/> turns it into a real <see cref="Board"/> through the same
    /// validation a board received over the network gets, so nothing made here can break the rules engine.
    /// </summary>
    public sealed class BoardDraft
    {
        /// <summary>Number tokens a tile can carry (7 never is one).</summary>
        public static readonly int[] Numbers = { 2, 3, 4, 5, 6, 8, 9, 10, 11, 12 };

        /// <summary>What a tile can be: the five resources, then the wasteland.</summary>
        public static readonly Resource[] Kinds = ResourceSet.Types.Append(Resource.Wasteland).ToArray();

        readonly Dictionary<Hex, Resource> _resources = new Dictionary<Hex, Resource>();
        readonly Dictionary<Hex, int> _numbers = new Dictionary<Hex, int>();

        // A tile turned into wasteland remembers its number, so turning it back restores it.
        readonly Dictionary<Hex, int> _remembered = new Dictionary<Hex, int>();
        List<Port> _ports;

        public int Radius { get; }

        /// <summary>Every tile, center outwards.</summary>
        public IReadOnlyList<Hex> Hexes { get; }

        public IReadOnlyList<Port> Ports => _ports;

        /// <summary>Goes up on every change, so a screen can tell it needs redrawing.</summary>
        public int Version { get; private set; }

        BoardDraft(Board board)
        {
            Radius = board.Radius;
            Hexes = Hex.Spiral(Hex.Zero, board.Radius).ToList();
            foreach (Tile t in board.Tiles)
            {
                _resources[t.Hex] = t.Resource;
                _numbers[t.Hex] = t.Number;
            }
            _ports = board.Ports.ToList();
        }

        /// <summary>A copy of an existing board to edit.</summary>
        public static BoardDraft From(Board board) => new BoardDraft(board ?? throw new ArgumentNullException(nameof(board)));

        /// <summary>A fresh random board, exactly what a game with this radius and seed would get.</summary>
        public static BoardDraft Random(int radius, int seed) => new BoardDraft(BoardGenerator.Generate(new BoardConfig { Radius = radius, Seed = seed }));

        public Resource ResourceAt(Hex h) => _resources[h];

        /// <summary>The tile's number token, or 0 for the wasteland.</summary>
        public int NumberAt(Hex h) => _numbers[h];

        public bool Contains(Hex h) => _resources.ContainsKey(h);

        public void SetResource(Hex h, Resource resource)
        {
            Resource old = _resources[h];
            if (old == resource) return;
            if (resource == Resource.Wasteland)
            {
                _remembered[h] = _numbers[h];
                _numbers[h] = 0;
            }
            else if (old == Resource.Wasteland)
            {
                _numbers[h] = _remembered.TryGetValue(h, out int n) ? n : FreshNumber();
            }
            _resources[h] = resource;
            Version++;
        }

        /// <summary>Sets a land tile's number. The wasteland has none, so it's ignored there.</summary>
        public void SetNumber(Hex h, int number)
        {
            if (Array.IndexOf(Numbers, number) < 0) throw new ArgumentOutOfRangeException(nameof(number));
            if (_resources[h] == Resource.Wasteland || _numbers[h] == number) return;
            _numbers[h] = number;
            Version++;
        }

        /// <summary>Swaps two tiles, number and all.</summary>
        public void Swap(Hex a, Hex b)
        {
            if (a == b) return;
            (_resources[a], _resources[b]) = (_resources[b], _resources[a]);
            (_numbers[a], _numbers[b]) = (_numbers[b], _numbers[a]);
            Version++;
        }

        /// <summary>Moves the same tiles to new places. The wasteland takes no number and the land keeps its set of numbers.</summary>
        public void ShuffleTiles(int seed)
        {
            var rng = new Rng(seed);
            List<Resource> kinds = Hexes.Select(h => _resources[h]).ToList();
            rng.Shuffle(kinds);
            List<int> tokens = LandNumbers();
            for (int i = 0; i < Hexes.Count; i++) _resources[Hexes[i]] = kinds[i];
            DealNumbers(tokens, rng);
        }

        /// <summary>Deals the same numbers out again over the land, keeping 6s and 8s apart where it can.</summary>
        public void ShuffleNumbers(int seed) => DealNumbers(LandNumbers(), new Rng(seed));

        /// <summary>New harbors around the same coast.</summary>
        public void ShuffleHarbors(int seed)
        {
            var tiles = Hexes.ToDictionary(h => h, h => new Tile(h, _resources[h], _numbers[h]));
            _ports = BoardGenerator.BuildPorts(Radius, tiles, new Rng(seed));
            Version++;
        }

        /// <summary>How many tiles of each kind the board has.</summary>
        public int Count(Resource kind) => _resources.Values.Count(r => r == kind);

        /// <summary>Things worth knowing about the layout. None of them stop a game.</summary>
        public IReadOnlyList<string> Notes()
        {
            var notes = new List<string>();
            int hot = BoardGenerator.HotViolations(LandNumberMap(), out _);
            if (hot > 0) notes.Add("Some 6s and 8s sit next to each other, so those spots will be very rich.");
            if (Count(Resource.Wasteland) == 0) notes.Add("No wasteland: the raider starts on the center tile.");
            foreach (Resource r in ResourceSet.Types)
            {
                if (Count(r) == 0) notes.Add($"No {r} tiles: {r} only comes from trading and cards.");
            }
            return notes;
        }

        /// <summary>The board to play on. Throws <see cref="ArgumentException"/> if the draft is somehow invalid.</summary>
        public Board Build() =>
            Board.FromData(Radius, Hexes.Select(h => new Tile(h, _resources[h], _numbers[h])), _ports);

        List<int> LandNumbers() => Hexes.Select(h => _numbers[h]).Where(n => n != 0).ToList();

        Dictionary<Hex, int> LandNumberMap() => Hexes.Where(h => _resources[h] != Resource.Wasteland).ToDictionary(h => h, h => _numbers[h]);

        void DealNumbers(List<int> tokens, Rng rng)
        {
            List<Hex> land = Hexes.Where(h => _resources[h] != Resource.Wasteland).ToList();
            // A shuffle can move the wasteland, so the land count can differ from the numbers we had.
            while (tokens.Count < land.Count) tokens.Add(Numbers[rng.Next(Numbers.Length)]);
            if (tokens.Count > land.Count) tokens.RemoveRange(land.Count, tokens.Count - land.Count);

            Dictionary<Hex, int> dealt = BoardGenerator.PlaceTokens(land, tokens, rng);
            foreach (Hex h in Hexes) _numbers[h] = dealt.TryGetValue(h, out int n) ? n : 0;
            _remembered.Clear();
            Version++;
        }

        /// <summary>A number for a tile that just stopped being wasteland: the rarest standard one on the board.</summary>
        int FreshNumber()
        {
            var counts = Numbers.ToDictionary(n => n, n => 0);
            foreach (int n in _numbers.Values)
            {
                if (counts.ContainsKey(n)) counts[n]++;
            }
            // Prefer middling numbers when tied, so a new tile is neither dead nor a 6/8.
            int[] order = { 5, 9, 4, 10, 3, 11, 2, 12, 6, 8 };
            return order.OrderBy(n => counts[n]).First();
        }
    }
}
