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
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<CashSession> CashSessions { get; set; }
        public DbSet<SaleReturn> SaleReturns { get; set; }
        public DbSet<SaleReturnItem> SaleReturnItems { get; set; }
        public DbSet<Purchase> Purchases { get; set; }
        public DbSet<PurchaseItem> PurchaseItems { get; set; }
        public DbSet<InventoryMovement> InventoryMovements { get; set; }
        public DbSet<UserActivity> UserActivities { get; set; }


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
                   .HasOne(s => s.CancelledByUser).WithMany()
                   .HasForeignKey(s => s.CancelledByUserId)
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

            // ---------- Devoluciones ----------
            builder.Entity<SaleReturn>()
                   .HasOne(r => r.Sale).WithMany(s => s.Returns)
                   .HasForeignKey(r => r.SaleId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SaleReturn>()
                   .HasOne(r => r.User).WithMany()
                   .HasForeignKey(r => r.UserId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SaleReturn>()
                   .HasOne(r => r.CashSession).WithMany()
                   .HasForeignKey(r => r.CashSessionId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SaleReturnItem>()
                   .HasOne(i => i.SaleReturn).WithMany(r => r.Items)
                   .HasForeignKey(i => i.SaleReturnId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<SaleReturnItem>()
                   .HasOne(i => i.SaleItem).WithMany()
                   .HasForeignKey(i => i.SaleItemId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SaleReturnItem>()
                   .HasOne(i => i.Product).WithMany()
                   .HasForeignKey(i => i.ProductId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<SaleReturn>().Property(r => r.Total).HasPrecision(18, 2);
            builder.Entity<SaleReturnItem>().Property(i => i.UnitPrice).HasPrecision(18, 2);
            builder.Entity<SaleReturnItem>().Property(i => i.Total).HasPrecision(18, 2);
            builder.Entity<SaleReturnItem>().Property(i => i.Quantity).HasPrecision(18, 3);
            builder.Entity<SaleReturnItem>().ToTable(t =>
                t.HasCheckConstraint("CK_SaleReturnItems_Quantity_Positive", "[Quantity] > 0"));

            // Lo devuelto nunca puede superar el total de la venta (red de seguridad en la base de datos).
            builder.Entity<Sale>().Property(s => s.ReturnedTotal).HasPrecision(18, 2);
            builder.Entity<Sale>().ToTable(t =>
                t.HasCheckConstraint("CK_Sales_ReturnedTotal_Range", "[ReturnedTotal] >= 0 AND [ReturnedTotal] <= [Total]"));

            // ---------- Compras ----------
            builder.Entity<Purchase>()
                   .HasOne(p => p.User).WithMany()
                   .HasForeignKey(p => p.UserId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Purchase>()
                   .HasOne(p => p.Supplier).WithMany()
                   .HasForeignKey(p => p.SupplierId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Purchase>()
                   .HasOne(p => p.CancelledByUser).WithMany()
                   .HasForeignKey(p => p.CancelledByUserId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<PurchaseItem>()
                   .HasOne(i => i.Purchase).WithMany(p => p.Items)
                   .HasForeignKey(i => i.PurchaseId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PurchaseItem>()
                   .HasOne(i => i.Product).WithMany()
                   .HasForeignKey(i => i.ProductId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Product>().Property(p => p.Cost).HasPrecision(18, 2);
            builder.Entity<Purchase>().Property(p => p.Total).HasPrecision(18, 2);
            builder.Entity<PurchaseItem>().Property(i => i.UnitCost).HasPrecision(18, 2);
            builder.Entity<PurchaseItem>().Property(i => i.Total).HasPrecision(18, 2);
            builder.Entity<PurchaseItem>().Property(i => i.Quantity).HasPrecision(18, 3);
            builder.Entity<PurchaseItem>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_PurchaseItems_Quantity_Positive", "[Quantity] > 0");
                t.HasCheckConstraint("CK_PurchaseItems_UnitCost_Positive", "[UnitCost] > 0");
            });

            // ---------- Historial de inventario ----------
            // Restrict: un producto o usuario con movimientos no se borra (el historial no se pierde).
            builder.Entity<InventoryMovement>()
                   .HasOne(m => m.Product).WithMany()
                   .HasForeignKey(m => m.ProductId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<InventoryMovement>()
                   .HasOne(m => m.User).WithMany()
                   .HasForeignKey(m => m.UserId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<InventoryMovement>().Property(m => m.Quantity).HasPrecision(18, 3);
            builder.Entity<InventoryMovement>().Property(m => m.StockAfter).HasPrecision(18, 3);
            builder.Entity<InventoryMovement>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_InventoryMovements_Quantity_NonZero", "[Quantity] <> 0");
                t.HasCheckConstraint("CK_InventoryMovements_StockAfter_NonNegative", "[StockAfter] >= 0");
            });

            // ---------- Historial de actividad de usuarios ----------
            // Restrict: un usuario con actividad no se borra (los usuarios se desactivan).
            builder.Entity<UserActivity>()
                   .HasOne(a => a.ActorUser).WithMany()
                   .HasForeignKey(a => a.ActorUserId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<UserActivity>()
                   .HasOne(a => a.TargetUser).WithMany()
                   .HasForeignKey(a => a.TargetUserId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<UserActivity>().ToTable(t =>
                t.HasCheckConstraint("CK_UserActivities_AttemptCount_Positive", "[AttemptCount] >= 1"));
        }

        private void ConfigureIndexes(ModelBuilder builder)
        {
            builder.Entity<SistemaVentasRole>()
                   .HasIndex(r => r.Name)
                   .IsUnique();

           

            builder.Entity<Product>()
                   .HasIndex(p => p.Name)
                   .IsUnique();

            // Dos productos no pueden tener el mismo código de barras. Los productos sin código quedan fuera del índice.
            builder.Entity<Product>()
                   .HasIndex(p => p.Barcode)
                   .IsUnique()
                   .HasFilter("[Barcode] IS NOT NULL")
                   .HasDatabaseName("IX_Products_Barcode");

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

            // Un mismo cobro (mismo código del punto de venta y mismo cajero) solo se registra una vez.
            // El código va primero para no reemplazar el índice de la relación con el usuario (IX_Sales_UserId).
            // Las ventas anteriores a este cambio no tienen código y quedan fuera del índice.
            builder.Entity<Sale>()
                   .HasIndex(s => new { s.IdempotencyKey, s.UserId })
                   .IsUnique()
                   .HasFilter("[IdempotencyKey] IS NOT NULL")
                   .HasDatabaseName("IX_Sales_IdempotencyKey_UserId");
            builder.Entity<Category>().HasIndex(c => c.Name).IsUnique();

            builder.Entity<Supplier>().HasIndex(s => s.Name).IsUnique();
            // El NIT/documento es opcional: solo es único cuando existe.
            builder.Entity<Supplier>()
                   .HasIndex(s => s.TaxId)
                   .IsUnique()
                   .HasFilter("[TaxId] IS NOT NULL")
                   .HasDatabaseName("IX_Suppliers_TaxId");

            builder.Entity<CashSession>().HasIndex(c => c.Number).IsUnique();
            builder.Entity<SaleReturn>().HasIndex(r => r.Number).IsUnique();

            builder.Entity<Purchase>().HasIndex(p => p.Number).IsUnique();
            builder.Entity<Purchase>().HasIndex(p => p.CreatedAtUtc);

            // Una misma compra (mismo código de la pantalla y mismo usuario) solo se registra una vez.
            // El código va primero para no reemplazar el índice de la relación con el usuario (IX_Purchases_UserId).
            builder.Entity<Purchase>()
                   .HasIndex(p => new { p.IdempotencyKey, p.UserId })
                   .IsUnique()
                   .HasFilter("[IdempotencyKey] IS NOT NULL")
                   .HasDatabaseName("IX_Purchases_IdempotencyKey_UserId");

            builder.Entity<UserActivity>().HasIndex(a => a.Number).IsUnique();
            builder.Entity<UserActivity>().HasIndex(a => a.OccurredAtUtc);
            // Un solo registro por grupo de fallos (mismo tipo, correo, IP y minuto), aunque lleguen a la vez.
            builder.Entity<UserActivity>()
                   .HasIndex(a => a.GroupKey)
                   .IsUnique()
                   .HasFilter("[GroupKey] IS NOT NULL")
                   .HasDatabaseName("IX_UserActivities_GroupKey");

            builder.Entity<InventoryMovement>().HasIndex(m => m.Number).IsUnique();
            builder.Entity<InventoryMovement>().HasIndex(m => m.CreatedAtUtc);

            // Historial de un producto (el más reciente primero). También cubre la FK a Products.
            builder.Entity<InventoryMovement>()
                   .HasIndex(m => new { m.ProductId, m.Number })
                   .HasDatabaseName("IX_InventoryMovements_ProductId_Number");

            // Un mismo ajuste (mismo código del formulario y mismo usuario) solo se aplica una vez.
            // El código va primero para no reemplazar el índice de la relación con el usuario.
            builder.Entity<InventoryMovement>()
                   .HasIndex(m => new { m.IdempotencyKey, m.UserId })
                   .IsUnique()
                   .HasFilter("[IdempotencyKey] IS NOT NULL")
                   .HasDatabaseName("IX_InventoryMovements_IdempotencyKey_UserId");

            // No es único: la factura repetida de un proveedor solo se avisa. El índice acelera esa búsqueda.
            builder.Entity<Purchase>()
                   .HasIndex(p => new { p.SupplierId, p.SupplierInvoiceNumber })
                   .HasDatabaseName("IX_Purchases_SupplierId_SupplierInvoiceNumber");

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
