using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;

namespace SistemadeVentasSupermercado.Web.Data.Seeders
{
    public class PermissionsSeeder
    {
        private readonly DataContext _context;

        public PermissionsSeeder(DataContext context)
        {
            _context = context;
        }

        public async Task SeedAsync()
        {
            // Lista combinada de permisos de todos los módulos
            List<Permission> permissions = [
                ..Productos(),
                ..Clientes(),
                ..Categorias(),
                ..Proveedores(),
                ..Compras(),
                ..Inventario(),
                ..Ventas(),
                ..Caja(),
                ..Reportes(),
                ..Roles(),
                ..Users()
            ];

            foreach (Permission permission in permissions)
            {
                bool exists = await _context.Permissions.AnyAsync(p => p.Name == permission.Name);

                if (!exists)
                {
                    await _context.Permissions.AddAsync(permission);
                }
            }

            await _context.SaveChangesAsync();

            await RemoveObsoleteAsync();
        }

        /// <summary>Módulos de la plantilla original que el sistema no tiene.</summary>
        public static readonly string[] ObsoleteModules = ["Secciones", "Blogs"];

        /// <summary>
        /// Borra los permisos de módulos que ya no existen (y sus asignaciones a roles).
        /// Así se limpian también bases ya instaladas, sin migración.
        /// </summary>
        public async Task RemoveObsoleteAsync()
        {
            await _context.RolePermissions
                          .Where(rp => ObsoleteModules.Contains(rp.Permission.Module))
                          .ExecuteDeleteAsync();

            int removed = await _context.Permissions
                                        .Where(p => ObsoleteModules.Contains(p.Module))
                                        .ExecuteDeleteAsync();

            if (removed > 0)
                Console.WriteLine($"Se borraron {removed} permisos obsoletos (módulos {string.Join(", ", ObsoleteModules)}).");
        }

        private List<Permission> Productos()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "showProducts", Description = "Ver productos", Module = "Productos" },
                new Permission { Id = Guid.NewGuid(), Name = "createProducts", Description = "Crear productos", Module = "Productos" },
                new Permission { Id = Guid.NewGuid(), Name = "updateProducts", Description = "Editar productos", Module = "Productos" },
                new Permission { Id = Guid.NewGuid(), Name = "deleteProducts", Description = "Eliminar productos", Module = "Productos" }
            };
        }

        private List<Permission> Clientes()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "showClients", Description = "Ver clientes", Module = "Clientes" },
                new Permission { Id = Guid.NewGuid(), Name = "createClients", Description = "Crear clientes", Module = "Clientes" },
                new Permission { Id = Guid.NewGuid(), Name = "updateClients", Description = "Editar clientes", Module = "Clientes" },
                new Permission { Id = Guid.NewGuid(), Name = "deleteClients", Description = "Eliminar clientes", Module = "Clientes" }
            };
        }
        private List<Permission> Ventas()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "createSales", Description = "Usar el punto de venta", Module = "Ventas" },
                new Permission { Id = Guid.NewGuid(), Name = "showSales", Description = "Ver historial y comprobantes de ventas", Module = "Ventas" },
                new Permission { Id = Guid.NewGuid(), Name = "cancelSales", Description = "Anular ventas (con el turno de caja abierto)", Module = "Ventas" },
                new Permission { Id = Guid.NewGuid(), Name = "createReturns", Description = "Hacer devoluciones de ventas", Module = "Ventas" }
            };
        }

        private List<Permission> Caja()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "manageCashSessions", Description = "Abrir y cerrar su propia caja", Module = "Caja" },
                new Permission { Id = Guid.NewGuid(), Name = "showCashSessions", Description = "Ver todos los turnos de caja y cerrar cajas de otros", Module = "Caja" }
            };
        }

        private List<Permission> Reportes()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "showReports", Description = "Ver y exportar reportes de ventas", Module = "Reportes" },
                new Permission { Id = Guid.NewGuid(), Name = "showCashierReport", Description = "Ver y exportar el reporte por cajero (datos de cada empleado)", Module = "Reportes" }
            };
        }

        private List<Permission> Categorias()
        {
            return new List<Permission>
    {
        new Permission { Id = Guid.NewGuid(), Name = "showCategories", Description = "Ver categorías", Module = "Categorias" },
        new Permission { Id = Guid.NewGuid(), Name = "createCategories", Description = "Crear categorías", Module = "Categorias" },
        new Permission { Id = Guid.NewGuid(), Name = "updateCategories", Description = "Editar categorías", Module = "Categorias" },
        new Permission { Id = Guid.NewGuid(), Name = "deleteCategories", Description = "Eliminar categorías", Module = "Categorias" }
    };
        }

        private List<Permission> Proveedores()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "showSuppliers", Description = "Ver proveedores", Module = "Proveedores" },
                new Permission { Id = Guid.NewGuid(), Name = "createSuppliers", Description = "Crear proveedores", Module = "Proveedores" },
                new Permission { Id = Guid.NewGuid(), Name = "updateSuppliers", Description = "Editar proveedores", Module = "Proveedores" },
                new Permission { Id = Guid.NewGuid(), Name = "deleteSuppliers", Description = "Eliminar proveedores", Module = "Proveedores" }
            };
        }

        private List<Permission> Compras()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "showPurchases", Description = "Ver historial y detalle de compras", Module = "Compras" },
                new Permission { Id = Guid.NewGuid(), Name = "createPurchases", Description = "Registrar compras a proveedores (suma el stock)", Module = "Compras" },
                new Permission { Id = Guid.NewGuid(), Name = "cancelPurchases", Description = "Anular compras (resta el stock si todavía alcanza)", Module = "Compras" }
            };
        }

        private List<Permission> Inventario()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "showInventory", Description = "Ver el historial de movimientos de inventario", Module = "Inventario" },
                new Permission { Id = Guid.NewGuid(), Name = "adjustInventory", Description = "Ajustar el stock (mermas, pérdidas, conteo físico)", Module = "Inventario" }
            };
        }

        private List<Permission> Roles()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "showRoles", Description = "Ver roles", Module = "Roles" },
                new Permission { Id = Guid.NewGuid(), Name = "createRoles", Description = "Crear roles", Module = "Roles" },
                new Permission { Id = Guid.NewGuid(), Name = "updateRoles", Description = "Editar roles", Module = "Roles" },
                new Permission { Id = Guid.NewGuid(), Name = "deleteRoles", Description = "Eliminar roles", Module = "Roles" }
            };
        }
        private List<Permission> Users()
        {
            return new List<Permission>
            {
                new Permission { Name = "showUsers", Description = "Ver Usuarios", Module = "Usuarios"},
                new Permission { Name = "createUsers", Description = "Crear Usuarios", Module = "Usuarios"},
                new Permission { Name = "updateUsers", Description = "Editar Usuarios", Module = "Usuarios"},
                new Permission { Name = "deleteUsers", Description = "Eliminar Usuarios", Module = "Usuarios"},
                new Permission { Name = "resetPasswords", Description = "Restablecer contraseñas", Module = "Usuarios"},
                new Permission { Name = "deactivateUsers", Description = "Desactivar y reactivar usuarios", Module = "Usuarios"},
                new Permission { Name = "showUserActivity", Description = "Ver la actividad de usuarios", Module = "Usuarios"},
            };
        }
    }
}

