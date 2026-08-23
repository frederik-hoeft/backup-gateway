# Deployment

Backup Gateway is deployed as one active ASP.NET Core container plus PostgreSQL. The application deliberately refuses to run concurrently against the same database: startup acquires a dedicated PostgreSQL advisory lock on a non-pooled connection, and a background monitor stops the process if that lock connection can no longer be verified.

## Network boundary

The gateway container serves plain HTTP on port 8080. The supplied Compose file publishes it only on `127.0.0.1`; a trusted host-local reverse proxy is expected to provide HTTPS, certificate management, and external exposure. Do not publish the gateway container directly to untrusted networks and do not terminate TLS on a network hop that can be observed or modified by untrusted hosts.

The reverse proxy must pass the `Authorization` header unchanged. Clients should use relative API paths from the documented base URL and must not rely on framework-generated absolute URLs or error text.

Required gateway egress is intentionally narrow:

- PostgreSQL TCP on the private Compose network;
- configured Wake-on-LAN UDP destinations/ports;
- configured TCP readiness endpoints;
- configured SSH shutdown endpoints;
- DNS only when configured target hosts are names rather than IP addresses.

Routed Wake-on-LAN may require router/firewall support such as a stable neighbor entry or directed-broadcast policy. That is infrastructure configuration, not a permission that backup clients need. Cyborg clients require HTTPS access to the gateway plus their normal direct backup-data path to the target; they do not need Wake-on-LAN or shutdown/SSH access.

## Container security

The gateway image runs the application as the .NET image's non-root `app` user. Compose additionally uses a read-only root filesystem, drops all Linux capabilities, enables `no-new-privileges`, bounds the PID count, and provides only a small non-executable `/tmp` tmpfs. `/tmp` is needed for the short-lived verified SSH known-hosts file.

The gateway does not use privileged mode, host networking, the Docker socket, or host filesystem mounts other than explicitly configured secret files. PostgreSQL is not published to the host network.

The runtime image includes `openssh-client` solely for pinned-host-key shutdown operations.

## Secrets

The deployment has four secret categories:

- PostgreSQL password;
- JWT RSA private signing key;
- one-time initial administrator credential file;
- one or more dedicated SSH private keys referenced by target definitions.

Do not store these in Git. Bind-mounted files must be readable by the container's non-root `app` user. The official .NET Linux image currently uses UID `1654`; verify the built image before provisioning host-side permissions rather than assuming that value forever. On Linux, a host ACL such as `setfacl -m u:1654:r <file>` can grant the container identity read access while preserving a restrictive owner-only mode. SSH additionally enforces private-key permissions, so target shutdown keys must not become group/world-readable merely to make a bind mount work.

The JWT key is read at gateway startup. Replacing it therefore requires a gateway restart; tokens signed with the previous key become invalid after restart. Client credentials are rotated through the administrator API, which updates the Identity security stamp so already-issued client JWTs no longer validate. SSH private-key contents are read by `ssh` on each invocation and can be replaced atomically without rebuilding the image. Changing a pinned SSH host fingerprint or any other target definition requires a gateway restart because the target catalog is immutable for one process lifetime. PostgreSQL credentials can be rotated in PostgreSQL and the deployment secret/environment, followed by a gateway restart; no application image rebuild is required for any of these rotations.

The bootstrap credential has authority only while the Identity database is empty. Once users exist, it cannot replace or recreate an administrator.

## Target configuration

Target definitions use the normal ASP.NET Core configuration hierarchy. A Compose override can provide one target like this:

```yaml
services:
  gateway:
    environment:
      Targets__backup-1__Host: 10.100.100.3
      Targets__backup-1__WakeOnLan__MacAddress: "02:11:22:33:44:55"
      Targets__backup-1__WakeOnLan__Destination: 10.100.100.255
      Targets__backup-1__WakeOnLan__Port: 9
      Targets__backup-1__Readiness__Port: 22
      Targets__backup-1__Shutdown__Port: 22
      Targets__backup-1__Shutdown__Username: backup-gateway
      Targets__backup-1__Shutdown__Command: sudo /sbin/shutdown -h now
      Targets__backup-1__Shutdown__PrivateKeyFile: /run/secrets/backup-1-shutdown-key
      Targets__backup-1__Shutdown__HostKeyFingerprint: SHA256:REPLACE_WITH_PINNED_FINGERPRINT
    volumes:
      - ./secrets/backup-1-shutdown-key:/run/secrets/backup-1-shutdown-key:ro
```

The command and all transport values are deployment configuration. Backup clients cannot override them through the API.

## PostgreSQL backup and restore

PostgreSQL contains Identity state, target grants, leases, runtime observations, and append-only audit history. Back it up as one logical unit. A typical Compose backup is:

```bash
docker compose exec -T postgres sh -c \
  'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom' \
  > backup-gateway.pgdump
```

For the simplest restore procedure, stop the gateway first so no lease/security state changes during recovery, recreate the application database, restore the archive, and then start the gateway again:

```bash
docker compose stop gateway
docker compose exec -T postgres sh -c \
  'dropdb --if-exists -U "$POSTGRES_USER" "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
docker compose exec -T postgres sh -c \
  'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --no-privileges' \
  < backup-gateway.pgdump
docker compose start gateway
```

The application runs EF Core migrations before serving requests, so restoring an older compatible backup is followed by normal forward migration on startup.

Restored held leases remain authoritative. This is intentional: preserving a possibly stale reservation and keeping/waking a target is safer than silently releasing a backup that may still be active. After a disaster recovery restore, inspect administrator target diagnostics and explicitly force-release only leases known to be inactive.

## Verification

`scripts/compose-smoke-test.sh` builds an isolated Compose project, starts a clean PostgreSQL volume, waits for readiness, checks liveness, Prometheus exposition, and the embedded OpenAPI contract, and authenticates with the bootstrapped administrator. It destroys its temporary volume and credentials on exit. The temporary smoke-test credentials are deliberately made readable to the non-root container user inside an unguessable temporary directory; production key files should use restrictive ownership/ACLs instead.

The normal test suite also contains PostgreSQL integration coverage. Set `BACKUP_GATEWAY_TEST_DATABASE` to a dedicated disposable database before running integration tests; those tests delete and recreate the configured database.
