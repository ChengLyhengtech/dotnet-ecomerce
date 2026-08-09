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
            // 4. Seed Default Shop Profile & Contacts
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(context.ShopProfiles))
            {
                var defaultProfile = new ShopProfile
                {
                    ShopName = "My Commerce Store",
                    Description = "Your premier destination for high quality products.",
                    Email = "support@ecommerce.com",
                    Phone = "+855 12 345 678",
                    Address = "St 2004, Phnom Penh, Cambodia",
                    GoogleMapUrl = "https://maps.google.com/?q=11.5564,104.9282",
                    Latitude = 11.5564,
                    Longitude = 104.9282,
                    OpeningHours = "Mon - Sun: 8:00 AM - 8:00 PM",
                    UpdatedAt = DateTime.UtcNow
                };
                context.ShopProfiles.Add(defaultProfile);
                await context.SaveChangesAsync();
                logger.LogInformation("Default ShopProfile seeded successfully.");
            }

            if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(context.ShopContacts))
            {
                var defaultContacts = new[]
                {
                    new ShopContact
                    {
                        Title = "Facebook",
                        ProfileUrl = "https://facebook.com/ecommerce",
                        IconUrl = "/uploads/contacts/default_facebook.png",
                        ContactType = "Social",
                        DisplayOrder = 1,
                        IsActive = true
                    },
                    new ShopContact
                    {
                        Title = "TikTok",
                        ProfileUrl = "https://tiktok.com/@ecommerce",
                        IconUrl = "/uploads/contacts/default_tiktok.png",
                        ContactType = "Social",
                        DisplayOrder = 2,
                        IsActive = true
                    },
                    new ShopContact
                    {
                        Title = "Phone",
                        ProfileUrl = "tel:+85512345678",
                        IconUrl = "/uploads/contacts/default_phone.png",
                        ContactType = "Phone",
                        DisplayOrder = 3,
                        IsActive = true
                    },
                    new ShopContact
                    {
                        Title = "Telegram",
                        ProfileUrl = "https://t.me/ecommerce_support",
                        IconUrl = "/uploads/contacts/default_telegram.png",
                        ContactType = "Messenger",
                        DisplayOrder = 4,
                        IsActive = true
                    }
                };
                context.ShopContacts.AddRange(defaultContacts);
                await context.SaveChangesAsync();
                logger.LogInformation("Default ShopContacts seeded successfully.");
            }
        }
    }
}
