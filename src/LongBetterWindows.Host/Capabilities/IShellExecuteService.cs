using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    /// <summary>
    /// Shell 执行能力——打开文件、文件夹、URL。
    /// 需要 manifest.json 中声明 "shell.execute" 能力。
    /// </summary>
    public interface IShellExecuteService
    {
        Task<HostApiResponse> OpenUrlAsync(string url);
        Task<HostApiResponse> OpenFolderAsync(string path);
        Task<HostApiResponse> OpenWithDefaultAsync(string path);
    }
}
