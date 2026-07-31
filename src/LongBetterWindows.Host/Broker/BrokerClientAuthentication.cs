using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace LongBetterWindows.Host.Broker;

internal sealed record BrokerClientIdentity(string Sid, int SessionId, int IntegrityLevel);

internal interface IBrokerClientIdentityProbe
{
    BrokerClientIdentity GetServerIdentity();
    BrokerClientIdentity GetClientIdentity(NamedPipeServerStream pipe);
}

internal static class BrokerClientAuthentication
{
    internal static bool IsSameSecurityBoundary(
        BrokerClientIdentity server,
        BrokerClientIdentity client)
        => string.Equals(server.Sid, client.Sid, StringComparison.OrdinalIgnoreCase)
           && server.SessionId == client.SessionId
           && server.IntegrityLevel == client.IntegrityLevel;
}

internal sealed class WindowsBrokerClientIdentityProbe : IBrokerClientIdentityProbe
{
    public BrokerClientIdentity GetServerIdentity()
    {
        using var process = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        return new BrokerClientIdentity(
            identity.User?.Value ?? throw new UnauthorizedAccessException("Host SID is unavailable."),
            process.SessionId,
            GetIntegrityLevel(identity.AccessToken));
    }

    public BrokerClientIdentity GetClientIdentity(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var processId))
            throw new UnauthorizedAccessException("Client process identity is unavailable.");

        string? sid = null;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            sid = identity.User?.Value;
        });

        using var process = Process.GetProcessById(checked((int)processId));
        if (!OpenProcessToken(process.Handle, TokenQuery, out var token))
            throw new UnauthorizedAccessException("Client process token is unavailable.");
        using (token)
        {
            return new BrokerClientIdentity(
                sid ?? throw new UnauthorizedAccessException("Client SID is unavailable."),
                process.SessionId,
                GetIntegrityLevel(token));
        }
    }

    private static int GetIntegrityLevel(SafeAccessTokenHandle token)
    {
        _ = GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var length);
        if (length <= 0)
            throw new UnauthorizedAccessException("Token integrity metadata is unavailable.");

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
                throw new UnauthorizedAccessException("Token integrity metadata is unavailable.");
            var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(label.Label.Sid));
            return Marshal.ReadInt32(GetSidSubAuthority(label.Label.Sid, (uint)(count - 1)));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel { public SidAndAttributes Label; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);
}
