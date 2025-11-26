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
                ..Secciones(),
                ..Roles(),
                ..Users(),
                ..Configuration(),
                ..Caja(),
                ..Ventas()
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
        }
        private List<Permission> Ventas()
        {
            return new List<Permission>
    {
        new Permission { Id = Guid.NewGuid(), Name = "showSales", Description = "Ver ventas", Module = "Ventas" },
        new Permission { Id = Guid.NewGuid(), Name = "createSales", Description = "Registrar ventas", Module = "Ventas" },
        new Permission { Id = Guid.NewGuid(), Name = "cancelSales", Description = "Anular ventas", Module = "Ventas" },
        new Permission { Id = Guid.NewGuid(), Name = "applyDiscounts", Description = "Aplicar descuentos", Module = "Ventas" }
    };
        }
        private List<Permission> Caja()
        {
            return new List<Permission>
    {
        new Permission { Id = Guid.NewGuid(), Name = "showCashRegisters", Description = "Ver cajas", Module = "Caja" },
        new Permission { Id = Guid.NewGuid(), Name = "openCashRegisters", Description = "Abrir caja", Module = "Caja" },
        new Permission { Id = Guid.NewGuid(), Name = "closeCashRegisters", Description = "Cerrar caja", Module = "Caja" }
    };
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

        private List<Permission> Secciones()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "showSections", Description = "Ver secciones", Module = "Secciones" },
                new Permission { Id = Guid.NewGuid(), Name = "createSections", Description = "Crear secciones", Module = "Secciones" },
                new Permission { Id = Guid.NewGuid(), Name = "updateSections", Description = "Editar secciones", Module = "Secciones" },
                new Permission { Id = Guid.NewGuid(), Name = "deleteSections", Description = "Eliminar secciones", Module = "Secciones" }
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
            };
        }
        private List<Permission> Configuration()
        {
            return new List<Permission>
            {
                new Permission { Id = Guid.NewGuid(), Name = "showPaymentMethods", Description = "Ver métodos de pago", Module = "Configuración" },
                new Permission { Id = Guid.NewGuid(), Name = "createPaymentMethods", Description = "Crear métodos de pago", Module = "Configuración" },
                new Permission { Id = Guid.NewGuid(), Name = "updatePaymentMethods", Description = "Editar métodos de pago", Module = "Configuración" },
                new Permission { Id = Guid.NewGuid(), Name = "deletePaymentMethods", Description = "Eliminar métodos de pago", Module = "Configuración" },
                new Permission { Id = Guid.NewGuid(), Name = "showDiscounts", Description = "Ver descuentos", Module = "Configuración" },
                new Permission { Id = Guid.NewGuid(), Name = "createDiscounts", Description = "Crear descuentos", Module = "Configuración" },
                new Permission { Id = Guid.NewGuid(), Name = "updateDiscounts", Description = "Editar descuentos", Module = "Configuración" },
                new Permission { Id = Guid.NewGuid(), Name = "deleteDiscounts", Description = "Eliminar descuentos", Module = "Configuración" }
            };
        }
    }
}

