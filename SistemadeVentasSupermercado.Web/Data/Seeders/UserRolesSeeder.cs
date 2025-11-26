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
        private const string CONTENT_MANAGER_ROLE_NAME = "Gestor de contenido";
        private const string BASIC_ROLE_NAME = "Basic";
        private const string ADMINISTRATOR_ROLE_NAME = "Administrador";
        private const string SUPERVISOR_ROLE_NAME = "Supervisor";
        private const string CASHIER_ROLE_NAME = "Cajero";

        public UserRolesSeeder(DataContext context, IUsersService usersService)
        {
            _context = context;
            _usersService = usersService;
        }

        public async Task SeedAsync()
        {
            await CheckRolesAsync();
            await CheckUsersAsync();
        }

        private async Task CheckRolesAsync()
        {
            await AdminRoleAsync();
            await AdministratorRoleAsync();
            await SupervisorRoleAsync();
            await CashierRoleAsync();
            await BasicRoleAsync();
            await ContentManagerRoleAsync();
        }

        private async Task CheckUsersAsync()
        {
            // Super Admin
            User? user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "manuel@yopmail.com");

            if (user is null)
            {
                SistemaVentasRole adminRole = await _context.SistemaVentasRoles.FirstOrDefaultAsync(r => r.Name == Env.SUPER_ADMIN_ROLE_NAME);

                user = new User
                {
                    Email = "manuel@yopmail.com",
                    FirstName = "Manuel",
                    LastName = "Domínguez",
                    PhoneNumber = "3000000000",
                    UserName = "manuel@yopmail.com",
                    Document = "1111",
                    SistemaVentasRoleId = adminRole!.Id
                };

                await _usersService.AddUserAsync(user, "1234");

                string token = (await _usersService.GenerateConfirmationTokenAsync(user)).Result;
                await _usersService.ConfirmUserAsync(user, token);
            }

            // Administrator
            user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "admin@yopmail.com");

            if (user is null)
            {
                SistemaVentasRole adminRole = await _context.SistemaVentasRoles.FirstOrDefaultAsync(r => r.Name == ADMINISTRATOR_ROLE_NAME);

                user = new User
                {
                    Email = "admin@yopmail.com",
                    FirstName = "Admin",
                    LastName = "Sistema",
                    PhoneNumber = "3000000001",
                    UserName = "admin@yopmail.com",
                    Document = "1112",
                    SistemaVentasRoleId = adminRole!.Id
                };

                await _usersService.AddUserAsync(user, "1234");

                string token = (await _usersService.GenerateConfirmationTokenAsync(user)).Result;
                await _usersService.ConfirmUserAsync(user, token);
            }

            // Supervisor
            user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "supervisor@yopmail.com");

            if (user is null)
            {
                SistemaVentasRole supervisorRole = await _context.SistemaVentasRoles.FirstOrDefaultAsync(r => r.Name == SUPERVISOR_ROLE_NAME);

                user = new User
                {
                    Email = "supervisor@yopmail.com",
                    FirstName = "Supervisor",
                    LastName = "Ventas",
                    PhoneNumber = "3000000002",
                    UserName = "supervisor@yopmail.com",
                    Document = "1113",
                    SistemaVentasRoleId = supervisorRole!.Id
                };

                await _usersService.AddUserAsync(user, "1234");

                string token = (await _usersService.GenerateConfirmationTokenAsync(user)).Result;
                await _usersService.ConfirmUserAsync(user, token);
            }

            // Cashier
            user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "cajero@yopmail.com");

            if (user is null)
            {
                SistemaVentasRole cashierRole = await _context.SistemaVentasRoles.FirstOrDefaultAsync(r => r.Name == CASHIER_ROLE_NAME);

                user = new User
                {
                    Email = "cajero@yopmail.com",
                    FirstName = "Cajero",
                    LastName = "Principal",
                    PhoneNumber = "3000000003",
                    UserName = "cajero@yopmail.com",
                    Document = "1114",
                    SistemaVentasRoleId = cashierRole!.Id
                };

                await _usersService.AddUserAsync(user, "1234");

                string token = (await _usersService.GenerateConfirmationTokenAsync(user)).Result;
                await _usersService.ConfirmUserAsync(user, token);
            }

            // Content manager
            user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "anad@yopmail.com");

            if (user is null)
            {
                SistemaVentasRole contentManagerRole = await _context.SistemaVentasRoles.FirstOrDefaultAsync(r => r.Name == CONTENT_MANAGER_ROLE_NAME);

                user = new User
                {
                    Email = "anad@yopmail.com",
                    FirstName = "Ana",
                    LastName = "Doe",
                    PhoneNumber = "3100000000",
                    UserName = "anad@yopmail.com",
                    Document = "222",
                    SistemaVentasRoleId = contentManagerRole!.Id
                };

                await _usersService.AddUserAsync(user, "1234");

                string token = (await _usersService.GenerateConfirmationTokenAsync(user)).Result;
                await _usersService.ConfirmUserAsync(user, token);
            }

            // Basic
            user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == "jhond@yopmail.com");

            if (user is null)
            {
                SistemaVentasRole basicRole = await _context.SistemaVentasRoles.FirstOrDefaultAsync(r => r.Name == BASIC_ROLE_NAME);

                user = new User
                {
                    Email = "jhond@yopmail.com",
                    FirstName = "Jhon",
                    LastName = "Doe",
                    PhoneNumber = "3200000000",
                    UserName = "jhond@yopmail.com",
                    Document = "333",
                    SistemaVentasRoleId = basicRole!.Id
                };

                await _usersService.AddUserAsync(user, "1234");

                string token = (await _usersService.GenerateConfirmationTokenAsync(user)).Result;
                await _usersService.ConfirmUserAsync(user, token);
            }
        }

        private async Task AdminRoleAsync()
        {
            bool exists = await _context.SistemaVentasRoles.AnyAsync(r => r.Name == Env.SUPER_ADMIN_ROLE_NAME);

            if (!exists)
            {
                SistemaVentasRole role = new SistemaVentasRole { Id = Guid.NewGuid(), Name = Env.SUPER_ADMIN_ROLE_NAME };
                await _context.SistemaVentasRoles.AddAsync(role);

                // Super Admin tiene todos los permisos
                List<Permission> allPermissions = await _context.Permissions.ToListAsync();
                foreach (Permission permission in allPermissions)
                {
                    await _context.RolePermissions.AddAsync(new RolePermission
                    {
                        PermissionId = permission.Id,
                        SistemaVentasRoleId = role.Id
                    });
                }

                await _context.SaveChangesAsync();
            }
        }

        private async Task AdministratorRoleAsync()
        {
            bool exists = await _context.SistemaVentasRoles.AnyAsync(r => r.Name == ADMINISTRATOR_ROLE_NAME);

            if (!exists)
            {
                SistemaVentasRole role = new SistemaVentasRole { Id = Guid.NewGuid(), Name = ADMINISTRATOR_ROLE_NAME };
                await _context.SistemaVentasRoles.AddAsync(role);

                // Administrador tiene permisos según la matriz
                List<Permission> permissions = await _context.Permissions
                    .Where(p => p.Module == "Configuración" || // Todos los permisos de configuración
                               p.Module == "Gestión de Usuarios" ||
                               p.Module == "Gestión de Invitación" ||
                               p.Module == "Gestión de Ventas" ||
                               p.Module == "Gestión de Caja" ||
                               p.Module == "Reportes y Análisis" ||
                               p.Module == "Gestión de clientes")
                    .ToListAsync();

                foreach (Permission permission in permissions)
                {
                    await _context.RolePermissions.AddAsync(new RolePermission
                    {
                        PermissionId = permission.Id,
                        SistemaVentasRoleId = role.Id
                    });
                }

                await _context.SaveChangesAsync();
            }
        }

        private async Task SupervisorRoleAsync()
        {
            bool exists = await _context.SistemaVentasRoles.AnyAsync(r => r.Name == SUPERVISOR_ROLE_NAME);

            if (!exists)
            {
                SistemaVentasRole role = new SistemaVentasRole { Id = Guid.NewGuid(), Name = SUPERVISOR_ROLE_NAME };
                await _context.SistemaVentasRoles.AddAsync(role);

                // Supervisor tiene permisos limitados según la matriz
                List<Permission> permissions = await _context.Permissions
                    .Where(p => p.Name.Contains("show") || // Solo permisos de visualización
                               (p.Module == "Gestión de Caja" && p.Name.Contains("show")) ||
                               (p.Module == "Reportes y Análisis" && p.Name.Contains("show")))
                    .ToListAsync();

                foreach (Permission permission in permissions)
                {
                    await _context.RolePermissions.AddAsync(new RolePermission
                    {
                        PermissionId = permission.Id,
                        SistemaVentasRoleId = role.Id
                    });
                }

                await _context.SaveChangesAsync();
            }
        }

        private async Task CashierRoleAsync()
        {
            bool exists = await _context.SistemaVentasRoles.AnyAsync(r => r.Name == CASHIER_ROLE_NAME);

            if (!exists)
            {
                SistemaVentasRole role = new SistemaVentasRole { Id = Guid.NewGuid(), Name = CASHIER_ROLE_NAME };
                await _context.SistemaVentasRoles.AddAsync(role);

                // Cajero tiene permisos básicos según la matriz
                List<Permission> permissions = await _context.Permissions
                    .Where(p => (p.Module == "Gestión de Caja" && p.Name.Contains("show")) ||
                               (p.Module == "Gestión de Ventas" && p.Name.Contains("show")) ||
                               (p.Module == "Gestión de clientes" && p.Name.Contains("show")))
                    .ToListAsync();

                foreach (Permission permission in permissions)
                {
                    await _context.RolePermissions.AddAsync(new RolePermission
                    {
                        PermissionId = permission.Id,
                        SistemaVentasRoleId = role.Id
                    });
                }

                await _context.SaveChangesAsync();
            }
        }

        private async Task BasicRoleAsync()
        {
            bool exists = await _context.SistemaVentasRoles.AnyAsync(r => r.Name == BASIC_ROLE_NAME);

            if (!exists)
            {
                SistemaVentasRole role = new SistemaVentasRole { Id = Guid.NewGuid(), Name = BASIC_ROLE_NAME };
                await _context.SistemaVentasRoles.AddAsync(role);
                await _context.SaveChangesAsync();
            }
        }

        private async Task ContentManagerRoleAsync()
        {
            bool exists = await _context.SistemaVentasRoles.AnyAsync(r => r.Name == CONTENT_MANAGER_ROLE_NAME);

            if (!exists)
            {
                SistemaVentasRole role = new SistemaVentasRole { Id = Guid.NewGuid(), Name = CONTENT_MANAGER_ROLE_NAME };
                await _context.SistemaVentasRoles.AddAsync(role);

                List<Permission> permissions = await _context.Permissions.Where(p => p.Module == "Secciones" || p.Module == "Blogs")
                                                                         .ToListAsync();
                foreach (Permission permission in permissions)
                {
                    await _context.RolePermissions.AddAsync(new RolePermission { PermissionId = permission.Id, SistemaVentasRoleId = role.Id });
                }

                await _context.SaveChangesAsync();
            }
        }
    }
}