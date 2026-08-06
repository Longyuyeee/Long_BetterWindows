using System.IO;
using LongBetterWindows.Host.Contracts;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Host.Interaction
{
    public sealed class ExplorerContextProvider : IContextProvider
    {
        private readonly ShellSelectionService _shellSelection;

        public ExplorerContextProvider(ShellSelectionService shellSelection)
        {
            _shellSelection = shellSelection;
        }

        public string Id => "explorer";
        public int Priority => 200;

        public async Task<IReadOnlyList<ContextItem>> CaptureAsync(
            ContextCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = await _shellSelection.GetSelectedItemsForWindowAsync(
                request.ForegroundWindowHandle);
            cancellationToken.ThrowIfCancellationRequested();

            if (selection.IsSuccess && selection.Data is { Count: > 0 } paths)
            {
                var inputTypes = ContextInputClassifier.ClassifyExplorerSelection(paths);
                var label = paths.Count > 1
                    ? $"Explorer 选区 · {paths.Count} 项"
                    : $"Explorer 选区 · {Path.GetFileName(paths[0])}";
                return new[]
                {
                    new ContextItem
                    {
                        Id = "explorer.selection",
                        Source = ContextSource.ExplorerSelection,
                        Label = label,
                        Paths = paths,
                        CompatibleInputTypes = inputTypes,
                        Sensitivity = ContextSensitivity.Sensitive,
                    },
                };
            }

            var folder = await _shellSelection.GetExplorerFolderPathForWindowAsync(
                request.ForegroundWindowHandle);
            cancellationToken.ThrowIfCancellationRequested();
            if (!folder.IsSuccess || string.IsNullOrWhiteSpace(folder.Data))
                return Array.Empty<ContextItem>();

            var normalizedFolder = folder.Data.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(normalizedFolder);
            if (string.IsNullOrWhiteSpace(folderName))
                folderName = folder.Data;

            return new[]
            {
                new ContextItem
                {
                    Id = "explorer.folder",
                    Source = ContextSource.ActiveExplorerFolder,
                    Label = $"Explorer 文件夹 · {folderName}",
                    Paths = new[] { folder.Data },
                    CompatibleInputTypes = new[] { AcceptedInputType.Folder },
                    Sensitivity = ContextSensitivity.Sensitive,
                },
            };
        }
    }
}
