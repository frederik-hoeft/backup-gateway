# Authentication and Authorization

## Security model

Backup Gateway uses ASP.NET Core Identity for durable service identities and roles. API authentication is machine-to-machine: callers exchange an Identity username and credential for a short-lived RS256 bearer JWT, then use that token for subsequent requests. There is no public registration flow.

Two coarse roles define capability boundaries:

- `administrator` identities provision backup clients, rotate their credentials, and manage target grants;
- `backup-client` identities may call target lifecycle APIs only when an explicit database-backed grant exists for the requested target.

A role never substitutes for a target grant. Target authorization is evaluated against PostgreSQL on every protected target request so revocation does not depend on JWT expiry.

## Token lifecycle

JWTs are signed with an RSA private key loaded from an externally mounted PEM file. The gateway validates issuer, audience, lifetime, signature, and the RS256 algorithm. Tokens contain the Identity user ID, username, roles, a unique token ID, and the Identity security stamp.

The security stamp is compared with current Identity state after JWT validation. Credential rotation therefore invalidates already-issued tokens immediately rather than waiting for their normal expiry. The configured token lifetime is intentionally bounded to at most one hour and defaults to 15 minutes.

Credentials and JWT values are response data only. Authentication failures are logged without usernames, credentials, or tokens.

## Initial administrator bootstrap

On startup the gateway ensures the required Identity roles exist, then examines the Identity user store:

- if the store is empty, `Auth:BootstrapAdministrator` supplies the initial administrator username and a path to a credential secret file;
- if users already exist, bootstrap credentials are ignored and cannot overwrite or recreate an administrator;
- if users exist but none has the administrator role, startup fails rather than silently creating a new privileged identity.

This makes possession of the deployment bootstrap secret useful only for the initial empty-database transition. The bootstrap credential must be at least 24 characters and is never persisted outside Identity's password hash.

## Client provisioning

Administrators create dedicated backup-client identities through the administration API. The server generates a 256-bit random credential and returns it only in the successful provisioning/rotation response. Identity stores only its password hash.

Target grants are ordinary authorization state and have an Identity foreign key, so deleting an Identity removes its grants. Durable leases deliberately do not have that foreign key: removing a compromised client must not cascade-delete held leases and accidentally make a target eligible for shutdown.

## Configuration

The authentication configuration keys are:

```json
{
  "Auth": {
    "Jwt": {
      "Issuer": "backup-gateway",
      "Audience": "backup-gateway-clients",
      "RsaPrivateKeyFile": "/run/secrets/backup-gateway-jwt.pem",
      "TokenLifetime": "00:15:00",
      "ClockSkew": "00:00:30"
    },
    "BootstrapAdministrator": {
      "Username": "admin",
      "CredentialFile": "/run/secrets/backup-gateway-bootstrap-admin"
    }
  }
}
```

Secret files are deployment inputs and must not be committed to the repository or copied into the container image.
