namespace BackupGateway.Web.Api.V1.Models.Auth;

public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc);
