using System.ComponentModel.DataAnnotations;

namespace BackupGateway.Web.Api.V1.Models.Auth;

public sealed class TokenRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Username { get; init; }

    [Required]
    [StringLength(1024, MinimumLength = 1)]
    public required string Credential { get; init; }
}
