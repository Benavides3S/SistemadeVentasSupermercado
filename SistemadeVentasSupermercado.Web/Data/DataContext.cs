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
        public DbSet<Discount> Discounts { get; set; }
        public DbSet<PaymentMethod> PaymentMethods { get; set; }

        public DbSet<CashRegister> CashRegisters { get; set; }
        // Add this property to your DataContext class:
        public DbSet<Sale> Sales { get; set; }
        public DbSet<SaleDetail> SaleDetails { get; set; }



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
        }
    }
}
