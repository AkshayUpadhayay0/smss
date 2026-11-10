using System.Net;
using smss_api_service_layer.helper;
using Xunit;

namespace smss_api_tests
{
    public class ClientAddressTests
    {
        private static string Key(string ip) => ClientAddress.PartitionKey(IPAddress.Parse(ip));

        [Fact] public void Ipv4_is_its_own_bucket() { Assert.Equal("203.0.113.5", Key("203.0.113.5")); Assert.NotEqual(Key("203.0.113.5"), Key("203.0.113.6")); }
        [Fact] public void Ipv4_mapped_ipv6_shares_the_ipv4_bucket() => Assert.Equal(Key("203.0.113.5"), Key("::ffff:203.0.113.5"));
        [Fact] public void Addresses_in_one_ipv6_64_share_a_bucket() => Assert.Equal(Key("2001:db8:1:2::1"), Key("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));
        [Fact] public void Different_ipv6_64s_are_different_buckets() => Assert.NotEqual(Key("2001:db8:1:2::1"), Key("2001:db8:1:3::1"));
        [Fact] public void Ipv6_bucket_is_labelled_with_its_prefix() => Assert.Equal("2001:db8:1:2::/64", Key("2001:db8:1:2::1"));
        [Fact] public void Missing_address_shares_one_bucket_instead_of_being_unlimited() => Assert.Equal("unknown", ClientAddress.PartitionKey(null));
    }
}
