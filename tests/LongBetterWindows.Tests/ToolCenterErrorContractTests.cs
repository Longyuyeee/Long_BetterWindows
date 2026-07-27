using System.IO;
using System.Text.Json;
using LongBetterWindows.Host.Interaction;
using LongBetterWindows.Host.Services;

namespace LongBetterWindows.Tests;

public sealed class ToolCenterErrorContractTests
{
    [Fact]
    public void SparsePackageErrorCodes_HaveStablePublishedValues()
    {
        Assert.Equal(0, (int)SparsePackageErrorCode.None);
        Assert.Equal(6000, (int)SparsePackageErrorCode.ScriptMissing);
        Assert.Equal(6001, (int)SparsePackageErrorCode.ProcessFailed);
        Assert.Equal(6002, (int)SparsePackageErrorCode.InvalidState);
        Assert.Equal(6003, (int)SparsePackageErrorCode.TimedOut);
        Assert.Equal(6004, (int)SparsePackageErrorCode.Cancelled);
        Assert.Equal(6005, (int)SparsePackageErrorCode.UnexpectedFailure);
    }

    [Fact]
    public void ToolCenterFailures_HaveBilingualPresentationKeys()
    {
        var repository = FindRepositoryRoot();
        using var chinese = ReadResources(repository, "zh-CN.json");
        using var english = ReadResources(repository, "en-US.json");

        foreach (var code in Enum.GetValues<SparsePackageErrorCode>()
                     .Where(code => code != SparsePackageErrorCode.None))
        {
            var key = SparsePackagePresentation.GetErrorResourceKey(code);
            Assert.StartsWith("system.sparse.error.", key);
            Assert.True(chinese.RootElement.TryGetProperty(key, out _), key);
            Assert.True(english.RootElement.TryGetProperty(key, out _), key);
        }

        foreach (var key in new[]
                 {
                     "system.column.error.enable",
                     "system.column.error.disable",
                     "system.column.error.unexpected",
                     "system.legacy.error.register",
                     "system.legacy.error.unregister",
                     "system.legacy.error.unexpected",
                 })
        {
            Assert.True(chinese.RootElement.TryGetProperty(key, out _), key);
            Assert.True(english.RootElement.TryGetProperty(key, out _), key);
        }
    }

    [Fact]
    public void ToolCenter_DoesNotDisplayTechnicalFailureMessages()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "LongBetterWindows.Host",
            "Views",
            "SystemIntegrationPageControl.xaml.cs"));

        Assert.DoesNotContain(
            "StatusText.Text = \"操作异常: \" + ex.Message",
            source);
        Assert.DoesNotContain(
            "StatusText.Text = \"移除失败: \" + (result.ErrorMessage",
            source);
        Assert.DoesNotContain(
            "StatusText.Text = \"注册失败: \" + (result.ErrorMessage",
            source);
        Assert.DoesNotContain(
            "StatusText.Text = \"注入失败: \" + (result.ErrorMessage",
            source);
        Assert.DoesNotContain(
            "I18n(\"status.operationFailed\"),\n                    result.Message",
            source);
        Assert.DoesNotContain(
            "I18n(\"status.unavailable\"),\n                    result.Message",
            source);
    }

    private static JsonDocument ReadResources(
        string repository,
        string fileName)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository,
            "src",
            "LongBetterWindows.Host",
            "i18n",
            fileName)));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
