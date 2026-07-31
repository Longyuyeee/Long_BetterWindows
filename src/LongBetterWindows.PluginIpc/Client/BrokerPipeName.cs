using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace LongBetterWindows.PluginIpc.Client;

public static class BrokerPipeName
{
    public static string ForCurrentUser() =>
        ForSid(WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user does not have a SID."));

    public static string ForSid(string sid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(sid));
        return $"long-plugin-broker-v1-{Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant()}";
    }
}
