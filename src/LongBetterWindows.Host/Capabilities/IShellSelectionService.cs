using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Capabilities
{
    public interface IShellSelectionService
    {
        Task<HostApiResponse<string>> GetActiveExplorerFolderPathAsync();
        Task<HostApiResponse<List<string>>> GetSelectedItemsAsync();
        Task<HostApiResponse<ScreenRect>> GetSelectedItemScreenRectAsync();
    }

    public struct ScreenRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
