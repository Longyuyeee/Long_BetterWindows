using LongBetterWindows.MarketplacePublisher;

try
{
    if (args.Length > 0 && string.Equals(args[0], "prepare", StringComparison.OrdinalIgnoreCase))
    {
        var preparation = await new MarketplaceReleasePreparationPipeline().PrepareAsync(
            ReleasePreparationArguments.Parse(args[1..]));
        Console.WriteLine($"Release prepared: {preparation.ReleaseId}");
        Console.WriteLine($"Packages: {preparation.PackageCount}");
        Console.WriteLine($"Publisher key: {preparation.PublisherKeyId} ({preparation.PublicKeyFingerprint})");
        Console.WriteLine("Mode: signed bundle verified; deployment dry-run only");
        return 0;
    }
    if (args.Length > 0 && string.Equals(
        args[0], "verify-preparation", StringComparison.OrdinalIgnoreCase))
    {
        var preparation = await new MarketplaceReleasePreparationValidator().ValidateAsync(
            ReleasePreparationValidationArguments.Parse(args[1..]));
        Console.WriteLine($"Release preparation verified: {preparation.ReleaseId}");
        Console.WriteLine($"Packages: {preparation.PackageCount}");
        return 0;
    }
    if (args.Length > 0 && string.Equals(args[0], "rollback", StringComparison.OrdinalIgnoreCase))
    {
        var rollback = await new MarketplaceRollbackPipeline().RollbackAsync(
            RollbackArguments.Parse(args[1..]));
        Console.WriteLine($"Registry rolled back from release: {rollback.ReleaseId}");
        Console.WriteLine($"Restored Registry SHA-256: {rollback.RestoredRegistrySha256}");
        return 0;
    }
    if (args.Length > 0 && string.Equals(args[0], "verify", StringComparison.OrdinalIgnoreCase))
    {
        var verification = await new MarketplaceVerificationPipeline().VerifyAsync(
            VerificationArguments.Parse(args[1..]));
        Console.WriteLine($"Registry verified: {verification.RegistryUri}");
        Console.WriteLine($"Entries: {verification.EntryCount}");
        Console.WriteLine($"Packages: {verification.PackageCount} ({verification.TotalPackageBytes} bytes)");
        Console.WriteLine($"Trusted publisher keys: {verification.TrustedPublisherKeyCount}");
        return 0;
    }
    if (args.Length > 0 && string.Equals(args[0], "verify-bundle", StringComparison.OrdinalIgnoreCase))
    {
        var verification = await new MarketplaceBundleVerificationPipeline().VerifyAsync(
            BundleVerificationArguments.Parse(args[1..]));
        Console.WriteLine($"Marketplace bundle verified: {verification.BundleDirectory}");
        Console.WriteLine($"Packages: {verification.PackageCount}");
        Console.WriteLine($"Publisher key: {verification.PublisherKeyId} ({verification.PublicKeyFingerprint})");
        return 0;
    }
    if (args.Length > 0 && string.Equals(args[0], "deploy", StringComparison.OrdinalIgnoreCase))
    {
        var deployment = await new MarketplaceDeploymentPipeline().DeployAsync(
            DeploymentArguments.Parse(args[1..]));
        Console.WriteLine($"Deployment release: {deployment.Plan.ReleaseId}");
        Console.WriteLine($"Files: {deployment.Plan.Files.Count}");
        Console.WriteLine(deployment.DryRun ? "Mode: dry-run" : "Mode: deployed");
        return 0;
    }
    var options = PublisherArguments.Parse(args);
    var result = await new MarketplacePublishingPipeline().PublishAsync(options);
    Console.WriteLine($"Registry published: {result.OutputDirectory}");
    Console.WriteLine($"Packages: {result.PackageCount}");
    Console.WriteLine($"Publisher key: {result.PublisherKeyId} ({result.PublicKeyFingerprint})");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Marketplace operation failed: {ex.Message}");
    return 1;
}
