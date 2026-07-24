using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LongBetterWindows.Host.Services;

public sealed record SparsePackageState
{
    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("installed")]
    public bool Installed { get; init; }

    [JsonPropertyName("identity_name")]
    public string IdentityName { get; init; } = "Long.LongBetterWindows";

    [JsonPropertyName("package_full_name")]
    public string? PackageFullName { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; init; }

    [JsonPropertyName("architecture")]
    public string? Architecture { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "Unknown";

    [JsonPropertyName("external_location")]
    public string? ExternalLocation { get; init; }

    [JsonPropertyName("package_sha256")]
    public string? PackageSha256 { get; init; }
}

public enum SparsePackageErrorCode
{
    None = 0,
    ScriptMissing = 6000,
    ProcessFailed = 6001,
    InvalidState = 6002,
    TimedOut = 6003,
    Cancelled = 6004,
    UnexpectedFailure = 6005,
}

public sealed record SparsePackageOperationResult(
    bool IsSuccess,
    string Message,
    SparsePackageState? State)
{
    public SparsePackageErrorCode ErrorCode { get; init; }
}

internal sealed record SparsePackageProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal interface ISparsePackageProcessRunner
{
    Task<SparsePackageProcessResult> RunAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal sealed class SparsePackagePowerShellRunner : ISparsePackageProcessRunner
{
    public async Task<SparsePackageProcessResult> RunAsync(
        string scriptPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Sparse Package 管理进程");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        return new SparsePackageProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }
}

public sealed class SparsePackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _scriptPath;
    private readonly ISparsePackageProcessRunner _runner;

    public SparsePackageService()
        : this(
            Path.Combine(
                AppContext.BaseDirectory,
                "Scripts",
                "manage-sparse-package.ps1"),
            new SparsePackagePowerShellRunner())
    {
    }

    internal SparsePackageService(
        string scriptPath,
        ISparsePackageProcessRunner runner)
    {
        _scriptPath = scriptPath;
        _runner = runner;
    }

    public Task<SparsePackageOperationResult> GetStatusAsync(
        CancellationToken cancellationToken = default)
        => ExecuteAsync(["-Action", "Status"], cancellationToken);

    public Task<SparsePackageOperationResult> RegisterOrUpgradeAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        return ExecuteAsync(
            [
                "-Action", "Register",
                "-PackagePath", Path.GetFullPath(packagePath),
                "-ExternalLocation", Path.GetFullPath(AppContext.BaseDirectory),
            ],
            cancellationToken);
    }

    public Task<SparsePackageOperationResult> UnregisterAsync(
        CancellationToken cancellationToken = default)
        => ExecuteAsync(["-Action", "Unregister"], cancellationToken);

    private async Task<SparsePackageOperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_scriptPath))
        {
            return new SparsePackageOperationResult(
                false,
                "Sparse Package 管理脚本未随应用发布",
                null)
            {
                ErrorCode = SparsePackageErrorCode.ScriptMissing,
            };
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var process = await _runner.RunAsync(
                _scriptPath,
                arguments,
                timeout.Token);
            var state = ParseLastState(process.StandardOutput);
            var message = state?.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = string.IsNullOrWhiteSpace(process.StandardError)
                    ? "Sparse Package 管理脚本没有返回有效状态"
                    : process.StandardError.Trim();
            }

            var isSuccess = process.ExitCode == 0 && state?.Succeeded == true;
            return new SparsePackageOperationResult(isSuccess, message, state)
            {
                ErrorCode = isSuccess
                    ? SparsePackageErrorCode.None
                    : state is null
                        ? SparsePackageErrorCode.InvalidState
                        : SparsePackageErrorCode.ProcessFailed,
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SparsePackageOperationResult(
                false,
                "Sparse Package 操作等待超时",
                null)
            {
                ErrorCode = SparsePackageErrorCode.TimedOut,
            };
        }
        catch (OperationCanceledException exception)
        {
            return new SparsePackageOperationResult(
                false,
                exception.Message,
                null)
            {
                ErrorCode = SparsePackageErrorCode.Cancelled,
            };
        }
        catch (Exception exception)
        {
            return new SparsePackageOperationResult(
                false,
                exception.Message,
                null)
            {
                ErrorCode = SparsePackageErrorCode.UnexpectedFailure,
            };
        }
    }

    private static SparsePackageState? ParseLastState(string output)
    {
        foreach (var line in output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            if (!line.StartsWith('{') || !line.EndsWith('}'))
                continue;
            try
            {
                return JsonSerializer.Deserialize<SparsePackageState>(
                    line,
                    JsonOptions);
            }
            catch (JsonException)
            {
                // PowerShell may emit progress or native tool output before the final JSON.
            }
        }

        return null;
    }
}
