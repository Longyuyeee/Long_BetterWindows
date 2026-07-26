using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;

namespace LongBetterWindows.Tests;

public sealed class BrandingAssetTests
{
    [Fact]
    public void BrandName_IsConsistentAcrossVisibleProductSurfaces()
    {
        var root = FindRepositoryRoot();
        var zh = ReadJson(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "i18n",
            "zh-CN.json"));
        var en = ReadJson(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "i18n",
            "en-US.json"));
        Assert.Equal("Long助手", zh["app.title"].GetString());
        Assert.Equal("Long助手", zh["app.brand"].GetString());
        Assert.Equal("Long Assistant", en["app.title"].GetString());
        Assert.Equal("Long Assistant", en["app.brand"].GetString());

        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "LongBetterWindows.Host.csproj"));
        Assert.Contains("<Title>Long助手</Title>", project);
        Assert.Contains("<AssemblyTitle>Long助手</AssemblyTitle>", project);
        Assert.Contains("<Product>Long助手</Product>", project);
        Assert.Contains("<ApplicationIcon>..\\..\\Assets\\app.ico</ApplicationIcon>", project);

        XNamespace packageNamespace =
            "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace uapNamespace =
            "http://schemas.microsoft.com/appx/manifest/uap/windows10";
        var manifest = XDocument.Load(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Package",
            "appxmanifest.xml"));
        Assert.Equal(
            "Long助手",
            manifest.Root?
                .Element(packageNamespace + "Properties")?
                .Element(packageNamespace + "DisplayName")?
                .Value);
        Assert.Equal(
            "Long助手",
            manifest.Descendants(uapNamespace + "VisualElements")
                .Single()
                .Attribute("DisplayName")?
                .Value);
    }

    [Fact]
    public void BrandAssets_ProvideHighResolutionPngAndWindowsIconFrames()
    {
        var root = FindRepositoryRoot();
        var png = File.ReadAllBytes(Path.Combine(root, "Assets", "app-icon.png"));
        Assert.True(png.Length > 8);
        Assert.Equal(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            png[..8]);
        Assert.Equal(1024, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(1024, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        Assert.Equal(6, png[25]); // RGBA

        var icon = File.ReadAllBytes(Path.Combine(root, "Assets", "app.ico"));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(0, 2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(2, 2)));
        var frameCount = BinaryPrimitives.ReadUInt16LittleEndian(icon.AsSpan(4, 2));
        Assert.Equal(9, frameCount);
        var sizes = Enumerable.Range(0, frameCount)
            .Select(index =>
            {
                var value = icon[6 + index * 16];
                return value == 0 ? 256 : value;
            })
            .ToArray();
        Assert.True(
            sizes.SequenceEqual(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }),
            $"Unexpected icon sizes: {string.Join(", ", sizes)}");
    }

    [Fact]
    public void ApplicationShell_UsesBrandIconInsideTheInterface()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "MainWindow.xaml"));
        var toolCenter = File.ReadAllText(Path.Combine(
            root,
            "src",
            "LongBetterWindows.Host",
            "Views",
            "ToolCenterControl.xaml"));
        const string resource =
            "/LongBetterWindows.Host;component/Assets/app-icon.png";
        Assert.Contains(resource, mainWindow);
        Assert.True(
            toolCenter.Split(resource, StringSplitOptions.None).Length - 1 >= 2);
    }

    private static Dictionary<string, JsonElement> ReadJson(string path)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
               File.ReadAllText(path))
           ?? throw new InvalidDataException($"Invalid JSON: {path}");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
