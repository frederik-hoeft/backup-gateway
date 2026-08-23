using BackupGateway.Web.Services.Targets;
using System.Security.Cryptography;

namespace BackupGateway.Web.Services.Lifecycle.Transports;

internal sealed class SshShutdownTransport(IExternalProcessRunner processRunner) : ITargetShutdownTransport
{
    public async Task RequestShutdownAsync(TargetDefinition target, CancellationToken cancellationToken)
    {
        string knownHost = await ScanAndVerifyHostKeyAsync(target, cancellationToken);
        string knownHostsFile = Path.Combine(Path.GetTempPath(), $"backup-gateway-known-hosts-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(knownHostsFile, knownHost + Environment.NewLine, cancellationToken);
            ExternalProcessInvocation invocation = new(
                "ssh",
                CreateSshArguments(target, knownHostsFile),
                target.Shutdown.CommandTimeout);
            ExternalProcessResult result = await processRunner.RunAsync(invocation, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new TargetLifecycleTransportException(
                    "ssh-command-failed",
                    $"SSH shutdown command exited with code {result.ExitCode}.");
            }
        }
        finally
        {
            File.Delete(knownHostsFile);
        }
    }

    private async Task<string> ScanAndVerifyHostKeyAsync(TargetDefinition target, CancellationToken cancellationToken)
    {
        ExternalProcessInvocation invocation = new(
            "ssh-keyscan",
            [
                "-T", ToWholeSeconds(target.Shutdown.ConnectTimeout),
                "-p", target.Shutdown.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--", target.Host,
            ],
            target.Shutdown.ConnectTimeout + TimeSpan.FromSeconds(1));
        ExternalProcessResult result;
        try
        {
            result = await processRunner.RunAsync(invocation, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TargetLifecycleTransportException("ssh-host-key-scan-timeout", "SSH host-key scan timed out.");
        }

        if (result.ExitCode != 0)
        {
            throw new TargetLifecycleTransportException(
                "ssh-host-key-scan-failed",
                $"SSH host-key scan exited with code {result.ExitCode}.");
        }

        foreach (string line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryGetFingerprint(line, out string? fingerprint)
                && string.Equals(fingerprint, target.Shutdown.HostKeyFingerprint, StringComparison.Ordinal))
            {
                return line;
            }
        }

        throw new TargetLifecycleTransportException(
            "ssh-host-key-mismatch",
            "The target did not present the configured SSH host key.");
    }

    internal static bool TryGetFingerprint(string knownHostsLine, out string? fingerprint)
    {
        fingerprint = null;
        if (string.IsNullOrWhiteSpace(knownHostsLine) || knownHostsLine[0] == '#')
        {
            return false;
        }

        string[] fields = knownHostsLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 3)
        {
            return false;
        }

        try
        {
            byte[] key = Convert.FromBase64String(fields[2]);
            byte[] digest = SHA256.HashData(key);
            fingerprint = $"SHA256:{Convert.ToBase64String(digest).TrimEnd('=')}";
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> CreateSshArguments(TargetDefinition target, string knownHostsFile) =>
    [
        "-o", "BatchMode=yes",
        "-o", "IdentitiesOnly=yes",
        "-o", "StrictHostKeyChecking=yes",
        "-o", $"UserKnownHostsFile={knownHostsFile}",
        "-o", "GlobalKnownHostsFile=/dev/null",
        "-o", "PasswordAuthentication=no",
        "-o", "KbdInteractiveAuthentication=no",
        "-o", "PreferredAuthentications=publickey",
        "-o", $"ConnectTimeout={ToWholeSeconds(target.Shutdown.ConnectTimeout)}",
        "-o", "ConnectionAttempts=1",
        "-i", target.Shutdown.PrivateKeyFile,
        "-p", target.Shutdown.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--",
        $"{target.Shutdown.Username}@{target.Host}",
        target.Shutdown.Command,
    ];

    private static string ToWholeSeconds(TimeSpan value) =>
        Math.Max(1, (int)Math.Ceiling(value.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
