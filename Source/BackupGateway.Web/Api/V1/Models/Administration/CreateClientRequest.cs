using System.ComponentModel.DataAnnotations;

namespace BackupGateway.Web.Api.V1.Models.Administration;

public sealed class CreateClientRequest
{
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public required string Username { get; init; }
}
