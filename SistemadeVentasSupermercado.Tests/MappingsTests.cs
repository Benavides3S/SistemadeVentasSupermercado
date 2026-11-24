using SistemadeVentasSupermercado.Web.Core.Mappings;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Tests
{
    /// <summary>
    /// Mapeo manual entidad ↔ DTO (Core/Mappings). No usan base de datos.
    /// Si alguien agrega un campo a una entidad o a un DTO y olvida copiarlo, estas pruebas lo muestran.
    /// </summary>
    public class MappingsTests
    {
        // ---------- Categorías ----------

        [Fact]
        public void Categoria_ida_y_vuelta()
        {
            Category categoria = new Category { Id = Guid.NewGuid(), Name = "Lácteos", IsActive = false };

            CategoryDTO dto = categoria.ToDTO();
            Assert.Equal(categoria.Id, dto.Id);
            Assert.Equal("Lácteos", dto.Name);
            Assert.False(dto.IsActive);

            Category nueva = dto.ToEntity();
            Assert.Equal(categoria.Id, nueva.Id);
            Assert.Equal("Lácteos", nueva.Name);
            Assert.False(nueva.IsActive);
        }

        [Fact]
        public void Categoria_ApplyTo_copia_nombre_y_estado_y_conserva_el_id()
        {
            Guid id = Guid.NewGuid();
            Category categoria = new Category { Id = id, Name = "Viejo", IsActive = true };

            new CategoryDTO { Id = Guid.NewGuid(), Name = "Nuevo", IsActive = false }.ApplyTo(categoria);

            Assert.Equal(id, categoria.Id);
            Assert.Equal("Nuevo", categoria.Name);
            Assert.False(categoria.IsActive);
        }

        // ---------- Proveedores ----------

        [Fact]
        public void Proveedor_ida_y_vuelta()
        {
            Supplier proveedor = new Supplier
            {
                Id = Guid.NewGuid(), Name = "Distribuidora Andina", TaxId = "900123456-7", Phone = "3001234567",
                Email = "ventas@andina.co", Address = "Calle 10 # 5-20", Notes = "Entrega los martes", IsActive = false
            };

            SupplierDTO dto = proveedor.ToDTO();
            Assert.Equal(proveedor.Id, dto.Id);
            Assert.Equal("Distribuidora Andina", dto.Name);
            Assert.Equal("900123456-7", dto.TaxId);
            Assert.Equal("3001234567", dto.Phone);
            Assert.Equal("ventas@andina.co", dto.Email);
            Assert.Equal("Calle 10 # 5-20", dto.Address);
            Assert.Equal("Entrega los martes", dto.Notes);
            Assert.False(dto.IsActive);

            Supplier nuevo = dto.ToEntity();
            Assert.Equal(proveedor.Id, nuevo.Id);
            Assert.Equal("Distribuidora Andina", nuevo.Name);
            Assert.Equal("900123456-7", nuevo.TaxId);
            Assert.Equal("3001234567", nuevo.Phone);
            Assert.Equal("ventas@andina.co", nuevo.Email);
            Assert.Equal("Calle 10 # 5-20", nuevo.Address);
            Assert.Equal("Entrega los martes", nuevo.Notes);
            Assert.False(nuevo.IsActive);
        }

        [Fact]
        public void Proveedor_ApplyTo_copia_los_campos_y_conserva_el_id()
        {
            Guid id = Guid.NewGuid();
            Supplier proveedor = new Supplier { Id = id, Name = "Viejo", TaxId = "1", Phone = "2", Email = "a@b.co", IsActive = true };

            new SupplierDTO
            {
                Id = Guid.NewGuid(), Name = "Nuevo", TaxId = null, Phone = "3", Email = null,
                Address = "Dir", Notes = "Nota", IsActive = false
            }.ApplyTo(proveedor);

            Assert.Equal(id, proveedor.Id);
            Assert.Equal("Nuevo", proveedor.Name);
            Assert.Null(proveedor.TaxId);
            Assert.Equal("3", proveedor.Phone);
            Assert.Null(proveedor.Email);
            Assert.Equal("Dir", proveedor.Address);
            Assert.Equal("Nota", proveedor.Notes);
            Assert.False(proveedor.IsActive);
        }

        // ---------- Clientes ----------

        [Fact]
        public void Cliente_ida_y_vuelta()
        {
            Client cliente = new Client
            {
                Id = Guid.NewGuid(), Name = "Ana", Address = "Calle 1", Phone = "300", Email = "ana@correo.co"
            };

            ClientDTO dto = cliente.ToDTO();
            Assert.Equal(cliente.Id, dto.Id);
            Assert.Equal("Ana", dto.Name);
            Assert.Equal("Calle 1", dto.Address);
            Assert.Equal("300", dto.Phone);
            Assert.Equal("ana@correo.co", dto.Email);

            Client nuevo = dto.ToEntity();
            Assert.Equal(cliente.Id, nuevo.Id);
            Assert.Equal("Ana", nuevo.Name);
            Assert.Equal("Calle 1", nuevo.Address);
            Assert.Equal("300", nuevo.Phone);
            Assert.Equal("ana@correo.co", nuevo.Email);
        }

        [Fact]
        public void Cliente_ApplyTo_copia_los_datos_y_conserva_el_id()
        {
            Guid id = Guid.NewGuid();
            Client cliente = new Client { Id = id, Name = "Ana", Address = "Calle 1", Phone = "300", Email = "ana@correo.co" };

            new ClientDTO { Id = Guid.NewGuid(), Name = "Ana María", Address = null, Phone = "301", Email = "am@correo.co" }
                .ApplyTo(cliente);

            Assert.Equal(id, cliente.Id);
            Assert.Equal("Ana María", cliente.Name);
            Assert.Null(cliente.Address);
            Assert.Equal("301", cliente.Phone);
            Assert.Equal("am@correo.co", cliente.Email);
        }

        // ---------- Productos ----------

        [Fact]
        public void Producto_a_DTO_incluye_el_nombre_de_la_categoria()
        {
            Category categoria = new Category { Id = Guid.NewGuid(), Name = "Quesos" };
            Product producto = new Product
            {
                Id = Guid.NewGuid(), Name = "Queso", Barcode = "7701234567890", Description = "Campesino", Price = 12000.50m, Stock = 3.250m,
                Unit = UnitOfMeasure.Kilogram, CategoryId = categoria.Id, Category = categoria
            };

            ProductDTO dto = producto.ToDTO();

            Assert.Equal(producto.Id, dto.Id);
            Assert.Equal("Queso", dto.Name);
            Assert.Equal("7701234567890", dto.Barcode);
            Assert.Equal("Campesino", dto.Description);
            Assert.Equal(12000.50m, dto.Price);
            Assert.Equal(3.250m, dto.Stock);
            Assert.Equal(UnitOfMeasure.Kilogram, dto.Unit);
            Assert.Equal(categoria.Id, dto.CategoryId);
            Assert.Equal("Quesos", dto.CategoryName);
        }

        [Fact]
        public void Producto_a_DTO_sin_categoria_cargada_deja_el_nombre_vacio()
        {
            Product producto = new Product { Id = Guid.NewGuid(), Name = "Pan", CategoryId = Guid.NewGuid() };

            Assert.Null(producto.ToDTO().CategoryName);
        }

        [Fact]
        public void Producto_nuevo_desde_DTO()
        {
            Guid categoriaId = Guid.NewGuid();
            ProductDTO dto = new ProductDTO
            {
                Id = Guid.NewGuid(), Name = "Arroz", Barcode = "ARROZ-1", Description = "Libra", Price = 2500m, Stock = 10m,
                Unit = UnitOfMeasure.Pound, CategoryId = categoriaId, CategoryName = "se ignora"
            };

            Product producto = dto.ToEntity();

            Assert.Equal(dto.Id, producto.Id);
            Assert.Equal("Arroz", producto.Name);
            Assert.Equal("ARROZ-1", producto.Barcode);
            Assert.Equal("Libra", producto.Description);
            Assert.Equal(2500m, producto.Price);
            Assert.Equal(10m, producto.Stock);
            Assert.Equal(UnitOfMeasure.Pound, producto.Unit);
            Assert.Equal(categoriaId, producto.CategoryId);
            Assert.Null(producto.Category); // nunca se crea una categoría desde el formulario
        }

        [Fact]
        public void Producto_ApplyTo_no_toca_stock_ni_unidad()
        {
            Guid id = Guid.NewGuid();
            Guid categoriaNueva = Guid.NewGuid();
            Product producto = new Product
            {
                Id = id, Name = "Queso", Description = null, Price = 10000m, Stock = 4.5m,
                Unit = UnitOfMeasure.Kilogram, CategoryId = Guid.NewGuid()
            };

            new ProductDTO
            {
                Id = Guid.NewGuid(), Name = "Queso costeño", Barcode = "123", Description = "Nuevo", Price = 11000m,
                Stock = 99m, Unit = UnitOfMeasure.Unit, CategoryId = categoriaNueva
            }.ApplyTo(producto);

            Assert.Equal(id, producto.Id);
            Assert.Equal("Queso costeño", producto.Name);
            Assert.Equal("123", producto.Barcode);
            Assert.Equal("Nuevo", producto.Description);
            Assert.Equal(11000m, producto.Price);
            Assert.Equal(categoriaNueva, producto.CategoryId);
            Assert.Equal(4.5m, producto.Stock);                     // el stock se maneja aparte
            Assert.Equal(UnitOfMeasure.Kilogram, producto.Unit);    // el formulario de edición no trae unidad
        }

        [Fact]
        public void Producto_muestra_el_ultimo_costo_pero_ni_crear_ni_editar_lo_copian()
        {
            // Solo el módulo de compras fija el costo: un formulario manipulado no puede cambiarlo.
            Product conCosto = new Product { Id = Guid.NewGuid(), Name = "Arroz", Price = 2500m, Cost = 1800m };
            Assert.Equal(1800m, conCosto.ToDTO().Cost);
            Assert.Null(new Product { Id = Guid.NewGuid(), Name = "Nuevo" }.ToDTO().Cost);

            ProductDTO manipulado = new ProductDTO { Id = conCosto.Id, Name = "Arroz", Price = 2600m, Cost = 1m, CategoryId = Guid.NewGuid() };

            Assert.Null(manipulado.ToEntity().Cost);

            manipulado.ApplyTo(conCosto);
            Assert.Equal(1800m, conCosto.Cost);
            Assert.Equal(2600m, conCosto.Price);
        }

        // ---------- Usuarios ----------

        [Fact]
        public void Usuario_a_UserDTO()
        {
            SistemaVentasRole rol = new SistemaVentasRole { Id = Guid.NewGuid(), Name = "Cajero" };
            Guid id = Guid.NewGuid();
            User usuario = new User
            {
                Id = id.ToString(), Document = "123", FirstName = "Ana", LastName = "Gómez", PhoneNumber = "300",
                Email = "ana@correo.co", SistemaVentasRoleId = rol.Id, SistemaVentasRole = rol
            };

            UserDTO dto = usuario.ToUserDTO();

            Assert.Equal(id, dto.Id);
            Assert.Equal("123", dto.Document);
            Assert.Equal("Ana", dto.FirstName);
            Assert.Equal("Gómez", dto.LastName);
            Assert.Equal("300", dto.PhoneNumber);
            Assert.Equal("ana@correo.co", dto.Email);
            Assert.Equal(rol.Id.ToString(), dto.SistemaVentasRoleId);
            Assert.Same(rol, dto.SistemaVentasRole);
        }

        [Fact]
        public void Usuario_nuevo_desde_UserDTO_usa_el_correo_como_nombre_de_usuario()
        {
            Guid rolId = Guid.NewGuid();
            UserDTO dto = new UserDTO
            {
                Document = "123", FirstName = "Ana", LastName = "Gómez", PhoneNumber = "300",
                Email = "ana@correo.co", SistemaVentasRoleId = rolId.ToString()
            };

            User usuario = dto.ToEntity();

            Assert.Equal("123", usuario.Document);
            Assert.Equal("Ana", usuario.FirstName);
            Assert.Equal("Gómez", usuario.LastName);
            Assert.Equal("300", usuario.PhoneNumber);
            Assert.Equal("ana@correo.co", usuario.Email);
            Assert.Equal("ana@correo.co", usuario.UserName);
            Assert.Equal(rolId, usuario.SistemaVentasRoleId);
        }

        [Fact]
        public void Usuario_a_AccountUserDTO()
        {
            User usuario = new User
            {
                Id = Guid.NewGuid().ToString(), Document = "123", FirstName = "Ana", LastName = "Gómez",
                PhoneNumber = "300", Email = "ana@correo.co", SistemaVentasRoleId = Guid.NewGuid()
            };

            AccountUserDTO dto = usuario.ToAccountUserDTO();

            Assert.Equal(usuario.Id, dto.Id);
            Assert.Equal("123", dto.Document);
            Assert.Equal("Ana", dto.FirstName);
            Assert.Equal("Gómez", dto.LastName);
            Assert.Equal("300", dto.PhoneNumber);
            Assert.Equal("ana@correo.co", dto.Email);
        }

        // ---------- Roles y permisos ----------

        [Fact]
        public void Rol_ida_y_vuelta()
        {
            SistemaVentasRole rol = new SistemaVentasRole { Id = Guid.NewGuid(), Name = "Supervisor" };

            SistemaVentaRoleDTO dto = rol.ToDTO();
            Assert.Equal(rol.Id, dto.Id);
            Assert.Equal("Supervisor", dto.Name);

            SistemaVentasRole nuevo = dto.ToEntity();
            Assert.Equal(rol.Id, nuevo.Id);
            Assert.Equal("Supervisor", nuevo.Name);
        }

        [Fact]
        public void Permiso_a_DTO()
        {
            Permission permiso = new Permission
            {
                Id = Guid.NewGuid(), Name = "showProducts", Description = "Ver productos", Module = "Productos"
            };

            PermissionDTO dto = permiso.ToDTO();

            Assert.Equal(permiso.Id, dto.Id);
            Assert.Equal("showProducts", dto.Name);
            Assert.Equal("Ver productos", dto.Description);
            Assert.Equal("Productos", dto.Module);
        }
    }
}
