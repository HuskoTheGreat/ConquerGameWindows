using System.Collections.Generic;
using System.Linq;
using Conquer.Core.Net;
using Conquer.Core;

namespace Conquer.Client
{
    /// <summary>
    /// Online play: the same screen, but the game is the server's latest snapshot and every command goes to
    /// the server instead of the local engine. This window only ever acts for its own seat.
    /// </summary>
    public sealed partial class LocalGameController
    {
        OnlineSession _online;
        bool _onlineGameShown;

        public OnlineSession Online => _online;
        public bool IsOnline => _online != null;

        /// <summary>The seat this window plays: ours online, whoever's input is awaited in a local game.</summary>
        public int MySeat => IsOnline ? _online.Seat : Actor;

        /// <summary>True when this window may act for <paramref name="seat"/> right now.</summary>
        public bool CanActFor(int seat) => IsOnline ? seat == _online.Seat : !IsBot(seat);

        /// <summary>Plays through <paramref name="session"/> from now on. The local game, if any, is dropped.</summary>
        public void GoOnline(OnlineSession session)
        {
            LeaveOnline();
            _online = session;
            _onlineGameShown = false;
            session.SnapshotReceived += OnSnapshot;
            session.LogReceived += OnLog;
            session.ChatReceived += OnChat;
            session.ErrorReceived += ShowToast;
            session.Changed += OnOnlineChanged;

            Game = null;
            _botsGame = null;
            _bots.Clear();
            _log.Clear();
            Tool = Tool.None;
            HideHands = false;
            HandoffPending = false;
            Changed?.Invoke();
        }

        /// <summary>Disconnects (if still connected) and returns to having no game at all.</summary>
        public void LeaveOnline()
        {
            if (_online == null) return;
            OnlineSession s = _online;
            s.SnapshotReceived -= OnSnapshot;
            s.LogReceived -= OnLog;
            s.ChatReceived -= OnChat;
            s.ErrorReceived -= ShowToast;
            s.Changed -= OnOnlineChanged;
            _online = null;
            s.Leave();
            Game = null;
            _log.Clear();
            Changed?.Invoke();
        }

        void OnSnapshot(Game game)
        {
            Game = game;
            // The first snapshot of a room (or after a reconnect) is a fresh game to the animation layer.
            if (!_onlineGameShown)
            {
                _onlineGameShown = true;
                GameNumber++;
            }
            if (Tool != Tool.None && (game.Phase != Phase.Main || game.CurrentPlayer != _online.Seat)) Tool = Tool.None;
            Refresh();
        }

        void OnLog(IReadOnlyList<string> lines) => AddLog(lines);

        void OnChat(string line) => AddLog(new[] { line });

        void OnOnlineChanged()
        {
            // A reconnect brings a fresh welcome; the next snapshot starts the view over.
            if (_online.Status != OnlineStatus.Playing) _onlineGameShown = false;
            Changed?.Invoke();
        }

        void AddLog(IEnumerable<string> lines)
        {
            _log.AddRange(lines);
            if (_log.Count > 60) _log.RemoveRange(0, _log.Count - 60);
            Changed?.Invoke();
        }

        /// <summary>Online: hands the command to the server. Its answer arrives later as a snapshot or an error.</summary>
        bool SendOnline(Command command)
        {
            if (command.Player != _online.Seat)
            {
                ShowToast("It isn't your move.");
                return false;
            }
            _online.Send(command);
            if (command is BuildRoad || command is BuildVillage || command is BuildCity) Tool = Tool.None;
            Toast = null;
            return true;
        }

        string OnlinePrompt()
        {
            Game g = Game;
            int me = _online.Seat;
            if (g.Phase == Phase.GameOver) return g.Winner == me ? "You win!" : $"{g.Players[g.Winner].Name} wins!";
            if (g.Phase == Phase.Discard)
                return g.PendingDiscards.TryGetValue(me, out int owe)
                    ? $"Discard {owe} cards."
                    : "Waiting for " + string.Join(", ", g.PendingDiscards.Keys.Select(p => g.Players[p].Name)) + " to discard.";
            if (g.CurrentPlayer != me) return $"Waiting for {g.Players[g.CurrentPlayer].Name}...";
            return null;
        }
    }
}
