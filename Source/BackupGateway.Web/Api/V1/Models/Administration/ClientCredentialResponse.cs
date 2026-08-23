namespace BackupGateway.Web.Api.V1.Models.Administration;

public sealed record ClientCredentialResponse(Guid ClientId, string Username, string Credential);
