using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Conquer.Core.Net;
using Conquer.Server.Bots;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Conquer.Server
{
    /// <summary>
    /// Owns all rooms: creates them under the room caps, finds them by code, ticks them, and forgets them once
    /// they close. Room codes are 9 characters from a 31-letter alphabet with no look-alikes (about 44 bits),
    /// drawn from a CSPRNG, and lookups are rate limited per IP by the caller, so codes can't be guessed.
    /// </summary>
    public sealed class RoomRegistry : BackgroundService
    {
        public const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        public const int CodeLength = 9;

        readonly ConcurrentDictionary<string, Room> _rooms = new ConcurrentDictionary<string, Room>();
        readonly CountLimiter _roomLimits;
        readonly ServerOptions _options;
        readonly Func<double> _clock;
        readonly BotDirector _bots;
        readonly ILogger<RoomRegistry> _log;

        public RoomRegistry(ServerOptions options, Func<double> clock, BotDirector bots, ILogger<RoomRegistry> log)
        {
            _options = options;
            _clock = clock;
            _bots = bots;
            _log = log;
            _roomLimits = new CountLimiter(options.MaxRoomsPerIp, options.MaxRooms);
        }

        public int Count => _rooms.Count;

        public static string NewCode() => RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);

        /// <summary>Returns the new room, or null with <paramref name="error"/> set.</summary>
        public Room TryCreate(Connection host, JoinRequest hostJoin, int maxPlayers, int bots, out string error)
        {
            if (!_roomLimits.TryAcquire(host.Ip))
            {
                error = "The server has too many rooms open right now (or you already have some). Try again later.";
                return null;
            }

            string code;
            do code = NewCode(); while (_rooms.ContainsKey(code));

            var room = new Room(code, host, hostJoin, maxPlayers, bots, _options, _clock, _bots, _log);
            _rooms[code] = room;
            room.Start().ContinueWith(_ =>
            {
                _rooms.TryRemove(code, out Room _);
                _roomLimits.Release(room.CreatorIp);
            }, TaskScheduler.Default);
            error = null;
            return room;
        }

        public Room Find(string code) => code != null && _rooms.TryGetValue(code, out Room r) ? r : null;

        /// <summary>One timer for every room (rather than one per room) drives idle and lifetime checks.</summary>
        protected override async Task ExecuteAsync(CancellationToken stopping)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            try
            {
                while (await timer.WaitForNextTickAsync(stopping))
                    foreach (Room room in _rooms.Values) room.PostTick();
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
