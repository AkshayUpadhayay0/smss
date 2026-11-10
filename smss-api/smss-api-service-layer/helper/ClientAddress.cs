using System.Net;
using System.Net.Sockets;

namespace smss_api_service_layer.helper
{
    /// <summary>How a client address is turned into a rate-limit bucket. Pure, so it is unit tested.</summary>
    public static class ClientAddress
    {
        public const string Unknown = "unknown";

        /// <summary>
        /// One bucket per client: an IPv4 address is its own bucket (an IPv4-mapped IPv6 address is treated as the IPv4 one), and an IPv6 address shares a bucket
        /// with its whole /64, because one subscriber is normally given a whole /64 and could otherwise get a fresh address, and a fresh limit, for every request.
        /// A missing address (not expected behind Kestrel) shares one bucket rather than being unlimited.
        /// </summary>
        public static string PartitionKey(IPAddress? address)
        {
            if (address == null) return Unknown;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

            var bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes) + "/64";
        }
    }
}
