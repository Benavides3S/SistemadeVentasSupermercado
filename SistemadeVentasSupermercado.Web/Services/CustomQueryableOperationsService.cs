using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Abstractions;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services
{
    public class CustomQueryableOperationsService
    {
        private readonly DataContext _context;
        private readonly IMapper _mapper;

        public CustomQueryableOperationsService(DataContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Response<TDTO>> CreateAsync<TEntity, TDTO>(TDTO dto) where TEntity : IId
        {
            try
            {
                TEntity entity = _mapper.Map<TEntity>(dto);

                Guid id = Guid.NewGuid();

                entity.Id = id;
                await _context.AddAsync(entity);
                await _context.SaveChangesAsync();

                //dto.Id = id;
                return Response<TDTO>.Success(dto, "Registro creado con éxito");

            }
            catch (Exception ex)
            {
                return Response<TDTO>.Failure(ex);
            }
        }


        public async Task<Response<object>> DeleteAsync<TEntity>(Guid id) where TEntity : IId
        {
            try
            {

                TEntity? entity = await _context.Set<TEntity>()
                                                    .FirstOrDefaultAsync(s => s.Id == id);


                if (entity is null)
                {
                    return Response<object>.Failure($"No existe registro con Id: {id}");
                }

                // Eliminar el producto
                _context.Remove(entity);
                await _context.SaveChangesAsync();

                return Response<object>.Success("Registro eliminado con éxito");
            }
            catch (Exception ex)
            {
                return Response<object>.Failure(ex);
            }
        }
    }
}
