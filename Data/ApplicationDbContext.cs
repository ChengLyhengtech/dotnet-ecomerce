using Ecomerce_dotnet.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore; // Added this
using Microsoft.EntityFrameworkCore;

namespace Ecomerce_dotnet.Data
{
    public class ApplicationDbContext : IdentityDbContext<
        User,
        Role,
        Guid,
        IdentityUserClaim<Guid>,
        UserRole,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        // RBAC & Permission Entities
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }

        // NOTE: IdentityDbContext already includes DbSets for Users, Roles, UserRoles, etc.
        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<ProductVariant> ProductVariants { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Banner> Banners { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<WishlistItem> WishlistItems { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<Brand> Brands { get; set; } 
        public DbSet<Cart> Carts { get; set; }
        public DbSet<StockMovement> StockMovements { get; set; }
        public DbSet<ReturnRequest> ReturnRequests { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<ShopProfile> ShopProfiles { get; set; }
        public DbSet<ShopContact> ShopContacts { get; set; }
        public DbSet<ProductReview> ProductReviews { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. Configure Decimal precisions
            modelBuilder.Entity<Product>().Property(p => p.BasePrice).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<Product>().Property(p => p.DiscountPercentage).HasColumnType("decimal(5,2)");
            modelBuilder.Entity<Order>().Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<OrderItem>().Property(oi => oi.PriceAtPurchase).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<OrderItem>().Property(oi => oi.UnitCost).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<ProductVariant>().Property(pv => pv.CostPrice).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<StockMovement>().Property(sm => sm.UnitCost).HasColumnType("decimal(18,2)");

            // 2. Prevent Cascade Delete loops
            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.ProductVariant)
                .WithMany()
                .HasForeignKey(oi => oi.VariantId)
                .OnDelete(DeleteBehavior.Restrict);

            // If your database table is literally singular 'Brand', 
            // add this line below to force EF Core to point to the singular table name:
            modelBuilder.Entity<Brand>().ToTable("Brand");

            // 3. Configure 1-to-1 relationship for Order & Payment
            modelBuilder.Entity<Order>()
                .HasOne(o => o.Payment)
                .WithOne(p => p.Order)
                .HasForeignKey<Payment>(p => p.OrderId);

            // 4. Configure StockMovement mappings
            modelBuilder.Entity<StockMovement>()
                .Property(sm => sm.MovementType)
                .HasConversion<string>()
                .HasMaxLength(50);

            modelBuilder.Entity<StockMovement>()
                .Property(sm => sm.ReasonCode)
                .IsRequired()
                .HasMaxLength(100);

            modelBuilder.Entity<StockMovement>()
                .Property(sm => sm.ReferenceId)
                .HasMaxLength(100);

            modelBuilder.Entity<StockMovement>()
                .Property(sm => sm.Notes)
                .HasMaxLength(500);

            modelBuilder.Entity<StockMovement>()
                .HasOne(sm => sm.ProductVariant)
                .WithMany()
                .HasForeignKey(sm => sm.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);

            // 5. Configure ReturnRequest mappings
            modelBuilder.Entity<ReturnRequest>()
                .Property(rr => rr.CustomerReason)
                .IsRequired()
                .HasMaxLength(200);

            modelBuilder.Entity<ReturnRequest>()
                .Property(rr => rr.CustomerNotes)
                .HasMaxLength(500);

            modelBuilder.Entity<ReturnRequest>()
                .Property(rr => rr.AdminNotes)
                .HasMaxLength(500);

            modelBuilder.Entity<ReturnRequest>()
                .Property(rr => rr.Status)
                .HasConversion<string>()
                .HasMaxLength(50);

            // 6. Configure ProductImage index for fast color-based image lookups
            modelBuilder.Entity<ProductImage>()
                .HasIndex(pi => new { pi.ProductId, pi.Color });

            modelBuilder.Entity<ReturnRequest>()
                .HasOne(rr => rr.Order)
                .WithMany()
                .HasForeignKey(rr => rr.OrderId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ReturnRequest>()
                .HasOne(rr => rr.ProductVariant)
                .WithMany()
                .HasForeignKey(rr => rr.ProductVariantId)
                .OnDelete(DeleteBehavior.Restrict);

            // 6. Configure Notification mappings
            modelBuilder.Entity<Notification>()
                .Property(n => n.Title)
                .IsRequired()
                .HasMaxLength(100);

            modelBuilder.Entity<Notification>()
                .Property(n => n.Message)
                .IsRequired()
                .HasMaxLength(500);

            modelBuilder.Entity<Notification>()
                .Property(n => n.Type)
                .IsRequired()
                .HasMaxLength(50);

            modelBuilder.Entity<Notification>()
                .Property(n => n.TargetUrl)
                .HasMaxLength(250);

            // 7. Configure Category Self-Referencing Parent-Child relationship
            modelBuilder.Entity<Category>()
                .HasOne(c => c.ParentCategory)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // 8. Configure RBAC - UserRole (Many-to-Many junction between User and Role)
            modelBuilder.Entity<UserRole>(b =>
            {
                b.HasKey(ur => new { ur.UserId, ur.RoleId });

                b.HasOne(ur => ur.User)
                    .WithMany(u => u.UserRoles)
                    .HasForeignKey(ur => ur.UserId)
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(ur => ur.Role)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(ur => ur.RoleId)
                    .IsRequired()
                    .OnDelete(DeleteBehavior.Cascade);

                b.ToTable("AspNetUserRoles");
            });

            // 9. Configure RBAC - RolePermission (Many-to-Many junction between Role and Permission)
            modelBuilder.Entity<RolePermission>(b =>
            {
                b.HasKey(rp => new { rp.RoleId, rp.PermissionId });

                b.HasOne(rp => rp.Role)
                    .WithMany(r => r.RolePermissions)
                    .HasForeignKey(rp => rp.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(rp => rp.Permission)
                    .WithMany(p => p.RolePermissions)
                    .HasForeignKey(rp => rp.PermissionId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.ToTable("RolePermissions");
            });

            // 10. Configure RBAC - Permission Entity
            modelBuilder.Entity<Permission>(b =>
            {
                b.HasKey(p => p.Id);
                b.Property(p => p.Name).IsRequired().HasMaxLength(100);
                b.Property(p => p.Category).IsRequired().HasMaxLength(100);
                b.Property(p => p.Description).HasMaxLength(250);
                b.HasIndex(p => p.Name).IsUnique();
                b.ToTable("Permissions");
            });

            // 11. Configure RBAC - Role Entity
            modelBuilder.Entity<Role>(b =>
            {
                b.Property(r => r.Description).HasMaxLength(250);
            });

            // 12. Configure ProductReview Entity & Relationships
            modelBuilder.Entity<ProductReview>(b =>
            {
                b.HasKey(pr => pr.Id);
                b.Property(pr => pr.Rating).IsRequired();
                b.Property(pr => pr.Comment).HasMaxLength(1000);

                b.HasOne(pr => pr.Product)
                    .WithMany(p => p.Reviews)
                    .HasForeignKey(pr => pr.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(pr => pr.User)
                    .WithMany()
                    .HasForeignKey(pr => pr.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(pr => pr.Order)
                    .WithMany()
                    .HasForeignKey(pr => pr.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}