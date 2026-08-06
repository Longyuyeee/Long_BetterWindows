using System.IO;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    internal static class ContextInputClassifier
    {
        private static readonly HashSet<string> ImageExtensions = new(
            StringComparer.OrdinalIgnoreCase)
        {
            ".avif", ".bmp", ".gif", ".heic", ".heif", ".ico", ".jpeg", ".jpg",
            ".png", ".tif", ".tiff", ".webp",
        };

        public static IReadOnlyList<AcceptedInputType> ClassifyExplorerSelection(
            IReadOnlyList<string> paths)
        {
            if (paths.Count == 0)
                return Array.Empty<AcceptedInputType>();
            if (paths.Count > 1)
            {
                return new[]
                {
                    AcceptedInputType.Files,
                    AcceptedInputType.ExplorerSelection,
                };
            }

            var path = paths[0];
            if (Directory.Exists(path))
            {
                return new[]
                {
                    AcceptedInputType.Folder,
                    AcceptedInputType.ExplorerSelection,
                };
            }

            if (ImageExtensions.Contains(Path.GetExtension(path)))
            {
                return new[]
                {
                    AcceptedInputType.Image,
                    AcceptedInputType.File,
                    AcceptedInputType.ExplorerSelection,
                };
            }

            return new[]
            {
                AcceptedInputType.File,
                AcceptedInputType.ExplorerSelection,
            };
        }
    }
}
