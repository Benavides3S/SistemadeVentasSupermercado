namespace SistemadeVentasSupermercado.Web.Data.Seeders
{
    public class SeedDb
    {
        private readonly DataContext _context;

        public SeedDb(DataContext context)
        {
            _context = context;
        }

        public async Task SeedAsync()
        {
            await new ProductsSeeder(_context).SeedProductsAsync();

        }
    }
}
