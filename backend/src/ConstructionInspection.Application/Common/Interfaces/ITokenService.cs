using ConstructionInspection.Application.Common.Models;

namespace ConstructionInspection.Application.Common.Interfaces;

public interface ITokenService
{
    IssuedToken CreateToken(UserProfile user);
}
