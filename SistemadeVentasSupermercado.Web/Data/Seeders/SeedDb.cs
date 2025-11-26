using SistemadeVentasSupermercado.Web.Data.Seeders;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Data
{
    public class SeedDb
    {
        private readonly DataContext _context;
        private readonly IUsersService _usersService;
        private readonly PermissionsSeeder _permissionsSeeder;
        private readonly UserRolesSeeder _userRolesSeeder;

        public SeedDb(DataContext context, IUsersService usersService)
        {
            _context = context;
            _usersService = usersService;
            _permissionsSeeder = new PermissionsSeeder(context);
            _userRolesSeeder = new UserRolesSeeder(context, usersService);
        }

        public async Task SeedAsync()
        {
            await _context.Database.EnsureCreatedAsync();
            await _permissionsSeeder.SeedAsync();
            await _userRolesSeeder.SeedAsync();
        }
    }
}
