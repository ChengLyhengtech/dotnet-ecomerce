using System;
using System.Threading.Tasks;
using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ecomerce_dotnet.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

            // 1. Seed System Roles
            string[] roles = { "Admin", "Manager", "Customer" };

            foreach (var roleName in roles)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    var result = await roleManager.CreateAsync(new IdentityRole<Guid> { Name = roleName });
                    if (result.Succeeded)
                    {
                        logger.LogInformation("Role '{Role}' created successfully.", roleName);
                    }
                }
            }

            // 2. Seed Default Admin Account
            var adminEmail = "admin@ecommerce.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            if (adminUser == null)
            {
                adminUser = new User
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = "System Administrator",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow,
                    IsBanned = false
                };

                var createAdminResult = await userManager.CreateAsync(adminUser, "Admin@123456");
                if (createAdminResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                    logger.LogInformation("Default Admin account '{Email}' seeded successfully.", adminEmail);
                }
                else
                {
                    logger.LogError("Failed to seed Admin account: {Errors}", string.Join(", ", createAdminResult.Errors));
                }
            }

            // 3. Seed Default Manager Account
            var managerEmail = "manager@ecommerce.com";
            var managerUser = await userManager.FindByEmailAsync(managerEmail);

            if (managerUser == null)
            {
                managerUser = new User
                {
                    UserName = managerEmail,
                    Email = managerEmail,
                    FullName = "Store Manager",
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow,
                    IsBanned = false
                };

                var createManagerResult = await userManager.CreateAsync(managerUser, "Manager@123456");
                if (createManagerResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(managerUser, "Manager");
                    logger.LogInformation("Default Manager account '{Email}' seeded successfully.", managerEmail);
                }
                else
                {
                    logger.LogError("Failed to seed Manager account: {Errors}", string.Join(", ", createManagerResult.Errors));
                }
            }
        }
    }
}
