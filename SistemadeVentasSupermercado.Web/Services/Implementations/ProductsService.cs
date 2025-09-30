using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class ProductsService : IProductService
    {
         private readonly DataContext _context;
        private readonly IMapper _mapper;

        public ProductsService(DataContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Response<ProductDTO>> CreateAsync(ProductDTO dto)
        {
            try
            {
                Product product = _mapper.Map<Product>(dto);

                Guid id = Guid.NewGuid();
                product.Id = id;
                
                await _context.Products.AddAsync(product);
                await _context.SaveChangesAsync();
                dto.Id = id;
                return Response<ProductDTO>.Success(dto, "Producto creado con exito");

            }
            catch (Exception ex) 
            {
                return Response<ProductDTO>.Failure(ex);
               
            }

               
        }

        public async Task<Response<object>> DeleteAsync(Guid id)
        {
            try
            {
                // Buscar el producto en la base de datos
                Product? product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

                if (product == null)
                {
                    return Response<object>.Failure($"No se encontró el producto con Id {id}");
                }

                // Eliminar el producto
                _context.Products.Remove(product);
                await _context.SaveChangesAsync();

                return Response<object>.Success(null, "Producto eliminado con éxito");
            }
            catch (Exception ex)
            {
                return Response<object>.Failure(ex);
            }
        }

        public async Task<Response<ProductDTO>> EditAsync(ProductDTO dto)
        {
            try
            {
                // 1. Buscar la entidad 'Product' existente sin seguimiento.
                // Se asume que el contexto de base de datos tiene una colección llamada '_context.Products'
                Product? product = await _context.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == dto.Id);

                // 2. Verificar si el producto fue encontrado.
                if (product is null)
                {
                    // Retorna un error si no se encuentra el ID.
                    return Response<ProductDTO>.Failure($"No existe producto con id: {dto.Id}");
                }

                // 3. Mapear los datos actualizados del DTO a la entidad 'Product'.
                // Aquí se asume que 'Product' es el nombre de la clase de la entidad de la BD
                product = _mapper.Map<Product>(dto);

                // 4. Marcar la entidad como modificada y guardar los cambios.
                _context.Products.Update(product);
                await _context.SaveChangesAsync();

                // 5. Retorna el éxito con el DTO actualizado y un mensaje.
                return Response<ProductDTO>.Success(dto, "Producto actualizado con éxito");
            }
            catch (Exception ex)
            {
                // 6. Manejar cualquier excepción y retornar una respuesta de fallo.
                return Response<ProductDTO>.Failure(ex);
            }
        }


        public async Task<Response<List<ProductDTO>>> GetListAsync()
        {
            try
            {
                List<Product> Products = await _context.Products.ToListAsync();
                List<ProductDTO> List = _mapper.Map<List<ProductDTO>>(Products);

                return  Response<List<ProductDTO>>.Success(List);
                

            }
            catch (Exception ex)
            {
                return Response<List<ProductDTO>>.Failure(ex);

            }
        }

        public async Task<Response<ProductDTO>> GetOneAsync(Guid id)
        {
            try
            {
                // Buscar el producto en la base de datos
                Product? product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);

                if (product == null)
                {
                    return Response<ProductDTO>.Failure($"No se encontró el producto con Id {id}");
                }

                // Mapear la entidad a DTO
                ProductDTO dto = _mapper.Map<ProductDTO>(product);

                return Response<ProductDTO>.Success(dto, "Producto encontrado con éxito");
            }
            catch (Exception ex)
            {
                return Response<ProductDTO>.Failure(ex);
            }
        }

    }
}
