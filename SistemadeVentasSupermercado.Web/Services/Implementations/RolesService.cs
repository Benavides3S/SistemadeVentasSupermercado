using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Newtonsoft.Json;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class RolesService : CustomQueryableOperationsService, IRolesService
    {
        private readonly DataContext _context;
        private readonly IMapper _mapper;

        public RolesService(DataContext context, IMapper mapper) : base(context, mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<Response<SistemaVentaRoleDTO>> CreateAsync(SistemaVentaRoleDTO dto)
        {
            using (IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    Guid newRoleId = Guid.NewGuid();

                    // Role
                    SistemaVentasRole role = _mapper.Map<SistemaVentasRole>(dto);

                    await _context.SistemaVentasRoles.AddAsync(role);

                    await _context.SaveChangesAsync();

                    // Permissions
                    List<Guid> permissionIds = new();

                    if (!string.IsNullOrEmpty(dto.PermissionIds))
                    {
                        permissionIds = JsonConvert.DeserializeObject<List<Guid>>(dto.PermissionIds);
                    }

                    foreach (Guid permissionId in permissionIds)
                    {
                        RolePermission rolePermission = new RolePermission
                        {
                            SistemaVentasRoleId = role.Id,
                            PermissionId = permissionId
                        };

                        await _context.RolePermissions.AddAsync(rolePermission);
                    }

                    await _context.SaveChangesAsync();

                    transaction.Commit();

                    return Response<SistemaVentaRoleDTO>.Success(dto, "Rol creado con éxito");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return Response<SistemaVentaRoleDTO>.Failure(ex);
                }
            }
        }

        public async Task<Response<object>> DeleteAsync(Guid id)
        {
            if (_context.Users.Any(u => u.SistemaVentasRoleId == id))
            {
                return Response<object>.Failure("No puede eliminar el rol ya que existen usuarios que lo contienen");
            }
            return await DeleteAsync<SistemaVentasRole>(id);
        }

        public async Task<Response<SistemaVentaRoleDTO>> EditAsync(SistemaVentaRoleDTO dto)
        {
            try
            {
                if (dto.Name == Env.SUPER_ADMIN_ROLE_NAME)
                {
                    return Response<SistemaVentaRoleDTO>.Failure($"El rol '{Env.SUPER_ADMIN_ROLE_NAME}' no puede ser editado");
                }

                // Role
                SistemaVentasRole role = _mapper.Map<SistemaVentasRole>(dto);
                _context.SistemaVentasRoles.Update(role);

                // Permissions
                List<Guid> permissionIds = new();

                if (!string.IsNullOrEmpty(dto.PermissionIds))
                {
                    permissionIds = JsonConvert.DeserializeObject<List<Guid>>(dto.PermissionIds);
                }

                // Delete old
                List<RolePermission> oldRolePermissions = await _context.RolePermissions.Where(rp => rp.SistemaVentasRoleId == dto.Id).ToListAsync();
                _context.RolePermissions.RemoveRange(oldRolePermissions);

                // Create new ones
                foreach (Guid permissionId in permissionIds)
                {
                    RolePermission rolePermission = new RolePermission
                    {
                        SistemaVentasRoleId = role.Id,
                        PermissionId = permissionId
                    };

                    await _context.RolePermissions.AddAsync(rolePermission);
                }

                await _context.SaveChangesAsync();

                return Response<SistemaVentaRoleDTO>.Success(dto, "Rol actualizado con éxito");
            }
            catch (Exception ex)
            {
                return Response<SistemaVentaRoleDTO>.Failure(ex);
            }
        }

        public async Task<Response<SistemaVentaRoleDTO>> GetOneAsync(Guid id)
        {
            Response<SistemaVentaRoleDTO> response = await GetOneAsync<SistemaVentasRole, SistemaVentaRoleDTO>(id);

            if (!response.IsSuccess)
            {
                return response;
            }

            SistemaVentaRoleDTO dto = response.Result;

            List<PermissionsForRoleDTO> permissions = await _context.Permissions.Select(p => new PermissionsForRoleDTO
            {
                Id = p.Id,
                Description = p.Description,
                Module = p.Module,
                Selected = _context.RolePermissions.Any(rp => rp.PermissionId == p.Id && rp.SistemaVentasRoleId == dto.Id)
            }).ToListAsync();

            dto.Permissions = permissions;

            return Response<SistemaVentaRoleDTO>.Success(dto, "Rol obtenido con éxito");
        }

        public async Task<Response<PaginationResponse<SistemaVentaRoleDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<SistemaVentasRole> query = _context.SistemaVentasRoles.AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.Filter))
            {
                query = query.Where(r => r.Name.ToLower().Contains(request.Filter.ToLower()));
            }

            return await GetPaginationAsync<SistemaVentasRole, SistemaVentaRoleDTO>(request, query);
        }

        public async Task<Response<List<PermissionsForRoleDTO>>> GetPermissionsAsync()
        {
            Response<List<PermissionDTO>> permissionsResponse = await GetCompleteListAsync<Permission, PermissionDTO>();

            if (!permissionsResponse.IsSuccess)
            {
                return Response<List<PermissionsForRoleDTO>>.Failure(permissionsResponse.Message);
            }

            List<PermissionsForRoleDTO> dto = permissionsResponse.Result.Select(p => new PermissionsForRoleDTO
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Module = p.Module,
                Selected = false
            }).ToList();

            return Response<List<PermissionsForRoleDTO>>.Success(dto);
        }
    }
}
