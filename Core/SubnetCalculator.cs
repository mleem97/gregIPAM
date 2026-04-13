using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace DHCPSwitches;

public static class SubnetCalculator
{
    public static string GetNetworkAddress(string ip, int prefix)
    {
        if (!TryIpToUint(ip, out var ipBe))
        {
            return "";
        }

        var mask = PrefixToMask(prefix);
        return UintToIp(ipBe & mask);
    }

    public static string GetBroadcast(string ip, int prefix)
    {
        if (!TryIpToUint(ip, out var ipBe))
        {
            return "";
        }

        var mask = PrefixToMask(prefix);
        return UintToIp((ipBe & mask) | ~mask);
    }

    public static bool IsInSubnet(string ip, string cidr)
    {
        if (!ParseCidr(cidr, out var network, out var prefix))
        {
            return false;
        }

        if (!TryIpToUint(ip, out var ipBe))
        {
            return false;
        }

        var mask = PrefixToMask(prefix);
        return (ipBe & mask) == (network & mask);
    }

    public static int CountUsableAddresses(int prefix)
    {
        if (prefix < 0 || prefix > 32)
        {
            return 0;
        }

        if (prefix >= 31)
        {
            return 0;
        }

        var hostBits = 32 - prefix;
        return (int)(1u << hostBits) - 2;
    }

    public static bool IsValidCidr(string cidr)
    {
        return ParseCidr(cidr, out _, out _);
    }

    public static bool ParseCidr(string cidr, out uint networkBe, out int prefix)
    {
        networkBe = 0;
        prefix = 0;

        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        var slash = cidr.IndexOf('/');
        if (slash <= 0 || slash == cidr.Length - 1)
        {
            return false;
        }

        var ipPart = cidr.Substring(0, slash).Trim();
        var prefixPart = cidr.Substring(slash + 1).Trim();

        if (!TryIpToUint(ipPart, out networkBe))
        {
            return false;
        }

        if (!int.TryParse(prefixPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out prefix)
            || prefix < 0
            || prefix > 32)
        {
            return false;
        }

        networkBe &= PrefixToMask(prefix);
        return true;
    }

    public static IEnumerable<string> EnumerateHosts(string cidr)
    {
        if (!ParseCidr(cidr, out var network, out var prefix) || prefix >= 31)
        {
            yield break;
        }

        var hostBits = 32 - prefix;
        var block = 1u << hostBits;
        var first = network + 1;
        var last = network + block - 2;
        for (var current = first; current <= last; current++)
        {
            yield return UintToIp(current);
        }
    }

    private static uint PrefixToMask(int prefix)
    {
        if (prefix <= 0)
        {
            return 0;
        }

        if (prefix >= 32)
        {
            return uint.MaxValue;
        }

        return uint.MaxValue << (32 - prefix);
    }

    private static bool TryIpToUint(string ip, out uint value)
    {
        value = 0;
        if (!IPAddress.TryParse(ip, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var b = parsed.GetAddressBytes();
        value = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        return true;
    }

    private static string UintToIp(uint value)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}.{2}.{3}",
            (value >> 24) & 255,
            (value >> 16) & 255,
            (value >> 8) & 255,
            value & 255);
    }
}
