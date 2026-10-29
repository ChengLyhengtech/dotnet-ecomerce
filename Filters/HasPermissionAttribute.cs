using System;
using Microsoft.AspNetCore.Mvc;

namespace Ecomerce_dotnet.Filters
{
    /// <summary>
    /// Dynamic Permission-Based Authorization Attribute.
    /// Usage example: [HasPermission("products:create")] or [HasPermission("orders:review")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class HasPermissionAttribute : TypeFilterAttribute
    {
        public string Permission { get; }

        public HasPermissionAttribute(string permission) : base(typeof(HasPermissionAuthorizationFilter))
        {
            Permission = permission;
            Arguments = new object[] { permission };
        }
    }
}
