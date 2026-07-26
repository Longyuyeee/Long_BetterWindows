using System.Net;
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("1.10.0-rc.2", "1.10.0-rc.3", -1)]
    [InlineData("1.10.0-rc.10", "1.10.0", -1)]
    [InlineData("1.10.0", "1.10.1-alpha.1", -1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    public void SemanticVersion_UsesPrereleasePrecedence(
        string left,
        string right,
        int expectedSign)
    {
        var comparison = SemanticVersion.Parse(left)
            .CompareTo(SemanticVersion.Parse(right));
        Assert.Equal(expectedSign, Math.Sign(comparison));
    }

    [Fact]
    public async Task CheckAndDownload_RequireSignedManifestAndMatchingPackage()
    {
        const string tag = "v1.10.0-rc.3";
        const string packageName =
            "LongBetterWindows-v1.10.0-rc.3-win-x64-self-contained.zip";
        var packageBytes = Encoding.UTF8.GetBytes("verified update package");
        var packageHash = Convert.ToHexString(SHA256.HashData(packageBytes))
            .ToLowerInvariant();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1,
            product = "Long Assistant",
            version = "1.10.0-rc.3",
            channel = "prerelease",
            published_at = "2026-07-26T00:00:00Z",
            source_commit = new string('a', 40),
            release_page =
                $"https://github.com/Longyuyeee/Long_BetterWindows/releases/tag/{tag}",
            packages = new[]
            {
                new
                {
                    kind = "self-contained",
                    file = packageName,
                    url =
                        $"https://github.com/Longyuyeee/Long_BetterWindows/releases/download/{tag}/{packageName}",
                    sha256 = packageHash,
                    bytes = packageBytes.Length,
                },
            },
        });

        using var rsa = RSA.Create(2048);
        var signature = Convert.ToBase64String(rsa.SignData(
            manifest,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
        var releases = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new
            {
                tag_name = tag,
                draft = false,
                prerelease = true,
                assets = new[]
                {
                    new
                    {
                        name = UpdateService.ManifestAssetName,
                        browser_download_url =
                            $"https://github.com/Longyuyeee/Long_BetterWindows/releases/download/{tag}/{UpdateService.ManifestAssetName}",
                    },
                    new
                    {
                        name = UpdateService.SignatureAssetName,
                        browser_download_url =
                            $"https://github.com/Longyuyeee/Long_BetterWindows/releases/download/{tag}/{UpdateService.SignatureAssetName}",
                    },
                },
            },
        });
        using var client = new HttpClient(new RouteHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "api.github.com")
                return Bytes(releases, "application/json");
            if (path.EndsWith(UpdateService.ManifestAssetName, StringComparison.Ordinal))
                return Bytes(manifest, "application/json");
            if (path.EndsWith(UpdateService.SignatureAssetName, StringComparison.Ordinal))
                return Bytes(Encoding.UTF8.GetBytes(signature), "text/plain");
            if (path.EndsWith(packageName, StringComparison.Ordinal))
                return Bytes(packageBytes, "application/zip");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        using var service = new UpdateService(
            "1.10.0-rc.2",
            rsa.ToXmlString(false),
            client);

        var result = await service.CheckAsync(includePrereleases: true);
        Assert.Equal(UpdateCheckState.Available, result.State);
        Assert.Equal("1.10.0-rc.3", result.AvailableVersion);
        Assert.NotNull(result.Package);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "LongBetterWindows.Tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var path = await service.DownloadAsync(result.Package!, directory);
            Assert.Equal(packageBytes, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(path + ".part"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CheckAsync_RejectsTamperedManifest()
    {
        using var rsa = RSA.Create(2048);
        var releaseBytes = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new
            {
                tag_name = "v1.10.0",
                draft = false,
                prerelease = false,
                assets = new[]
                {
                    new
                    {
                        name = UpdateService.ManifestAssetName,
                        browser_download_url =
                            $"https://github.com/Longyuyeee/Long_BetterWindows/releases/download/v1.10.0/{UpdateService.ManifestAssetName}",
                    },
                    new
                    {
                        name = UpdateService.SignatureAssetName,
                        browser_download_url =
                            $"https://github.com/Longyuyeee/Long_BetterWindows/releases/download/v1.10.0/{UpdateService.SignatureAssetName}",
                    },
                },
            },
        });
        using var client = new HttpClient(new RouteHandler(request =>
            request.RequestUri!.Host == "api.github.com"
                ? Bytes(releaseBytes, "application/json")
                : request.RequestUri.AbsolutePath.EndsWith(
                    UpdateService.ManifestAssetName,
                    StringComparison.Ordinal)
                    ? Bytes(Encoding.UTF8.GetBytes("{}"), "application/json")
                    : Bytes(Encoding.UTF8.GetBytes(
                        Convert.ToBase64String(new byte[256])),
                        "text/plain")));
        using var service = new UpdateService(
            "1.9.0",
            rsa.ToXmlString(false),
            client);

        await Assert.ThrowsAsync<CryptographicException>(
            () => service.CheckAsync(includePrereleases: false));
    }

    private static HttpResponseMessage Bytes(byte[] content, string mediaType)
        => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
            {
                Headers = { ContentType = new(mediaType) },
            },
        };

    private sealed class RouteHandler(
        Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(route(request));
    }
}
