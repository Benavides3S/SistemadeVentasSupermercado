using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.Data
{
    // 👇 Aquí indicamos que el contexto usa User y SistemaVentasRole, ambos con GUID como clave
    public class DataContext : IdentityDbContext<User>
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options)
        {
        }

        public DbSet<Client> Clients { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<SistemaVentasRole> SistemaVentasRoles { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<Sale> Sales { get; set; }
        public DbSet<SaleItem> SaleItems { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<CashSession> CashSessions { get; set; }


        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder); // 👈 Esto debe ir primero para que Identity configure sus tablas
            ConfigureKeys(builder);
            ConfigureIndexes(builder);
        }

        private void ConfigureKeys(ModelBuilder builder)
        {
            // ✅ Clave compuesta correcta (PermissionId + SistemaVentasRoleId)
            builder.Entity<RolePermission>()
                   .HasKey(rp => new { rp.PermissionId, rp.SistemaVentasRoleId });

            builder.Entity<RolePermission>()
                   .HasOne(rp => rp.SistemaVentasRole)
                   .WithMany(r => r.RolePermissions)
                   .HasForeignKey(rp => rp.SistemaVentasRoleId);

            builder.Entity<RolePermission>()
                   .HasOne(rp => rp.Permission)
                   .WithMany(p => p.RolePermissions)
                   .HasForeignKey(rp => rp.PermissionId);
            builder.Entity<Sale>()
       .HasOne(s => s.User).WithMany()
       .HasForeignKey(s => s.UserId)
       .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Sale>()
                   .HasOne(s => s.Client).WithMany()
                   .HasForeignKey(s => s.ClientId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SaleItem>()
                   .HasOne(i => i.Sale).WithMany(s => s.Items)
                   .HasForeignKey(i => i.SaleId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SaleItem>()
                   .HasOne(i => i.Product).WithMany()
                   .HasForeignKey(i => i.ProductId)
                   .OnDelete(DeleteBehavior.Restrict);
            builder.Entity<Product>()
       .            HasOne(p => p.Category).WithMany(c => c.Products)
       .            HasForeignKey(p => p.CategoryId)
       .            OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Product>().Property(p => p.Price).HasPrecision(18, 2);
            builder.Entity<Sale>().Property(s => s.Total).HasPrecision(18, 2);
            builder.Entity<SaleItem>().Property(i => i.UnitPrice).HasPrecision(18, 2);
            builder.Entity<SaleItem>().Property(i => i.Total).HasPrecision(18, 2);
            builder.Entity<Product>().Property(p => p.Stock).HasPrecision(18, 3);
            builder.Entity<SaleItem>().Property(i => i.Quantity).HasPrecision(18, 3);
            builder.Entity<Product>().ToTable(t => t.HasCheckConstraint("CK_Products_Stock_NonNegative", "[Stock] >= 0"));

            // ---------- Caja ----------
            builder.Entity<CashSession>()
                   .HasOne(c => c.User).WithMany()
                   .HasForeignKey(c => c.UserId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<CashSession>()
                   .HasOne(c => c.ClosedByUser).WithMany()
                   .HasForeignKey(c => c.ClosedByUserId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Sale>()
                   .HasOne(s => s.CashSession).WithMany(c => c.Sales)
                   .HasForeignKey(s => s.CashSessionId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<CashSession>().Property(c => c.OpeningAmount).HasPrecision(18, 2);
            builder.Entity<CashSession>().Property(c => c.CountedAmount).HasPrecision(18, 2);
            builder.Entity<CashSession>().Property(c => c.ExpectedAmount).HasPrecision(18, 2);
            builder.Entity<CashSession>().Property(c => c.Difference).HasPrecision(18, 2);
            builder.Entity<CashSession>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_CashSessions_OpeningAmount_NonNegative", "[OpeningAmount] >= 0");
                t.HasCheckConstraint("CK_CashSessions_CountedAmount_NonNegative", "[CountedAmount] IS NULL OR [CountedAmount] >= 0");
            });
        }

        private void ConfigureIndexes(ModelBuilder builder)
        {
            builder.Entity<SistemaVentasRole>()
                   .HasIndex(r => r.Name)
                   .IsUnique();

           

            builder.Entity<Product>()
                   .HasIndex(p => p.Name)
                   .IsUnique();

            builder.Entity<Client>()
                   .HasIndex(c => c.Email)
                   .IsUnique();

            builder.Entity<User>()
                   .HasIndex(u => u.Document)
                   .IsUnique();

            builder.Entity<Permission>()
                   .HasIndex(p => p.Name)
                   .IsUnique();
            builder.Entity<Sale>().HasIndex(s => s.Number).IsUnique();
            builder.Entity<Sale>().HasIndex(s => s.CreatedAtUtc);
            builder.Entity<Category>().HasIndex(c => c.Name).IsUnique();

            builder.Entity<CashSession>().HasIndex(c => c.Number).IsUnique();

            // Un cajero solo puede tener UN turno abierto (Status = 1 = Open).
            // Lo garantiza la base de datos, aunque dos pestañas intenten abrir caja a la vez.
            builder.Entity<CashSession>()
                   .HasIndex(c => c.UserId)
                   .IsUnique()
                   .HasFilter("[Status] = 1")
                   .HasDatabaseName("IX_CashSessions_UserId_OneOpen");
        }
    }
}
