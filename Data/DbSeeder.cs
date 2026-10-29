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
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

            // 1. Seed System Permissions
            var systemPermissions = new List<(string Name, string Category, string Description)>
            {
                // Products
                ("products:read", "Products", "View products and product details"),
                ("products:create", "Products", "Create new products and variants"),
                ("products:update", "Products", "Update existing products"),
                ("products:delete", "Products", "Delete products and variants"),

                // Categories & Brands
                ("categories:read", "Categories", "View categories"),
                ("categories:create", "Categories", "Create new categories"),
                ("categories:update", "Categories", "Update existing categories"),
                ("categories:delete", "Categories", "Delete categories"),
                ("categories:manage", "Categories", "Full category management"),
                ("brands:read", "Brands", "View brands"),
                ("brands:create", "Brands", "Create new brands"),
                ("brands:update", "Brands", "Update existing brands"),
                ("brands:delete", "Brands", "Delete brands"),
                ("brands:manage", "Brands", "Full brand management"),

                // Orders
                ("orders:read", "Orders", "View customer orders and histories"),
                ("orders:manage", "Orders", "Update order status and handle cancellations"),
                ("orders:review", "Orders", "Review and audit order logs"),

                // Inventory
                ("inventory:read", "Inventory", "View inventory stock levels and logs"),
                ("inventory:manage", "Inventory", "Update stock levels and log movements"),

                // Return Requests
                ("returns:read", "Returns", "View return requests"),
                ("returns:manage", "Returns", "Approve or reject customer return requests"),

                // Banners & Notifications
                ("banners:read", "Banners", "View banners"),
                ("banners:create", "Banners", "Create new banners"),
                ("banners:update", "Banners", "Update banners and toggle active status"),
                ("banners:delete", "Banners", "Delete banners"),
                ("banners:manage", "Banners", "Full banner management"),
                ("notifications:manage", "Notifications", "Broadcast notifications"),

                // Users & Roles
                ("users:read", "Users", "View user accounts"),
                ("users:manage", "Users", "Create and edit user accounts"),
                ("users:ban", "Users", "Ban and unban users"),
                ("roles:read", "Roles", "View roles and assigned permissions"),
                ("roles:manage", "Roles", "Create, update, delete roles and configure permissions"),

                // Dashboard & Reports
                ("dashboard:read", "Dashboard", "View analytical dashboard metrics and reports")
            };

            foreach (var (name, category, description) in systemPermissions)
            {
                if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(context.Permissions, p => p.Name == name))
                {
                    context.Permissions.Add(new Permission
                    {
                        Name = name,
                        Category = category,
                        Description = description
                    });
                }
            }
            await context.SaveChangesAsync();

            // 2. Seed System Roles
            var systemRoles = new Dictionary<string, (string Description, string[] Permissions)>
            {
                ["Admin"] = ("Full administrative access with all permissions", systemPermissions.Select(p => p.Name).ToArray()),
                ["Manager"] = ("Store operations manager with catalog, order, inventory, and return permissions", new[]
                {
                    "products:read", "products:create", "products:update",
                    "categories:read", "categories:create", "categories:update", "categories:manage",
                    "brands:read", "brands:create", "brands:update", "brands:manage",
                    "orders:read", "orders:manage", "orders:review",
                    "inventory:read", "inventory:manage",
                    "returns:read", "returns:manage",
                    "banners:read", "banners:create", "banners:update", "banners:manage",
                    "dashboard:read"
                }),
             
            };

            foreach (var (roleName, (desc, perms)) in systemRoles)
            {
                var role = await roleManager.FindByNameAsync(roleName);
                if (role == null)
                {
                    role = new Role(roleName) { Description = desc };
                    var result = await roleManager.CreateAsync(role);
                    if (result.Succeeded)
                    {
                        logger.LogInformation("Role '{Role}' created successfully.", roleName);
                    }
                }

                // Sync Permissions for this role
                if (role != null)
                {
                    var existingRolePermIds = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                        context.RolePermissions.Where(rp => rp.RoleId == role.Id).Select(rp => rp.PermissionId));

                    var targetPermissionIds = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                        context.Permissions.Where(p => perms.Contains(p.Name)).Select(p => p.Id));

                    var missingPermIds = targetPermissionIds.Except(existingRolePermIds).ToList();
                    foreach (var permId in missingPermIds)
                    {
                        context.RolePermissions.Add(new RolePermission
                        {
                            RoleId = role.Id,
                            PermissionId = permId
                        });
                    }
                }
            }
            await context.SaveChangesAsync();

            // 3. Seed Default Admin Account
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

            // 4. Seed Default Manager Account
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
            // 5. Seed Default Shop Profile & Contacts
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
