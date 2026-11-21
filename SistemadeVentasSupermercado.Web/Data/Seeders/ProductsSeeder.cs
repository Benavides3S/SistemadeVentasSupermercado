using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.Data.Seeders
{
    public class ProductsSeeder
    {
        private readonly DataContext _context;

        public ProductsSeeder(DataContext context)
        {
            _context = context;
        }

        public async Task SeedProductsAsync()
        {
            string[] categoryNames = { "Granos", "Aceites", "Bebidas", "Aseo", "Frutas y verduras" };

            foreach (string name in categoryNames)
            {
                if (!await _context.Categories.AnyAsync(c => c.Name == name))
                {
                    _context.Categories.Add(new Category { Id = Guid.NewGuid(), Name = name, IsActive = true });
                }
            }

            await _context.SaveChangesAsync();

            Dictionary<string, Guid> categories = await _context.Categories
                .ToDictionaryAsync(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

            List<Product> products = new()
            {
                new Product { Id = Guid.NewGuid(), Name = "Arroz Diana 500g", Description = "Arroz blanco nacional", Price = 2500, Stock = 100, CategoryId = categories["Granos"] },
                new Product { Id = Guid.NewGuid(), Name = "Aceite Premier 1L", Description = "Aceite vegetal", Price = 9000, Stock = 50, CategoryId = categories["Aceites"] },
                new Product { Id = Guid.NewGuid(), Name = "Coca Cola 1.5L", Description = "Gaseosa Coca Cola botella", Price = 4500, Stock = 80, CategoryId = categories["Bebidas"] },
                new Product { Id = Guid.NewGuid(), Name = "Detergente Ariel 1Kg", Description = "Detergente en polvo", Price = 12000, Stock = 40, CategoryId = categories["Aseo"] },
                new Product { Id = Guid.NewGuid(), Name = "Tomate chonto", Description = "Se vende por kilo", Price = 4000, Stock = 25.5m, Unit = UnitOfMeasure.Kilogram, CategoryId = categories["Frutas y verduras"] },
                new Product { Id = Guid.NewGuid(), Name = "Papa pastusa", Description = "Se vende por kilo", Price = 3200, Stock = 40m, Unit = UnitOfMeasure.Kilogram, CategoryId = categories["Frutas y verduras"] }
            };

            foreach (Product product in products)
            {
                if (!await _context.Products.AnyAsync(p => p.Name == product.Name))
                {
                    await _context.Products.AddAsync(product);
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}