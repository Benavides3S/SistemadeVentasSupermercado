using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Core.Mappings
{
    /// <summary>
    /// Copia de datos entre entidades y DTOs del catálogo (categorías, productos y clientes), escrita a mano.
    /// Cada método nombra cada campo que copia: nada se copia "por magia".
    ///  - ToDTO: de la base de datos a la pantalla.
    ///  - ToEntity: para CREAR un registro nuevo.
    ///  - ApplyTo: para EDITAR; cambia solo los campos permitidos de una entidad ya cargada de la base.
    ///    Lo que no aparece aquí se conserva tal como está en la base.
    /// </summary>
    public static class CatalogMappings
    {
        // ---------- Categorías ----------

        public static CategoryDTO ToDTO(this Category category) => new CategoryDTO
        {
            Id = category.Id,
            Name = category.Name,
            IsActive = category.IsActive
        };

        public static Category ToEntity(this CategoryDTO dto) => new Category
        {
            Id = dto.Id,
            Name = dto.Name,
            IsActive = dto.IsActive
        };

        public static void ApplyTo(this CategoryDTO dto, Category category)
        {
            category.Name = dto.Name;
            category.IsActive = dto.IsActive;
        }

        // ---------- Proveedores ----------

        public static SupplierDTO ToDTO(this Supplier supplier) => new SupplierDTO
        {
            Id = supplier.Id,
            Name = supplier.Name,
            TaxId = supplier.TaxId,
            Phone = supplier.Phone,
            Email = supplier.Email,
            Address = supplier.Address,
            Notes = supplier.Notes,
            IsActive = supplier.IsActive
        };

        public static Supplier ToEntity(this SupplierDTO dto) => new Supplier
        {
            Id = dto.Id,
            Name = dto.Name,
            TaxId = dto.TaxId,
            Phone = dto.Phone,
            Email = dto.Email,
            Address = dto.Address,
            Notes = dto.Notes,
            IsActive = dto.IsActive
        };

        public static void ApplyTo(this SupplierDTO dto, Supplier supplier)
        {
            supplier.Name = dto.Name;
            supplier.TaxId = dto.TaxId;
            supplier.Phone = dto.Phone;
            supplier.Email = dto.Email;
            supplier.Address = dto.Address;
            supplier.Notes = dto.Notes;
            supplier.IsActive = dto.IsActive;
        }

        // ---------- Clientes ----------

        public static ClientDTO ToDTO(this Client client) => new ClientDTO
        {
            Id = client.Id,
            Name = client.Name,
            Address = client.Address,
            Phone = client.Phone,
            Email = client.Email
        };

        public static Client ToEntity(this ClientDTO dto) => new Client
        {
            Id = dto.Id,
            Name = dto.Name,
            Address = dto.Address,
            Phone = dto.Phone,
            Email = dto.Email
        };

        public static void ApplyTo(this ClientDTO dto, Client client)
        {
            client.Name = dto.Name;
            client.Address = dto.Address;
            client.Phone = dto.Phone;
            client.Email = dto.Email;
        }

        // ---------- Productos ----------

        /// <summary>Para leer un producto con su categoría, incluir Category en la consulta.</summary>
        public static ProductDTO ToDTO(this Product product) => new ProductDTO
        {
            Id = product.Id,
            Name = product.Name,
            Barcode = product.Barcode,
            Description = product.Description,
            Price = product.Price,
            Stock = product.Stock,
            StockOriginal = product.Stock,
            Unit = product.Unit,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name
        };

        public static Product ToEntity(this ProductDTO dto) => new Product
        {
            Id = dto.Id,
            Name = dto.Name,
            Barcode = dto.Barcode,
            Description = dto.Description,
            Price = dto.Price,
            Stock = dto.Stock,
            Unit = dto.Unit,
            CategoryId = dto.CategoryId ?? Guid.Empty
        };

        /// <summary>
        /// NO copia:
        ///  - Stock: lo maneja ProductsService.EditAsync aparte, para no pisar las ventas de los cajeros.
        ///  - Unit: el formulario de edición no tiene ese campo; copiarlo convertía en "Unidad"
        ///    los productos por peso cada vez que se editaban.
        /// </summary>
        public static void ApplyTo(this ProductDTO dto, Product product)
        {
            product.Name = dto.Name;
            product.Barcode = dto.Barcode;
            product.Description = dto.Description;
            product.Price = dto.Price;
            if (dto.CategoryId.HasValue)
                product.CategoryId = dto.CategoryId.Value;
        }
    }
}
