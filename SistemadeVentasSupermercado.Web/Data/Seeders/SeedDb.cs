using SistemadeVentasSupermercado.Web.Services.Implementations;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
namespace SistemadeVentasSupermercado.Web.Data.Seeders
{
    public class SeedDb
    {
        private readonly DataContext _context;
        private readonly IUsersService _usersService;

        public SeedDb(DataContext context , IUsersService usersService)
        {
            _context = context;
            _usersService = usersService;
        }

        public async Task SeedAsync()
        {
            await new ProductsSeeder(_context).SeedProductsAsync();
            await new PermissionsSeeder(_context).SeedAsync();
            await new UserRolesSeeder(_context, _usersService).SeedAsync();

        }
    }
}
