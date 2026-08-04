using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongBetterWindows.MarketplacePublisher;

namespace LongBetterWindows.Tests;

public sealed class MarketplaceDeploymentTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"long-market-deploy-{Guid.NewGuid():N}");

    public MarketplaceDeploymentTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task CreatePlanAsync_OrdersRegistryLastAndExcludesTrustFragment()
    {
        var bundle = await CreateBundleAsync();

        var plan = await MarketplaceDeploymentPipeline.CreatePlanAsync(bundle);

        Assert.NotEmpty(plan.ReleaseId);
        Assert.Equal(MarketplaceDeploymentFileKind.RegistryCommit, plan.Files[^1].Kind);
        Assert.Equal("registry.json", plan.Files[^1].RemotePath);
        Assert.Equal(MarketplaceDeploymentFileKind.ImmutablePackage, plan.Files[0].Kind);
        Assert.DoesNotContain(plan.Files, x => x.RemotePath.Contains("trusted-publisher"));
        Assert.Contains(plan.Files, x => x.Kind == MarketplaceDeploymentFileKind.AuditReport);
    }

    [Fact]
    public async Task CreatePlanAsync_TamperedPackageIsRejectedBeforeDeployment()
    {
        var bundle = await CreateBundleAsync();
        var package = Assert.Single(Directory.GetFiles(Path.Combine(bundle, "packages"), "*.lpak"));
        await File.AppendAllTextAsync(package, "tampered");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            MarketplaceDeploymentPipeline.CreatePlanAsync(bundle));

        Assert.Contains("modified after signing", error.Message);
    }

    [Fact]
    public async Task DryRun_WritesMachineReadableReportWithoutChangingTarget()
    {
        var bundle = await CreateBundleAsync();
        var target = Path.Combine(_tempDir, "dry-run-target");
        var reportPath = Path.Combine(_tempDir, "evidence", "deployment.json");

        var result = await new MarketplaceDeploymentPipeline().DeployAsync(new MarketplaceDeploymentOptions
        {
            BundleDirectory = bundle,
            TargetKind = MarketplaceDeploymentTargetKind.LocalDirectory,
            LocalTargetDirectory = target,
            DryRun = true,
            ResultPath = reportPath,
        });

        Assert.True(result.DryRun);
        Assert.False(Directory.Exists(target));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        Assert.Equal("dry_run", report.RootElement.GetProperty("Mode").GetString());
        Assert.Equal(result.Plan.ReleaseId, report.RootElement.GetProperty("ReleaseId").GetString());
        Assert.True(report.RootElement.GetProperty("Files").GetArrayLength() > 0);
        Assert.DoesNotContain("credential", await File.ReadAllTextAsync(reportPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DryRun_RejectsTargetThatRealHttpsDeploymentWouldReject()
    {
        var bundle = await CreateBundleAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            new MarketplaceDeploymentPipeline().DeployAsync(
                new MarketplaceDeploymentOptions
                {
                    BundleDirectory = bundle,
                    TargetKind = MarketplaceDeploymentTargetKind.Https,
                    RemoteBaseUri = new Uri("http://market.example/"),
                    DryRun = true,
                }));

        Assert.Contains("HTTPS deployment base URI", error.Message);
    }

    [Fact]
    public async Task LocalDeployment_RequiresForceThenAtomicallyReplacesTarget()
    {
        var bundle = await CreateBundleAsync();
        var target = Path.Combine(_tempDir, "staging-target");
        Directory.CreateDirectory(target);
        var sentinel = Path.Combine(target, "old.txt");
        await File.WriteAllTextAsync(sentinel, "old");
        var pipeline = new MarketplaceDeploymentPipeline();
        var options = new MarketplaceDeploymentOptions
        {
            BundleDirectory = bundle,
            TargetKind = MarketplaceDeploymentTargetKind.LocalDirectory,
            LocalTargetDirectory = target,
        };

        await Assert.ThrowsAsync<IOException>(() => pipeline.DeployAsync(options));
        Assert.Equal("old", await File.ReadAllTextAsync(sentinel));

        var forced = new MarketplaceDeploymentOptions
        {
            BundleDirectory = bundle,
            TargetKind = MarketplaceDeploymentTargetKind.LocalDirectory,
            LocalTargetDirectory = target,
            Force = true,
        };
        var result = await pipeline.DeployAsync(forced);

        Assert.False(result.DryRun);
        Assert.False(File.Exists(sentinel));
        Assert.True(File.Exists(Path.Combine(target, "registry.json")));
        Assert.True(File.Exists(Path.Combine(target, "deployment-release.txt")));
        Assert.True(File.Exists(Path.Combine(
            target, "releases", result.Plan.ReleaseId, "deployment-manifest.json")));
        Assert.False(File.Exists(Path.Combine(target, "trusted-publisher.fragment.json")));
        Assert.Empty(Directory.GetDirectories(_tempDir, ".market-deploy-*"));
    }

    [Fact]
    public async Task HttpsDeployment_UsesBearerAndCommitsRegistryLast()
    {
        var bundle = await CreateBundleAsync();
        var plan = await MarketplaceDeploymentPipeline.CreatePlanAsync(bundle);
        var handler = new InMemoryDeploymentHandler("secret-token");
        using var client = new HttpClient(handler);
        using var target = new HttpMarketplaceDeploymentTarget(
            client, new Uri("https://market.example/"), "secret-token");

        await target.DeployAsync(plan);

        var puts = handler.Operations.Where(x => x.StartsWith("PUT ")).ToArray();
        Assert.Equal(plan.Files.Count + 1, puts.Length);
        Assert.Equal("PUT /registry.json", puts[^1]);
        Assert.All(plan.Files, file => Assert.True(handler.Storage.ContainsKey('/' + file.RemotePath)));
        Assert.True(handler.Storage.ContainsKey($"/releases/{plan.ReleaseId}/deployment-manifest.json"));
        Assert.DoesNotContain(handler.Operations, x => x.Contains("secret-token"));
    }

    [Fact]
    public async Task HttpsDeployment_RegistryVerificationFailureRestoresPreviousRegistry()
    {
        var bundle = await CreateBundleAsync();
        var plan = await MarketplaceDeploymentPipeline.CreatePlanAsync(bundle);
        var previous = Encoding.UTF8.GetBytes("previous-registry");
        var handler = new InMemoryDeploymentHandler("secret-token")
        {
            CorruptFirstCommittedRegistryRead = true,
        };
        handler.Storage["/registry.json"] = previous;
        using var client = new HttpClient(handler);
        using var target = new HttpMarketplaceDeploymentTarget(
            client, new Uri("https://market.example/"), "secret-token");

        await Assert.ThrowsAsync<InvalidDataException>(() => target.DeployAsync(plan));

        Assert.Equal(previous, handler.Storage["/registry.json"]);
        Assert.Equal(2, handler.Operations.Count(x => x == "PUT /registry.json"));
    }

    [Fact]
    public async Task LocalRollback_RequiresExactConfirmationAndRestoresPreviousRegistry()
    {
        var firstBundle = await CreateBundleAsync("1.0.0");
        var secondBundle = await CreateBundleAsync("2.0.0");
        var target = Path.Combine(_tempDir, "rollback-local");
        var pipeline = new MarketplaceDeploymentPipeline();
        await pipeline.DeployAsync(new MarketplaceDeploymentOptions
        {
            BundleDirectory = firstBundle,
            TargetKind = MarketplaceDeploymentTargetKind.LocalDirectory,
            LocalTargetDirectory = target,
        });
        var previous = await File.ReadAllBytesAsync(Path.Combine(target, "registry.json"));
        var previousPackages = Directory.GetFiles(Path.Combine(target, "packages"))
            .Select(Path.GetFileName).ToArray();
        var second = await pipeline.DeployAsync(new MarketplaceDeploymentOptions
        {
            BundleDirectory = secondBundle,
            TargetKind = MarketplaceDeploymentTargetKind.LocalDirectory,
            LocalTargetDirectory = target,
            Force = true,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MarketplaceRollbackPipeline().RollbackAsync(new MarketplaceRollbackOptions
            {
                TargetKind = MarketplaceDeploymentTargetKind.LocalDirectory,
                LocalTargetDirectory = target,
                ReleaseId = second.Plan.ReleaseId,
                ConfirmReleaseId = "wrong-release",
            }));

        var rollback = await new MarketplaceRollbackPipeline().RollbackAsync(new MarketplaceRollbackOptions
        {
            TargetKind = MarketplaceDeploymentTargetKind.LocalDirectory,
            LocalTargetDirectory = target,
            ReleaseId = second.Plan.ReleaseId,
            ConfirmReleaseId = second.Plan.ReleaseId,
        });

        Assert.Equal(previous, await File.ReadAllBytesAsync(Path.Combine(target, "registry.json")));
        Assert.Equal(second.Plan.ReleaseId, rollback.ReleaseId);
        Assert.All(previousPackages, package =>
            Assert.True(File.Exists(Path.Combine(target, "packages", package!))));
        using var restoredRegistry = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(target, "registry.json")));
        var restoredPackageName = Path.GetFileName(restoredRegistry.RootElement
            .GetProperty("Entries")[0].GetProperty("Versions")[0]
            .GetProperty("PackageUri").GetString());
        Assert.True(File.Exists(Path.Combine(target, "packages", restoredPackageName!)));
    }

    [Fact]
    public async Task LocalRollback_TamperedSnapshotIsRejectedWithoutChangingRegistry()
    {
        var target = Path.Combine(_tempDir, "rollback-tamper");
        var pipeline = new MarketplaceDeploymentPipeline();
        await pipeline.DeployAsync(new MarketplaceDeploymentOptions
        {
            BundleDirectory = await CreateBundleAsync("1.0.0"),
            TargetKind = MarketplaceDeploymentTargetKind.LocalDirectory,
            LocalTargetDirectory = target,
        });
        var second = await pipeline.DeployAsync(new MarketplaceDeploymentOptions
        {
            BundleDirectory = await CreateBundleAsync("2.0.0"),
            TargetKind = MarketplaceDeploymentTargetKind.LocalDirectory,
            LocalTargetDirectory = target,
            Force = true,
        });
        var current = await File.ReadAllBytesAsync(Path.Combine(target, "registry.json"));
        var snapshot = Path.Combine(target, "releases", second.Plan.ReleaseId, "previous-registry.json");
        await File.AppendAllTextAsync(snapshot, "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MarketplaceRollbackPipeline().RollbackAsync(new MarketplaceRollbackOptions
            {
                TargetKind = MarketplaceDeploymentTargetKind.LocalDirectory,
                LocalTargetDirectory = target,
                ReleaseId = second.Plan.ReleaseId,
                ConfirmReleaseId = second.Plan.ReleaseId,
            }));

        Assert.Equal(current, await File.ReadAllBytesAsync(Path.Combine(target, "registry.json")));
    }

    [Fact]
    public async Task HttpsRollback_RestoresPersistedPreviousRegistrySnapshot()
    {
        var firstPlan = await MarketplaceDeploymentPipeline.CreatePlanAsync(await CreateBundleAsync("1.0.0"));
        var secondPlan = await MarketplaceDeploymentPipeline.CreatePlanAsync(await CreateBundleAsync("2.0.0"));
        var handler = new InMemoryDeploymentHandler("secret-token");
        using var client = new HttpClient(handler);
        using (var deployment = new HttpMarketplaceDeploymentTarget(
            client, new Uri("https://market.example/"), "secret-token"))
        {
            await deployment.DeployAsync(firstPlan);
            var previous = handler.Storage["/registry.json"].ToArray();
            await deployment.DeployAsync(secondPlan);
            Assert.True(handler.Storage.ContainsKey(
                $"/releases/{secondPlan.ReleaseId}/previous-registry.json"));

            using var rollbackTarget = new HttpMarketplaceRollbackTarget(
                client, new Uri("https://market.example/"), "secret-token");
            await new MarketplaceRollbackPipeline().RollbackAsync(new MarketplaceRollbackOptions
            {
                TargetKind = MarketplaceDeploymentTargetKind.Https,
                ReleaseId = secondPlan.ReleaseId,
                ConfirmReleaseId = secondPlan.ReleaseId,
            }, rollbackTarget);

            Assert.Equal(previous, handler.Storage["/registry.json"]);
        }
    }

    private async Task<string> CreateBundleAsync(string version = "1.0.0")
    {
        var packages = Path.Combine(_tempDir, $"source-packages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(packages);
        var packageFile = Path.Combine(packages, "deploy-test.lpak");
        using (var archive = ZipFile.Open(packageFile, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new
            {
                id = "dev.long.deploy-test",
                version,
                name = "Deploy Test",
                author = "Long",
                runtime = "webview",
                entry_point = "index.html",
                capabilities = Array.Empty<string>(),
                min_host_version = "0.5.0",
                min_api_version = "1.0.0",
                min_ui_kit_version = "1.0.0",
            }));
            WriteEntry(archive, "index.html", "<!doctype html><title>deploy</title>");
        }
        using var rsa = RSA.Create(2048);
        var key = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.private.pem");
        await File.WriteAllTextAsync(key, rsa.ExportPkcs8PrivateKeyPem());
        var source = Path.Combine(_tempDir, $"deploy-source-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(source, JsonSerializer.Serialize(new MarketplacePublishSource
        {
            Entries = new[]
            {
                new MarketplacePublishEntry
                {
                    Id = "dev.long.deploy-test",
                    Name = "Deploy Test",
                    Summary = "Deployment fixture",
                    Category = "Tests",
                    Versions = new[]
                    {
                        new MarketplacePublishVersion
                        {
                            Version = version,
                            PackageFile = "deploy-test.lpak",
                        },
                    },
                },
            },
        }));
        var output = Path.Combine(_tempDir, $"bundle-{Guid.NewGuid():N}");
        await new MarketplacePublishingPipeline().PublishAsync(new MarketplacePublishOptions
        {
            SourceCatalogPath = source,
            PackagesDirectory = packages,
            OutputDirectory = output,
            PrivateKeyPath = key,
            PublisherKeyId = "deploy-test-key",
            PublisherName = "Long Deploy Tests",
            BasePackageUri = new Uri("https://market.example/packages/"),
        });
        return output;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class InMemoryDeploymentHandler : HttpMessageHandler
    {
        private readonly string _expectedToken;
        public Dictionary<string, byte[]> Storage { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Operations { get; } = new();
        public bool CorruptFirstCommittedRegistryRead { get; init; }
        private int _registryPutCount;
        public InMemoryDeploymentHandler(string expectedToken) => _expectedToken = expectedToken;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(_expectedToken, request.Headers.Authorization?.Parameter);
            var path = request.RequestUri!.AbsolutePath;
            Operations.Add($"{request.Method.Method} {path}");
            if (request.Method == HttpMethod.Put)
            {
                Storage[path] = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                if (path == "/registry.json") _registryPutCount++;
                return new HttpResponseMessage(HttpStatusCode.NoContent) { RequestMessage = request };
            }
            if (request.Method == HttpMethod.Get && Storage.TryGetValue(path, out var bytes))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new ByteArrayContent(
                        path == "/registry.json" && CorruptFirstCommittedRegistryRead && _registryPutCount == 1
                            ? Encoding.UTF8.GetBytes("corrupted-read")
                            : bytes),
                };
            if (request.Method == HttpMethod.Delete)
            {
                Storage.Remove(path);
                return new HttpResponseMessage(HttpStatusCode.NoContent) { RequestMessage = request };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
