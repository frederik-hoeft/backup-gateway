using System.Collections.Frozen;
using System.Net;
using System.Net.NetworkInformation;

namespace BackupGateway.Web.Services.Targets;

internal sealed class TargetCatalog : ITargetCatalog
{
    private const string SectionName = "Targets";
    private readonly FrozenDictionary<string, TargetDefinition> _targets;

    private TargetCatalog(IEnumerable<TargetDefinition> targets)
    {
        TargetDefinition[] definitions = [.. targets];
        _targets = definitions.ToFrozenDictionary(target => target.Id, StringComparer.Ordinal);
        All = Array.AsReadOnly(definitions);
    }

    public IReadOnlyCollection<TargetDefinition> All { get; }

    public bool TryGet(string targetId, out TargetDefinition? target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        return _targets.TryGetValue(targetId, out target);
    }

    public static TargetCatalog FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        List<TargetDefinition> targets = [];
        foreach (IConfigurationSection targetSection in configuration.GetSection(SectionName).GetChildren())
        {
            string targetId = targetSection.Key;
            ValidateTargetId(targetId);
            TargetOptions options = targetSection.Get<TargetOptions>()
                ?? throw ConfigurationError(targetId, null, "target definition is empty");
            targets.Add(ValidateAndCreate(targetId, options));
        }
        return new TargetCatalog(targets);
    }

    private static TargetDefinition ValidateAndCreate(string targetId, TargetOptions options)
    {
        ValidateHost(targetId, options.Host);

        WakeOnLanOptions wake = options.WakeOnLan
            ?? throw ConfigurationError(targetId, "WakeOnLan", "section is required");
        PhysicalAddress macAddress = ParseMacAddress(targetId, wake.MacAddress);
        IPAddress destination = ParseIpAddress(targetId, "WakeOnLan:Destination", wake.Destination);
        ValidatePort(targetId, "WakeOnLan:Port", wake.Port);

        ReadinessOptions readiness = options.Readiness
            ?? throw ConfigurationError(targetId, "Readiness", "section is required");
        ValidatePort(targetId, "Readiness:Port", readiness.Port);
        ValidateDuration(targetId, "Readiness:ConnectTimeout", readiness.ConnectTimeout, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(30));
        ValidateDuration(targetId, "Readiness:RetryInterval", readiness.RetryInterval, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1));
        ValidateDuration(targetId, "Readiness:OverallTimeout", readiness.OverallTimeout, readiness.ConnectTimeout, TimeSpan.FromMinutes(30));

        ShutdownOptions shutdown = options.Shutdown
            ?? throw ConfigurationError(targetId, "Shutdown", "section is required");
        ValidatePort(targetId, "Shutdown:Port", shutdown.Port);
        ValidateBoundedText(targetId, "Shutdown:Username", shutdown.Username, 64, allowWhitespace: false);
        ValidateShutdownCommand(targetId, shutdown.Command);
        string privateKeyFile = ValidatePrivateKeyFile(targetId, shutdown.PrivateKeyFile);
        string hostKeyFingerprint = ValidateHostKeyFingerprint(targetId, shutdown.HostKeyFingerprint);
        ValidateDuration(targetId, "Shutdown:ConnectTimeout", shutdown.ConnectTimeout, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(60));
        ValidateDuration(targetId, "Shutdown:CommandTimeout", shutdown.CommandTimeout, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5));
        ValidateDuration(targetId, "Shutdown:OfflineTimeout", shutdown.OfflineTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(30));
        ValidateDuration(targetId, "Shutdown:RetryInterval", shutdown.RetryInterval, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(1));

        return new TargetDefinition(
            targetId,
            options.Host,
            new WakeOnLanDefinition(macAddress, destination, wake.Port),
            new ReadinessDefinition(readiness.Port, readiness.ConnectTimeout, readiness.RetryInterval, readiness.OverallTimeout),
            new ShutdownDefinition(
                shutdown.Port,
                shutdown.Username,
                shutdown.Command,
                privateKeyFile,
                hostKeyFingerprint,
                shutdown.ConnectTimeout,
                shutdown.CommandTimeout,
                shutdown.OfflineTimeout,
                shutdown.RetryInterval));
    }

    private static void ValidateTargetId(string targetId)
    {
        if (targetId.Length is < 1 or > 128
            || !IsAsciiLowerOrDigit(targetId[0])
            || !IsAsciiLowerOrDigit(targetId[^1])
            || targetId.Any(character => !IsAsciiLowerOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            throw ConfigurationError(targetId, null, "identifier must contain 1-128 lowercase ASCII letters, digits, '.', '_' or '-', with an alphanumeric first and last character");
        }
    }

    private static bool IsAsciiLowerOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static void ValidateHost(string targetId, string host)
    {
        if (string.IsNullOrWhiteSpace(host)
            || host.Length > 253
            || host.Any(char.IsWhiteSpace)
            || (IPAddress.TryParse(host, out _) is false && Uri.CheckHostName(host) != UriHostNameType.Dns))
        {
            throw ConfigurationError(targetId, "Host", "must be a valid IP address or DNS host name of at most 253 characters");
        }
    }

    private static PhysicalAddress ParseMacAddress(string targetId, string value)
    {
        if (!PhysicalAddress.TryParse(value, out PhysicalAddress? macAddress))
        {
            throw ConfigurationError(targetId, "WakeOnLan:MacAddress", "must be a valid MAC address");
        }

        byte[] bytes = macAddress.GetAddressBytes();
        if (bytes.Length != 6 || bytes.All(value => value == 0x00) || bytes.All(value => value == 0xff) || (bytes[0] & 0x01) != 0)
        {
            throw ConfigurationError(targetId, "WakeOnLan:MacAddress", "must be a six-byte unicast hardware address");
        }
        return macAddress;
    }

    private static IPAddress ParseIpAddress(string targetId, string field, string value)
    {
        if (!IPAddress.TryParse(value, out IPAddress? address))
        {
            throw ConfigurationError(targetId, field, "must be a valid IP address");
        }
        return address;
    }

    private static void ValidatePort(string targetId, string field, int port)
    {
        if (port is < 1 or > 65535)
        {
            throw ConfigurationError(targetId, field, "must be between 1 and 65535");
        }
    }

    private static void ValidateDuration(string targetId, string field, TimeSpan value, TimeSpan minimum, TimeSpan maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw ConfigurationError(targetId, field, $"must be between {minimum} and {maximum}");
        }
    }

    private static void ValidateBoundedText(string targetId, string field, string value, int maximumLength, bool allowWhitespace)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.Any(char.IsControl)
            || (!allowWhitespace && value.Any(char.IsWhiteSpace)))
        {
            throw ConfigurationError(targetId, field, $"must contain 1-{maximumLength} printable characters");
        }
    }

    private static void ValidateShutdownCommand(string targetId, string command)
    {
        ValidateBoundedText(targetId, "Shutdown:Command", command, 512, allowWhitespace: true);
        if (!string.Equals(command, command.Trim(), StringComparison.Ordinal))
        {
            throw ConfigurationError(targetId, "Shutdown:Command", "must not contain leading or trailing whitespace");
        }
    }

    private static string ValidatePrivateKeyFile(string targetId, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw ConfigurationError(targetId, "Shutdown:PrivateKeyFile", "must be an absolute path to a mounted private-key file");
        }
        FileInfo file = new(value);
        if (!file.Exists)
        {
            throw ConfigurationError(targetId, "Shutdown:PrivateKeyFile", "file does not exist");
        }
        return file.FullName;
    }

    private static string ValidateHostKeyFingerprint(string targetId, string value)
    {
        const string prefix = "SHA256:";
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw ConfigurationError(targetId, "Shutdown:HostKeyFingerprint", "must be an OpenSSH SHA256 fingerprint");
        }

        string encoded = value[prefix.Length..];
        if (encoded.Length != 43 || encoded.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '+' or '/')))
        {
            throw ConfigurationError(targetId, "Shutdown:HostKeyFingerprint", "must contain a 32-byte SHA256 digest without Base64 padding");
        }
        try
        {
            byte[] digest = Convert.FromBase64String(encoded + "=");
            if (digest.Length != 32)
            {
                throw ConfigurationError(targetId, "Shutdown:HostKeyFingerprint", "must contain a 32-byte SHA256 digest");
            }
        }
        catch (FormatException exception)
        {
            throw ConfigurationError(targetId, "Shutdown:HostKeyFingerprint", "contains invalid Base64", exception);
        }
        return value;
    }

    private static InvalidOperationException ConfigurationError(
        string targetId,
        string? field,
        string message,
        Exception? innerException = null)
    {
        string path = field is null ? $"{SectionName}:{targetId}" : $"{SectionName}:{targetId}:{field}";
        return new InvalidOperationException($"Invalid target configuration at '{path}': {message}.", innerException);
    }

    private sealed class TargetOptions
    {
        public string Host { get; init; } = string.Empty;

        public WakeOnLanOptions? WakeOnLan { get; init; }

        public ReadinessOptions? Readiness { get; init; }

        public ShutdownOptions? Shutdown { get; init; }
    }

    private sealed class WakeOnLanOptions
    {
        public string MacAddress { get; init; } = string.Empty;

        public string Destination { get; init; } = string.Empty;

        public int Port { get; init; } = 9;
    }

    private sealed class ReadinessOptions
    {
        public int Port { get; init; } = 22;

        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

        public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(5);

        public TimeSpan OverallTimeout { get; init; } = TimeSpan.FromMinutes(5);
    }

    private sealed class ShutdownOptions
    {
        public int Port { get; init; } = 22;

        public string Username { get; init; } = string.Empty;

        public string Command { get; init; } = string.Empty;

        public string PrivateKeyFile { get; init; } = string.Empty;

        public string HostKeyFingerprint { get; init; } = string.Empty;

        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

        public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(30);

        public TimeSpan OfflineTimeout { get; init; } = TimeSpan.FromMinutes(5);

        public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(5);
    }
}
