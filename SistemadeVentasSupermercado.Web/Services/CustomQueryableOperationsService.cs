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


        public async Task<Response<object>> DeleteAsync<TEntity>(Guid id) where TEntity : class, IId
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
        public async Task<Response<TDTO>> EditAsync<TEntity, TDTO>(TDTO dto, Guid id) where TEntity : IId
        {
            try
            {
                
                TEntity entity = _mapper.Map<TEntity>(dto);

                entity.Id = id;

                
                _context.Entry(entity).State = EntityState.Modified;

                await _context.SaveChangesAsync();

                return Response<TDTO>.Success(dto, "Registro actualizado con éxito");
            }
            catch (Exception ex)
            {
                
                return Response<TDTO>.Failure(ex);
            }
        }

        public async Task<Response<TDTO>> GetOneAsync<TEntity, TDTO>(Guid id) where TEntity : class, IId
        {
            try
            {
               
                TEntity? entity = await _context.Set<TEntity>()
                    .FirstOrDefaultAsync(s => s.Id == id);

                
                if (entity is null)
                {
                    
                    return Response<TDTO>.Failure($"No existe registro con id: {id}");
                }

                
                TDTO dto = _mapper.Map<TDTO>(entity);

                
                return Response<TDTO>.Success(dto, "Sección obtenida con éxito");
            }
            catch (Exception ex)
            {
              
                return Response<TDTO>.Failure(ex);
            }
        }

        public async Task<Response<List<TDTO>>> GetCompleteListAsync<TEntity, TDTO>(IQueryable<TEntity>? query = null)
     where TEntity : class, IId
        {
            try
            {
                // 1. Inicializar la consulta si no se proporcionó una
                if (query is null)
                {
                    query = _context.Set<TEntity>();
                }

                // 2. Ejecutar la consulta y obtener la lista de entidades
                List<TEntity> list = await query.ToListAsync();

                // 3. Mapear la lista de entidades a una lista de DTOs
                List<TDTO> dtoList = _mapper.Map<List<TDTO>>(list);

                // 4. Devolver una respuesta de éxito con la lista de DTOs
                return Response<List<TDTO>>.Success(dtoList);
            }
            catch (Exception ex)
            {
                // Manejar cualquier excepción y devolver un fallo
                return Response<List<TDTO>>.Failure(ex);
            }
        }
    }
}
