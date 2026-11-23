using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Data.Seeders
{
    public class UserRolesSeeder
    {
        private readonly DataContext _context;
        private readonly IUsersService _usersService;

        public const string CASHIER_ROLE_NAME = "Cajero";
        public const string SUPERVISOR_ROLE_NAME = "Supervisor";

        /// <summary>Roles de la plantilla original. Se borran si ningún usuario los tiene.</summary>
        public static readonly string[] ObsoleteRoles = ["Gestor de contenido", "Basic"];

        /// <summary>Permisos con los que nace cada rol de ejemplo. Después el dueño los puede cambiar en Roles.</summary>
        public static readonly string[] CashierPermissions = ["createSales", "manageCashSessions"];
        public static readonly string[] SupervisorPermissions =
            [.. CashierPermissions, "showSales", "cancelSales", "createReturns", "showCashSessions", "showReports"];

        public UserRolesSeeder(DataContext context, IUsersService usersService)
        {
            _context = context;
            _usersService = usersService;
        }

        public async Task SeedAsync(string? passwordInicial, bool crearUsuariosDePrueba)
        {
            await SeedRolesAsync();

            if (string.IsNullOrWhiteSpace(passwordInicial))
            {
                Console.WriteLine("Seed:PasswordInicial no está configurada: no se crean usuarios iniciales.");
            }
            else
            {
                await CheckUsersAsync(passwordInicial, crearUsuariosDePrueba);
            }

            // Al final: así los usuarios de prueba ya pasaron a los roles nuevos.
            await RemoveObsoleteRolesAsync();
        }

        /// <summary>
        /// Admin, Cajero y Supervisor. Los de ejemplo reciben permisos solo al crearse:
        /// si el dueño los cambia después en Roles, no se pisan sus cambios.
        /// </summary>
        public async Task SeedRolesAsync()
        {
            await EnsureRoleAsync(Env.SUPER_ADMIN_ROLE_NAME, []);
            await EnsureRoleAsync(CASHIER_ROLE_NAME, CashierPermissions);
            await EnsureRoleAsync(SUPERVISOR_ROLE_NAME, SupervisorPermissions);
        }

        public async Task RemoveObsoleteRolesAsync()
        {
            foreach (string name in ObsoleteRoles)
            {
                SistemaVentasRole? role = await _context.SistemaVentasRoles.FirstOrDefaultAsync(r => r.Name == name);
                if (role is null) continue;

                int users = await _context.Users.CountAsync(u => u.SistemaVentasRoleId == role.Id);
                if (users > 0)
                {
                    Console.WriteLine($"El rol obsoleto '{name}' no se borra porque lo tienen {users} usuario(s). Cámbiales el rol en Usuarios.");
                    continue;
                }

                await _context.RolePermissions.Where(rp => rp.SistemaVentasRoleId == role.Id).ExecuteDeleteAsync();
                await _context.SistemaVentasRoles.Where(r => r.Id == role.Id).ExecuteDeleteAsync();
                Console.WriteLine($"Se borró el rol obsoleto '{name}'.");
            }
        }

        private async Task EnsureRoleAsync(string name, string[] permissionNames)
        {
            if (await _context.SistemaVentasRoles.AnyAsync(r => r.Name == name)) return;

            SistemaVentasRole role = new SistemaVentasRole { Id = Guid.NewGuid(), Name = name };
            _context.SistemaVentasRoles.Add(role);

            List<Guid> permissionIds = await _context.Permissions
                                                     .Where(p => permissionNames.Contains(p.Name))
                                                     .Select(p => p.Id)
                                                     .ToListAsync();
            foreach (Guid permissionId in permissionIds)
            {
                _context.RolePermissions.Add(new RolePermission { PermissionId = permissionId, SistemaVentasRoleId = role.Id });
            }

            await _context.SaveChangesAsync();
        }

        private async Task<Guid> RoleIdAsync(string name)
            => await _context.SistemaVentasRoles.Where(r => r.Name == name).Select(r => r.Id).SingleAsync();

        private async Task CheckUsersAsync(string passwordInicial, bool crearUsuariosDePrueba)
        {
            // Admin
            User? user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "manuel@yopmail.com");

            if (user is null)
            {
                user = new User
                {
                    Email = "manuel@yopmail.com",
                    FirstName = "Manuel",
                    LastName = "Domínguez",
                    PhoneNumber = "3000000000",
                    UserName = "manuel@yopmail.com",
                    Document = "1111",
                    SistemaVentasRoleId = await RoleIdAsync(Env.SUPER_ADMIN_ROLE_NAME),
                    // Seed:PasswordInicial es conocida por quien instala: el Admin debe cambiarla al entrar.
                    // (Los usuarios de prueba anad y jhond, solo en Development, no la cambian.)
                    MustChangePassword = true
                };

                await _usersService.AddUserAsync(user, passwordInicial);

                string token = (await _usersService.GenerateConfirmationTokenAsync(user)).Result;
                await _usersService.ConfirmUserAsync(user, token);
            }
            else if (!user.MustChangePassword && await _usersService.CheckPasswordAsync(user, passwordInicial))
            {
                // El Admin se creó antes de existir el cambio obligatorio (la columna llegó en false)
                // y sigue con la clave inicial, que conoce quien instaló: se le pide cambiarla.
                user.MustChangePassword = true;
                await _context.SaveChangesAsync();
            }

            if (!crearUsuariosDePrueba) return;

            // Usuarios de prueba (solo Development), útiles para probar permisos sin el rol Admin.
            await TestUserAsync("anad@yopmail.com", "Ana", "Doe", "3100000000", "222", CASHIER_ROLE_NAME, passwordInicial);
            await TestUserAsync("jhond@yopmail.com", "Jhon", "Doe", "3200000000", "333", SUPERVISOR_ROLE_NAME, passwordInicial);
        }

        private async Task TestUserAsync(string email, string firstName, string lastName, string phone,
                                         string document, string roleName, string password)
        {
            Guid roleId = await RoleIdAsync(roleName);
            User? user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == email);

            if (user is null)
            {
                user = new User
                {
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    PhoneNumber = phone,
                    UserName = email,
                    Document = document,
                    SistemaVentasRoleId = roleId
                };

                await _usersService.AddUserAsync(user, password);

                string token = (await _usersService.GenerateConfirmationTokenAsync(user)).Result;
                await _usersService.ConfirmUserAsync(user, token);
                return;
            }

            // Bases de desarrollo anteriores: los usuarios de prueba tenían roles de la plantilla.
            bool hasObsoleteRole = await _context.SistemaVentasRoles
                                                 .AnyAsync(r => r.Id == user.SistemaVentasRoleId && ObsoleteRoles.Contains(r.Name));
            if (hasObsoleteRole)
            {
                user.SistemaVentasRoleId = roleId;
                await _context.SaveChangesAsync();
            }
        }
    }
}
