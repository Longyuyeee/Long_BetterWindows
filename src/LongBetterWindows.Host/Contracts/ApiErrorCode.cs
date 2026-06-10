namespace LongBetterWindows.Host.Contracts
{
    public enum ApiErrorCode
    {
        None = 0,
        Unknown = 1,
        NotSupported = 2,
        PermissionDenied = 3,
        InvalidArgument = 4,
        NotFound = 5,
        Conflict = 6,
        Win32Error = 7,
        Timeout = 8,
        NotNTFSVolume = 100,
        StreamNotFound = 101,
        RegistryKeyNotFound = 102,
        RegistryAccessDenied = 103,
        HotKeyConflict = 200,
        HotKeyRegistrationFailed = 201,
        ShellWindowNotFound = 300,
        ShellSelectionEmpty = 301,
        StorageKeyNotFound = 400,
    }
}
