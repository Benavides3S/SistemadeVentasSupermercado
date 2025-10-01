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
            List<Product> products = new List<Product>()
    {
        new Product { Id = Guid.NewGuid(), Name = "Arroz Diana 500g", Description = "Arroz blanco nacional", Price = 2500, Stock = 100, Category = "Granos" },
        new Product { Id = Guid.NewGuid(), Name = "Aceite Premier 1L", Description = "Aceite vegetal", Price = 9000, Stock = 50, Category = "Aceites" },
        new Product { Id = Guid.NewGuid(), Name = "Coca Cola 1.5L", Description = "Gaseosa Coca Cola botella", Price = 4500, Stock = 80, Category = "Bebidas" },
        new Product { Id = Guid.NewGuid(), Name = "Detergente Ariel 1Kg", Description = "Detergente en polvo", Price = 12000, Stock = 40, Category = "Aseo" }
    };

            foreach (Product product in products)
            {
                bool exists = await _context.Products.AnyAsync(p => p.Name == product.Name);

                if (!exists)
                {
                    await _context.Products.AddAsync(product);
                }
            }

            await _context.SaveChangesAsync();
        }


    }
}
