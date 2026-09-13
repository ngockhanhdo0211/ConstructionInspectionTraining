using System.IdentityModel.Tokens.Jwt;
using ConstructionInspection.Application.Common.Interfaces;

namespace ConstructionInspection.Api.Authentication;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }
}
