namespace LongBetterWindows.PluginIpc.Contracts;

public static class IpcErrorCodes
{
    public const string IncompatibleProtocol = "incompatible_protocol";
    public const string InvalidRequest = "invalid_request";
    public const string Unauthenticated = "unauthenticated";
    public const string HostUnavailable = "host_unavailable";
    public const string PluginNotFound = "plugin_not_found";
    public const string CommandNotFound = "command_not_found";
    public const string InstanceNotFound = "instance_not_found";
    public const string CapabilityDenied = "capability_denied";
    public const string SurfaceNotSupported = "surface_not_supported";
    public const string Timeout = "timeout";
    public const string Cancelled = "cancelled";
    public const string RateLimited = "rate_limited";
    public const string PluginCrashed = "plugin_crashed";
    public const string InternalError = "internal_error";

    public static bool IsKnown(string? code) =>
        code is IncompatibleProtocol
            or InvalidRequest
            or Unauthenticated
            or HostUnavailable
            or PluginNotFound
            or CommandNotFound
            or InstanceNotFound
            or CapabilityDenied
            or SurfaceNotSupported
            or Timeout
            or Cancelled
            or RateLimited
            or PluginCrashed
            or InternalError;

    public static string Normalize(string? code) =>
        IsKnown(code) ? code! : InternalError;
}
