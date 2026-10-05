using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Conquer.Client
{
    /// <summary>What a LAN host announces: where its server listens and which room to join.</summary>
    public sealed class LanGame
    {
        public const int MaxNameChars = 24;

        public IPAddress Address { get; init; }
        public int Port { get; init; }
        public string Code { get; init; }
        public string Host { get; init; }
        public int Players { get; init; }
        public int Seats { get; init; }
        public DateTime Seen { get; init; }

        /// <summary>What to type in the Online screen's server box to reach this game by hand.</summary>
        public string Server => $"{Address}:{Port}";

        const string Magic = "CONQUER-LAN 1";

        public byte[] Encode() => Encoding.UTF8.GetBytes(string.Join("\n", Magic, Port, Code, Clean(Host), Players, Seats));

        /// <summary>
        /// Reads an announcement. Anyone on the network can send one, so everything is checked and the address is
        /// always the packet's sender, never something the packet claims. Returns null for anything malformed.
        /// </summary>
        public static LanGame Decode(byte[] data, IPAddress sender, DateTime now)
        {
            if (data == null || data.Length > 512 || sender == null) return null;
            string[] parts;
            try
            {
                parts = new UTF8Encoding(false, true).GetString(data).Split('\n');
            }
            catch (ArgumentException)
            {
                return null;
            }
            if (parts.Length != 6 || parts[0] != Magic) return null;
            if (!int.TryParse(parts[1], out int port) || port < 1 || port > 65535) return null;
            string code = parts[2];
            if (code.Length == 0 || code.Length > 16 || !code.All(char.IsLetterOrDigit)) return null;
            if (!int.TryParse(parts[4], out int players) || !int.TryParse(parts[5], out int seats)) return null;
            if (seats < 2 || seats > 6 || players < 0 || players > seats) return null;
            return new LanGame
            {
                Address = sender.IsIPv4MappedToIPv6 ? sender.MapToIPv4() : sender,
                Port = port,
                Code = code,
                Host = Clean(parts[3]),
                Players = players,
                Seats = seats,
                Seen = now,
            };
        }

        static string Clean(string name)
        {
            string s = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (s.Length > MaxNameChars) s = s.Substring(0, MaxNameChars);
            return s.Length == 0 ? "Someone" : s;
        }
    }

    /// <summary>
    /// Finding games on the local network: a host broadcasts a small UDP announcement every couple of seconds and
    /// the LAN screen listens for them. Nothing here is trusted; joining still goes through the room code check.
    /// </summary>
    public static class LanDiscovery
    {
        public const int Port = 47621;
        static readonly TimeSpan Interval = TimeSpan.FromSeconds(1.5);
        static readonly TimeSpan Forget = TimeSpan.FromSeconds(6);

        /// <summary>
        /// Announces a hosted game until disposed. <paramref name="current"/> is read on each tick (from a worker
        /// thread) and may return null to skip that tick.
        /// </summary>
        public static IDisposable Announce(Func<LanGame> current)
        {
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
                while (!cts.IsCancellationRequested)
                {
                    LanGame game = current();
                    if (game != null)
                    {
                        byte[] packet = game.Encode();
                        foreach (IPAddress target in BroadcastAddresses())
                        {
                            try
                            {
                                await udp.SendAsync(packet, packet.Length, new IPEndPoint(target, Port));
                            }
                            catch (SocketException)
                            {
                                // An interface going down mid-game shouldn't stop the others.
                            }
                        }
                    }
                    try
                    {
                        await Task.Delay(Interval, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            });
            return new Stopper(cts);
        }

        /// <summary>
        /// Listens for announcements until disposed and calls <paramref name="changed"/> (on a worker thread) with the
        /// games currently heard, newest first. Throws <see cref="SocketException"/> if the port can't be opened.
        /// </summary>
        public static IDisposable Listen(Action<IReadOnlyList<LanGame>> changed)
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.EnableBroadcast = true;
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, Port));

            var cts = new CancellationTokenSource();
            var games = new Dictionary<string, LanGame>();
            string last = "";

            void Publish()
            {
                DateTime now = DateTime.UtcNow;
                foreach (string key in games.Where(g => now - g.Value.Seen > Forget).Select(g => g.Key).ToList()) games.Remove(key);
                List<LanGame> list = games.Values.OrderBy(g => g.Host, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Server).ToList();
                string shape = string.Join("|", list.Select(g => $"{g.Server}/{g.Code}/{g.Host}/{g.Players}/{g.Seats}"));
                if (shape == last) return;
                last = shape;
                changed(list);
            }

            _ = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                        wait.CancelAfter(Interval);
                        UdpReceiveResult r = await udp.ReceiveAsync(wait.Token);
                        LanGame game = LanGame.Decode(r.Buffer, r.RemoteEndPoint.Address, DateTime.UtcNow);
                        string key = game == null ? null : game.Server + "/" + game.Code;
                        if (key != null && (games.Count < 64 || games.ContainsKey(key))) games[key] = game;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
                    {
                        if (cts.IsCancellationRequested) break;
                    }
                    Publish();
                }
            });
            return new Stopper(cts, udp);
        }

        /// <summary>This computer's IPv4 addresses on the local network, for telling people where to connect.</summary>
        public static IReadOnlyList<IPAddress> LocalAddresses() =>
            Interfaces().Select(u => u.Address).Where(a => !a.ToString().StartsWith("169.254.", StringComparison.Ordinal)).Distinct().ToList();

        static IEnumerable<IPAddress> BroadcastAddresses()
        {
            var seen = new HashSet<IPAddress>();
            foreach (UnicastIPAddressInformation u in Interfaces())
            {
                if (u.IPv4Mask == null) continue;
                byte[] ip = u.Address.GetAddressBytes(), mask = u.IPv4Mask.GetAddressBytes();
                var b = new byte[4];
                for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                var addr = new IPAddress(b);
                if (seen.Add(addr)) yield return addr;
            }
            if (seen.Add(IPAddress.Broadcast)) yield return IPAddress.Broadcast;
        }

        static IEnumerable<UnicastIPAddressInformation> Interfaces()
        {
            NetworkInterface[] all;
            try
            {
                all = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException)
            {
                yield break;
            }
            foreach (NetworkInterface n in all)
            {
                if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation u in n.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(u.Address)) yield return u;
                }
            }
        }

        sealed class Stopper : IDisposable
        {
            readonly CancellationTokenSource _cts;
            readonly IDisposable _also;

            public Stopper(CancellationTokenSource cts, IDisposable also = null)
            {
                _cts = cts;
                _also = also;
            }

            public void Dispose()
            {
                _cts.Cancel();
                _also?.Dispose();
            }
        }
    }
}
