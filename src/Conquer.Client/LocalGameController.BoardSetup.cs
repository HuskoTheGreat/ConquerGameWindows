using System;
using Conquer.Core;

namespace Conquer.Client
{
    /// <summary>
    /// Board preview for the board setup screen: shows a board with nobody on it, every tile clickable, and
    /// reports clicks instead of sending commands. Uses its own controller so the real game is never touched.
    /// </summary>
    public sealed partial class LocalGameController
    {
        bool _arranging;

        /// <summary>Raised for a click on a tile while a board is being arranged.</summary>
        public event Action<Hex> TileClicked;

        /// <summary>True while this controller only shows a board being arranged.</summary>
        public bool IsArranging => _arranging && Game != null;

        /// <summary>Shows <paramref name="board"/> for arranging. Call again after every change to redraw it.</summary>
        public void ShowBoard(Board board)
        {
            LeaveOnline();
            _arranging = true;
            Game = new Game(board, new GameConfig { PlayerCount = 2 });
            Tool = Tool.None;
            HideHands = false;
            HandoffPending = false;
            RebuildSpots();
            Changed?.Invoke();
        }

        void AddAllTiles()
        {
            foreach (Tile t in Game.Board.Tiles)
            {
                var p = HexLayout.ToPlane(t.Hex);
                _spots.Add(new Spot { Kind = SpotKind.Hex, Hex = t.Hex, X = p.X, Y = p.Y });
            }
        }
    }
}
