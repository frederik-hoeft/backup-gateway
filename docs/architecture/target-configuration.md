# Target Configuration

## Configuration boundary

Operational target definitions are deployment configuration, not mutable database entities. A running gateway instance loads the complete target catalog at startup, validates it, and exposes the resulting immutable definitions through `ITargetCatalog`. Lifecycle and authorization code resolve targets through that catalog instead of reading raw `IConfiguration` values.

The configured target identifier is the durable join point between deployment configuration and PostgreSQL state. IDs are case-sensitive lowercase identifiers containing ASCII letters, digits, `.`, `_`, and `-`; the first and last character must be alphanumeric. Reusing an ID therefore means referring to the same logical target across deployments.

## Target shape

Each target contains four fixed areas of configuration:

```json
{
  "Targets": {
    "backup-1": {
      "Host": "10.100.100.3",
      "WakeOnLan": {
        "MacAddress": "02:11:22:33:44:55",
        "Destination": "10.100.100.255",
        "Port": 9
      },
      "Readiness": {
        "Port": 22,
        "ConnectTimeout": "00:00:05",
        "RetryInterval": "00:00:05",
        "OverallTimeout": "00:05:00"
      },
      "Shutdown": {
        "Port": 22,
        "Username": "backup-gateway",
        "Command": "sudo /sbin/shutdown -h now",
        "PrivateKeyFile": "/run/secrets/backup-1-ssh-key",
        "HostKeyFingerprint": "SHA256:<base64-sha256-fingerprint>",
        "ConnectTimeout": "00:00:10",
        "CommandTimeout": "00:00:30",
        "OfflineTimeout": "00:05:00",
        "RetryInterval": "00:00:05"
      }
    }
  }
}
```

`Host` is the address used for readiness and SSH. Wake-on-LAN has its own IP destination because routed or directed-broadcast delivery is independent from the target's normal host address.

## Validation and secrets

Startup rejects malformed host names, IP addresses, MAC addresses, ports, timeouts, shutdown commands, private-key paths, and SSH host-key fingerprints. Private-key paths must be absolute and refer to existing files. The configuration contract has no option to disable SSH host-key verification: a valid OpenSSH `SHA256:` fingerprint is mandatory.

Private-key contents remain outside configuration and PostgreSQL. Deployment configuration contains only the path to the mounted secret file.

## Relationship to durable state

PostgreSQL may retain runtime observations or grants for a target ID that is temporarily absent from configuration. Startup creates runtime rows for newly configured IDs and reports orphaned persisted state, but it does not automatically delete grants, observations, leases, or audit history.

An unconfigured target is inactive regardless of retained database records. Administration cannot create new grants for it, and target authorization fails before consulting a retained grant. If the same stable target ID is intentionally restored to configuration later, its durable state becomes associated with that logical target again.
