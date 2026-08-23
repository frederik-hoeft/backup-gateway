using Microsoft.AspNetCore.Identity;

namespace BackupGateway.Web.Services.Auth;

public interface IJwtTokenService
{
    AccessToken Issue(IdentityUser<Guid> user, IEnumerable<string> roles);
}
