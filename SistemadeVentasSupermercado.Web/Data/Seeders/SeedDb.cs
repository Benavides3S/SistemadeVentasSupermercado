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

        /// <param name="esDesarrollo">En Development se crean también productos y usuarios de prueba.</param>
        public async Task SeedAsync(string? passwordInicial, bool esDesarrollo)
        {
            // Las categorías de ejemplo se crean siempre (punto de partida); los productos solo en desarrollo.
            await new ProductsSeeder(_context).SeedAsync(crearProductos: esDesarrollo);
            // Después de los productos: los de ejemplo y los que ya existían reciben su saldo inicial.
            await new InventorySeeder(_context).SeedOpeningBalanceAsync();
            await new PermissionsSeeder(_context).SeedAsync();
            await new UserRolesSeeder(_context, _usersService).SeedAsync(passwordInicial, esDesarrollo);
        }
    }
}
