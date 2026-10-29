using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ecomerce_dotnet.Filters
{
    /// <summary>
    /// Custom dynamic authorization filter that checks whether the authenticated user's JWT claims
    /// contain the required permission string.
    /// Returns 401 Unauthorized if unauthenticated, and 403 Forbidden if permission is missing.
    /// </summary>
    public class HasPermissionAuthorizationFilter : IAuthorizationFilter
    {
        private readonly string _permission;

        public HasPermissionAuthorizationFilter(string permission)
        {
            _permission = permission ?? throw new ArgumentNullException(nameof(permission));
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            // 1. Check if the user is authenticated
            if (user.Identity == null || !user.Identity.IsAuthenticated)
            {
                context.Result = new UnauthorizedObjectResult(new
                {
                    statusCode = StatusCodes.Status401Unauthorized,
                    message = "Unauthorized: You must be authenticated with a valid token to access this resource."
                });
                return;
            }

            // 2. Check if user has the specific required permission claim
            // (Note: Users with the 'Admin' role can also be granted access as superusers, while checking direct permission claims)
            var hasPermissionClaim = user.Claims.Any(c =>
                string.Equals(c.Type, "permission", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Value, _permission, StringComparison.OrdinalIgnoreCase));

            var isAdmin = user.IsInRole("Admin") || user.Claims.Any(c =>
                (c.Type == ClaimTypes.Role || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase)) &&
                string.Equals(c.Value, "Admin", StringComparison.OrdinalIgnoreCase));

            if (!hasPermissionClaim && !isAdmin)
            {
                context.Result = new ObjectResult(new
                {
                    statusCode = StatusCodes.Status403Forbidden,
                    message = $"Forbidden: You do not possess the required permission '{_permission}' to perform this action."
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }
    }
}
