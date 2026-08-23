using BackupGateway.Web.Services.Targets;
using Microsoft.Extensions.Configuration;

namespace BackupGateway.Web.Tests;

[TestClass]
public sealed class TargetCatalogTests
{
    private string _privateKeyFile = null!;

    [TestInitialize]
    public void Initialize()
    {
        _privateKeyFile = Path.Combine(Path.GetTempPath(), $"backup-gateway-target-test-{Guid.NewGuid():N}.key");
        File.WriteAllText(_privateKeyFile, "test-key-placeholder");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_privateKeyFile))
        {
            File.Delete(_privateKeyFile);
        }
    }

    [TestMethod]
    public void FromConfigurationCreatesStronglyTypedTarget()
    {
        using ConfigurationManager configuration = CreateConfiguration();
        TargetCatalog catalog = TargetCatalog.FromConfiguration(configuration);

        Assert.IsTrue(catalog.TryGet("backup-1", out TargetDefinition? target));
        Assert.IsNotNull(target);
        Assert.AreEqual("10.100.100.3", target.Host);
        Assert.AreEqual(9, target.WakeOnLan.Port);
        Assert.AreEqual(22, target.Readiness.Port);
        Assert.AreEqual("backup-gateway", target.Shutdown.Username);
    }

    [TestMethod]
    public void FromConfigurationRejectsUnsafeTargetIdentifier()
    {
        using ConfigurationManager configuration = CreateConfiguration("Backup-1");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TargetCatalog.FromConfiguration(configuration));

        StringAssert.Contains(exception.Message, "Targets:Backup-1");
    }

    [TestMethod]
    public void FromConfigurationRejectsMalformedMacAddress()
    {
        using ConfigurationManager configuration = CreateConfiguration();
        configuration["Targets:backup-1:WakeOnLan:MacAddress"] = "not-a-mac";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TargetCatalog.FromConfiguration(configuration));

        StringAssert.Contains(exception.Message, "WakeOnLan:MacAddress");
    }

    [TestMethod]
    public void FromConfigurationRequiresPinnedHostKeyFingerprint()
    {
        using ConfigurationManager configuration = CreateConfiguration();
        configuration["Targets:backup-1:Shutdown:HostKeyFingerprint"] = "disabled";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TargetCatalog.FromConfiguration(configuration));

        StringAssert.Contains(exception.Message, "Shutdown:HostKeyFingerprint");
    }

    [TestMethod]
    public void FromConfigurationRequiresExistingAbsolutePrivateKeyFile()
    {
        using ConfigurationManager configuration = CreateConfiguration();
        configuration["Targets:backup-1:Shutdown:PrivateKeyFile"] = "/definitely/missing/backup-gateway.key";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TargetCatalog.FromConfiguration(configuration));

        StringAssert.Contains(exception.Message, "Shutdown:PrivateKeyFile");
    }

    [TestMethod]
    public void FromConfigurationRejectsMultilineShutdownCommand()
    {
        using ConfigurationManager configuration = CreateConfiguration();
        configuration["Targets:backup-1:Shutdown:Command"] = "shutdown -h now\necho unexpected";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TargetCatalog.FromConfiguration(configuration));

        StringAssert.Contains(exception.Message, "Shutdown:Command");
    }

    private ConfigurationManager CreateConfiguration(string targetId = "backup-1")
    {
        ConfigurationManager configuration = new();
        configuration[$"Targets:{targetId}:Host"] = "10.100.100.3";
        configuration[$"Targets:{targetId}:WakeOnLan:MacAddress"] = "02:11:22:33:44:55";
        configuration[$"Targets:{targetId}:WakeOnLan:Destination"] = "10.100.100.255";
        configuration[$"Targets:{targetId}:WakeOnLan:Port"] = "9";
        configuration[$"Targets:{targetId}:Readiness:Port"] = "22";
        configuration[$"Targets:{targetId}:Shutdown:Port"] = "22";
        configuration[$"Targets:{targetId}:Shutdown:Username"] = "backup-gateway";
        configuration[$"Targets:{targetId}:Shutdown:Command"] = "sudo /sbin/shutdown -h now";
        configuration[$"Targets:{targetId}:Shutdown:PrivateKeyFile"] = _privateKeyFile;
        configuration[$"Targets:{targetId}:Shutdown:HostKeyFingerprint"] =
            $"SHA256:{Convert.ToBase64String(new byte[32]).TrimEnd('=')}";
        return configuration;
    }
}
