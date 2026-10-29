using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Ecomerce_dotnet.Models;

namespace Ecomerce_dotnet.Services
{
    public interface IJwtTokenService
    {
        Task<string> GenerateAccessTokenAsync(User user);
        Task<List<string>> GetUserPermissionsAsync(Guid userId);
        Task<List<string>> GetUserRolesAsync(User user);
        string GenerateRefreshToken();
        ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
    }
}
