using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Interaction
{
    internal static class QualityContextFixtures
    {
        public const string DefaultProfile = "url";
        private static readonly byte[] TransparentPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ" +
            "AAAADUlEQVR42mP8z8BQDwAFgwJ/l7LkXwAAAABJRU5ErkJggg==");

        public static readonly IReadOnlySet<string> Profiles = new HashSet<string>(
            new[] { "empty", "file", "files", "image", "text", "url" },
            StringComparer.OrdinalIgnoreCase);

        public static ContextSnapshot Create(string? profile)
        {
            var normalized = string.IsNullOrWhiteSpace(profile)
                ? DefaultProfile
                : profile.Trim().ToLowerInvariant();
            if (!Profiles.Contains(normalized))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(profile),
                    profile,
                    "Unknown quality context profile.");
            }

            var capturedAt = DateTimeOffset.UtcNow;
            return normalized switch
            {
                "empty" => ContextSnapshot.Empty,
                "file" => Snapshot(capturedAt, "quality.file", ContextSource.ExplorerSelection,
                    "Quality file",
                    new[] { AcceptedInputType.File, AcceptedInputType.ExplorerSelection },
                    paths: new[] { @"C:\LongQuality\document.txt" }),
                "files" => Snapshot(capturedAt, "quality.files", ContextSource.ExplorerSelection,
                    "Quality selection",
                    new[] { AcceptedInputType.Files, AcceptedInputType.ExplorerSelection },
                    paths: new[]
                    {
                        @"C:\LongQuality\one.txt",
                        @"C:\LongQuality\two.txt",
                    }),
                "image" => Snapshot(capturedAt, "quality.image", ContextSource.Clipboard,
                    "Quality image", new[] { AcceptedInputType.Image },
                    imagePng: TransparentPng),
                "text" => Snapshot(capturedAt, "quality.text", ContextSource.Clipboard,
                    "Quality text",
                    new[] { AcceptedInputType.Clipboard, AcceptedInputType.Text },
                    text: "Long quality text"),
                _ => Snapshot(capturedAt, "quality.url", ContextSource.Clipboard,
                    "Quality link",
                    new[]
                    {
                        AcceptedInputType.Url,
                        AcceptedInputType.Clipboard,
                        AcceptedInputType.Text,
                    },
                    text: "https://long.example/quality"),
            };
        }

        private static ContextSnapshot Snapshot(
            DateTimeOffset capturedAt,
            string id,
            ContextSource source,
            string label,
            IReadOnlyList<AcceptedInputType> inputTypes,
            string? text = null,
            IReadOnlyList<string>? paths = null,
            byte[]? imagePng = null)
            => new(capturedAt, new[]
            {
                new ContextItem
                {
                    Id = id,
                    Source = source,
                    Label = label,
                    Text = text,
                    Paths = paths ?? Array.Empty<string>(),
                    ImagePng = imagePng,
                    CompatibleInputTypes = inputTypes,
                    Sensitivity = ContextSensitivity.Sensitive,
                },
            });
    }
}
