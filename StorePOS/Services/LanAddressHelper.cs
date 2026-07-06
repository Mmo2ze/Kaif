using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StorePOS.Services;

internal static class LanAddressHelper
{
    public static string? TryGetLanIPv4() => GetAllLanIPv4().FirstOrDefault();

    public static IReadOnlyList<string> GetAllLanIPv4()
    {
        var ips = new List<string>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                if (IPAddress.IsLoopback(ua.Address))
                    continue;
                if (!IsPrivateIPv4(ua.Address))
                    continue;

                var text = ua.Address.ToString();
                if (!ips.Contains(text, StringComparer.Ordinal))
                    ips.Add(text);
            }
        }

        return ips;
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        var b = address.GetAddressBytes();
        if (b[0] == 10)
            return true;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            return true;
        if (b[0] == 192 && b[1] == 168)
            return true;
        return false;
    }
}
