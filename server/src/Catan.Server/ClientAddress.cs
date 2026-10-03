using System.Net;
using System.Net.Sockets;

namespace Catan.Server
{
    public static class ClientAddress
    {
        /// <summary>
        /// The key per-IP limits count against. IPv6 users usually get a whole /64, so counting single
        /// addresses would let one person rotate through billions of them; the /64 prefix is the "one person".
        /// </summary>
        public static string Key(IPAddress address)
        {
            if (address == null) return "unknown";
            if (address.IsIPv4MappedToIPv6) return address.MapToIPv4().ToString();
            if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

            byte[] bytes = address.GetAddressBytes();
            for (int i = 8; i < 16; i++) bytes[i] = 0;
            return new IPAddress(bytes) + "/64";
        }
    }
}
