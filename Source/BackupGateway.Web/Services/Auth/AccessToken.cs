namespace BackupGateway.Web.Services.Auth;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);
