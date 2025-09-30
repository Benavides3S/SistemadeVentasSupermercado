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
    }
}
