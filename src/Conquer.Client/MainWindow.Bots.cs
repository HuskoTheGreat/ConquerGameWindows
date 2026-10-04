using System;
using Avalonia.Threading;
using Conquer.Core.Bots;

namespace Conquer.Client
{
    public partial class MainWindow
    {
        // New-game choices for computer players.
        int _setupBots = 2;
        BotDifficulty _setupBotLevel = BotDifficulty.Normal;

        DispatcherTimer _botTimer;

        /// <summary>Paces computer players: one move per tick, so people can follow along.</summary>
        void EnsureBotTimer()
        {
            if (_botTimer != null) return;
            _botTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _botTimer.Tick += (_, _) =>
            {
                if (_modal != Modal.None && _modal != Modal.Rules) return; // don't move under an open dialog
                _c.StepBot();
            };
            _botTimer.Start();
        }
    }
}
