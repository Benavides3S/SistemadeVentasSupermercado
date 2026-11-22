using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Abstractions;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;

namespace SistemadeVentasSupermercado.Web.Services
{
    /// <summary>
    /// Operaciones comunes (crear, editar, borrar, leer, paginar). Cada servicio indica cómo copiar
    /// los datos entre entidad y DTO con los métodos de Core/Mappings (por ejemplo dto.ToEntity()).
    /// </summary>
    public class CustomQueryableOperationsService
    {
        private readonly DataContext _context;

        public CustomQueryableOperationsService(DataContext context)
        {
            _context = context;
        }

        public async Task<Response<TDTO>> CreateAsync<TEntity, TDTO>(TDTO dto, Func<TDTO, TEntity> toEntity) where TEntity : IId
        {
            try
            {
                TEntity entity = toEntity(dto);

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
        /// <summary>
        /// Carga el registro de la base y le aplica SOLO los campos que copia <paramref name="applyChanges"/>.
        /// Así EF guarda únicamente las columnas que cambiaron, y lo que no está en el formulario se conserva.
        /// (Antes se armaba una entidad nueva con el formulario y se guardaba completa, pisando todo.)
        /// </summary>
        public async Task<Response<TDTO>> EditAsync<TEntity, TDTO>(TDTO dto, Guid id, Action<TDTO, TEntity> applyChanges)
            where TEntity : class, IId
        {
            try
            {
                TEntity? entity = await _context.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id);

                if (entity is null)
                {
                    return Response<TDTO>.Failure($"No existe registro con id: {id}");
                }

                applyChanges(dto, entity);

                await _context.SaveChangesAsync();

                return Response<TDTO>.Success(dto, "Registro actualizado con éxito");
            }
            catch (Exception ex)
            {
                
                return Response<TDTO>.Failure(ex);
            }
        }

        public async Task<Response<TDTO>> GetOneAsync<TEntity, TDTO>(Guid id, Func<TEntity, TDTO> toDto, IQueryable<TEntity>? query = null) where TEntity : class, IId
        {
            try
            {

                if (query is null)
                {
                    query = _context.Set<TEntity>();
                }

                TEntity? entity = await query.FirstOrDefaultAsync(s => s.Id == id);


                if (entity is null)
                {
                    
                    return Response<TDTO>.Failure($"No existe registro con id: {id}");
                }

                
                TDTO dto = toDto(entity);

                
                return Response<TDTO>.Success(dto, "Registro obtenido con éxito");
            }
            catch (Exception ex)
            {
              
                return Response<TDTO>.Failure(ex);
            }
        }

        public async Task<Response<List<TDTO>>> GetCompleteListAsync<TEntity, TDTO>(Func<TEntity, TDTO> toDto, IQueryable<TEntity>? query = null)
     where TEntity : class, IId
        {
            try
            {
              
                if (query is null)
                {
                    query = _context.Set<TEntity>();
                }

                
                List<TEntity> list = await query.ToListAsync();

                
                List<TDTO> dtoList = list.Select(toDto).ToList();

               
                return Response<List<TDTO>>.Success(dtoList);
            }
            catch (Exception ex)
            {
               
                return Response<List<TDTO>>.Failure(ex);
            }
        }



        public async Task<Response<PaginationResponse<TDTO>>> GetPaginationAsync<TEntity, TDTO>(PaginationRequest request, Func<TEntity, TDTO> toDto, IQueryable<TEntity>? query = null)
        where TEntity : class
        where TDTO : class
        {
            try
            {
                if (query is null)
                {
                    query = _context.Set<TEntity>();
                }

                PagedList<TEntity> list = await PagedList<TEntity>.ToPagedListAsync(query, request);

                PaginationResponse<TDTO> response = new PaginationResponse<TDTO>
                {
                    List = new PagedList<TDTO>(list.Select(toDto).ToList(), list.TotalCount, list.CurrentPage, list.RecordsPerPage),
                    TotalCount = list.TotalCount,
                    RecordsPerPage = list.RecordsPerPage,
                    CurrentPage = list.CurrentPage,
                    TotalPages = list.TotalPages,
                    Filter = request.Filter
                };

                return Response<PaginationResponse<TDTO>>.Success(response);
            }
            catch (Exception ex)
            {
                return Response<PaginationResponse<TDTO>>.Failure(ex);
            }
        }
    }
}
