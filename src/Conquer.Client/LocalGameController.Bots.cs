using System.Collections.Generic;
using System.Linq;
using Conquer.Core;
using Conquer.Core.Bots;

namespace Conquer.Client
{
    /// <summary>Computer players in a local game. The window calls <see cref="StepBot"/> on a timer.</summary>
    public sealed partial class LocalGameController
    {
        readonly Dictionary<int, BotPlayer> _bots = new Dictionary<int, BotPlayer>();
        Game _botsGame;
        bool _botActing;

        /// <summary>
        /// Starts a game where the last <paramref name="bots"/> seats are computer players at
        /// <paramref name="difficulty"/>. At least one seat always stays human.
        /// </summary>
        public void NewGame(int players, int radius, int victoryPoints, bool hideHands, int bots, BotDifficulty difficulty,
            int? seed = null, IDice dice = null)
        {
            bots = System.Math.Clamp(bots, 0, players - 1);
            _bots.Clear();
            for (int seat = players - bots; seat < players; seat++)
                _bots[seat] = new BotPlayer(seat, difficulty, seed.HasValue ? seed.Value + seat : (int?)null);

            NewGame(players, radius, victoryPoints, hideHands, seed, dice);
            _botsGame = Game;
            Refresh(); // re-run with the bots known, so no handoff screen appears for a bot
        }

        /// <summary>True if <paramref name="seat"/> is a computer player in the current game.</summary>
        public bool IsBot(int seat) => _botsGame != null && _botsGame == Game && _bots.ContainsKey(seat);

        public BotDifficulty? BotLevel(int seat) => IsBot(seat) ? _bots[seat].Difficulty : (BotDifficulty?)null;

        public bool HasBots => _botsGame != null && _botsGame == Game && _bots.Count > 0;

        /// <summary>Lets one computer player make one move. Returns true if a bot acted.</summary>
        public bool StepBot()
        {
            if (!HasBots || Game.Phase == Phase.GameOver || HandoffPending) return false;

            foreach (BotPlayer bot in _bots.Values)
            {
                Command command = bot.Decide(Game);
                if (command == null) continue;

                _botActing = true;
                try
                {
                    if (!Send(command))
                    {
                        Command fallback = bot.Fallback(Game);
                        if (fallback != null) Send(fallback);
                    }
                }
                finally
                {
                    _botActing = false;
                }
                return true;
            }
            return false;
        }

        /// <summary>People can't click for a bot's seat.</summary>
        string BotSeatBlocks(Command command) =>
            !_botActing && IsBot(command.Player) ? $"Waiting for {Game.Players[command.Player].Name} ({BotPlayer.Describe(_bots[command.Player].Difficulty)} bot)." : null;
    }
}
