using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Pagination;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;
using System.Security.Claims;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    // TODO: Mejorar mensajes de error
    public class UsersService : CustomQueryableOperationsService, IUsersService
    {
        private readonly DataContext _context;
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMapper _mapper;
        private readonly ILogger<UsersService> _logger; // ✅ Agregar esto


        public UsersService(DataContext context,
                        SignInManager<User> signInManager,
                        UserManager<User> userManager,
                        IHttpContextAccessor httpContextAccessor,
                        IMapper mapper,
                        ILogger<UsersService> logger) : base(context, mapper) // ✅ Agregar logger

        {
            _context = context;
            _signInManager = signInManager;
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<Response<IdentityResult>> AddUserAsync(User user, string password)
        {
            try
            {
                _logger.LogInformation("Creando usuario en Identity: {Email}", user.Email);

                IdentityResult result = await _userManager.CreateAsync(user, password);

                _logger.LogInformation("Resultado Identity: {Succeeded}, Errores: {ErrorCount}",
                    result.Succeeded, result.Errors.Count());

                return new Response<IdentityResult>
                {
                    Result = result,
                    IsSuccess = result.Succeeded,
                    Message = result.Succeeded ? "Usuario creado exitosamente" : string.Join(", ", result.Errors.Select(e => e.Description))
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción en AddUserAsync");
                return new Response<IdentityResult>
                {
                    Result = null,
                    IsSuccess = false,
                    Message = $"Excepción en AddUserAsync: {ex.Message}"
                };
            }
        }

        public async Task<Response<IdentityResult>> ConfirmUserAsync(User user, string token)
        {
            IdentityResult result = await _userManager.ConfirmEmailAsync(user, token);

            return new Response<IdentityResult>
            {
                Result = result,
                IsSuccess = result.Succeeded,
            };
        }

        public async Task<Response<UserDTO>> CreateAsync(UserDTO dto)
        {
            try
            {
                _logger.LogInformation("=== INICIANDO CREACIÓN ===");
                _logger.LogInformation($"Email: {dto.Email}, Rol: {dto.SistemaVentasRoleId}");

                // Verificar si el email ya existe
                var existingUser = await _userManager.FindByEmailAsync(dto.Email);
                if (existingUser != null)
                {
                    _logger.LogWarning("Email ya registrado: {Email}", dto.Email);
                    return Response<UserDTO>.Failure("El email ya está registrado.");
                }

                User user = _mapper.Map<User>(dto);
                user.Id = Guid.NewGuid().ToString();
                user.UserName = dto.Email;
                user.EmailConfirmed = true;

                if (!string.IsNullOrWhiteSpace(dto.SistemaVentasRoleId) && Guid.TryParse(dto.SistemaVentasRoleId, out Guid roleId))
                {
                    user.SistemaVentasRoleId = roleId;
                    _logger.LogInformation("Rol asignado: {RoleId}", roleId);
                }

                Response<IdentityResult> createResponse = await AddUserAsync(user, dto.Document);

                _logger.LogInformation("Create Response - Success: {Success}", createResponse.IsSuccess);

                if (!createResponse.IsSuccess || createResponse.Result == null)
                {
                    var errors = createResponse.Result?.Errors.Select(e => e.Description).ToList();
                    _logger.LogError("ERRORES IDENTITY al crear usuario:");
                    foreach (var error in errors ?? new List<string>())
                    {
                        _logger.LogError(" - {Error}", error);
                    }

                    return Response<UserDTO>.Failure("No se pudo crear el usuario en el sistema de autenticación.", errors);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("✅ USUARIO CREADO EXITOSAMENTE: {Email}", dto.Email);

                return Response<UserDTO>.Success(_mapper.Map<UserDTO>(user), "Usuario creado con éxito");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 EXCEPCIÓN en CreateAsync: {Message}", ex.Message);
                return Response<UserDTO>.Failure($"Error al crear usuario: {ex.Message}");
            }
        }


        public bool CurrentUserIsAuthenticaded()
        {
            ClaimsPrincipal? user = _httpContextAccessor.HttpContext?.User;
            return user?.Identity is not null && user.Identity.IsAuthenticated;
        }

        public async Task<bool> CurrentUserIsAuthorizedAsync(string permission, string module)
        {
            try
            {
                _logger.LogInformation($"Verificando permiso: {module}.{permission}");

                ClaimsPrincipal? claimsUser = _httpContextAccessor.HttpContext?.User;

                if (claimsUser?.Identity?.IsAuthenticated != true)
                {
                    _logger.LogWarning("Usuario no autenticado");
                    return false;
                }

                string userName = claimsUser.Identity.Name;
                User? user = await GetUserByEmailAsync(userName);

                if (user is null)
                {
                    _logger.LogWarning($"Usuario no encontrado: {userName}");
                    return false;
                }

                _logger.LogInformation($"Usuario: {userName}, Rol: {user.SistemaVentasRole?.Name ?? "Sin rol"}");

                // Super admin tiene acceso total
                if (user.SistemaVentasRole?.Name == Env.SUPER_ADMIN_ROLE_NAME)
                {
                    _logger.LogInformation("Acceso concedido - Super Admin");
                    return true;
                }

                // Verificar si el rol tiene permisos
                if (user.SistemaVentasRoleId == Guid.Empty)
                {
                    _logger.LogWarning("Usuario sin rol asignado");
                    return false;
                }

                // Verificar permisos directamente desde RolePermissions
                bool hasPermission = await _context.RolePermissions
                    .Include(rp => rp.Permission)
                    .Include(rp => rp.SistemaVentasRole)
                    .Where(rp => rp.SistemaVentasRoleId == user.SistemaVentasRoleId)
                    .AnyAsync(rp => rp.Permission.Module == module && rp.Permission.Name == permission);

                _logger.LogInformation($"Permiso {module}.{permission}: {(hasPermission ? "CONCEDIDO" : "DENEGADO")}");

                return hasPermission;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verificando autorización");
                return false;
            }
        }

        //public Task<Response<object>> DeleteAsync(Guid id)
        //{
        //    throw new NotImplementedException();
        //}

        public async Task<Response<UserDTO>> EditAsync(UserDTO dto)
        {
            try
            {
                User? user = await GetUserAsync(dto.Id?.ToString());

                if (user == null)
                {
                    return Response<UserDTO>.Failure("No existe usuario.");
                }

                user.PhoneNumber = dto.PhoneNumber;
                user.Document = dto.Document;
                user.FirstName = dto.FirstName;
                user.LastName = dto.LastName;

                // dto.SistemaVentasRoleId is expected to be a string representation of a Guid
                if (!string.IsNullOrWhiteSpace(dto.SistemaVentasRoleId) && Guid.TryParse(dto.SistemaVentasRoleId, out var roleGuid))
                {
                    user.SistemaVentasRoleId = roleGuid;
                }

                _context.Users.Update(user);

                await _context.SaveChangesAsync();

                return Response<UserDTO>.Success(dto, "Usuario actualizado con éxito");
            }
            catch (Exception ex)
            {
                return Response<UserDTO>.Failure(ex);
            }
        }

        public async Task<Response<string>> GenerateConfirmationTokenAsync(User user)
        {
            string result = await _userManager.GenerateEmailConfirmationTokenAsync(user);

            return Response<string>.Success(result);
        }

        //public Task<Response<UserDTO>> GetOneAsync(Guid id)
        //{
        //    throw new NotImplementedException();
        //}

        public async Task<Response<PaginationResponse<UserDTO>>> GetPaginatedListAsync(PaginationRequest request)
        {
            IQueryable<User> queryable = _context.Users.AsQueryable();

            if (!string.IsNullOrEmpty(request.Filter))
            {
                string f = request.Filter.ToLowerInvariant();

                queryable = queryable.Where(u =>
                       (!string.IsNullOrEmpty(u.FirstName) && u.FirstName.ToLower().Contains(f))
                    || (!string.IsNullOrEmpty(u.LastName) && u.LastName.ToLower().Contains(f))
                    || (!string.IsNullOrEmpty(u.Document) && u.Document.ToLower().Contains(f))
                    || (!string.IsNullOrEmpty(u.Email) && u.Email.ToLower().Contains(f))
                    || (!string.IsNullOrEmpty(u.PhoneNumber) && u.PhoneNumber.ToLower().Contains(f))
                );
            }

            return await GetPaginationAsync<User, UserDTO>(request, queryable);
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _context.Users.Include(u => u.SistemaVentasRole)
                                       .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            return await _context.Users.Include(u => u.SistemaVentasRole)
                                       .FirstOrDefaultAsync(u => u.Id == id.ToString());
        }

        public async Task<Response<SignInResult>> LoginAsync(LoginDTO dto)
        {
            SignInResult result = await _signInManager.PasswordSignInAsync(dto.Email, dto.Password, false, false);

            return new Response<SignInResult>
            {
                Result = result,
                IsSuccess = result.Succeeded,
            };
        }

        public async Task LogoutAsync()
        {
            await _signInManager.SignOutAsync();
        }

        public async Task<Response<AccountUserDTO>> UpdateUserAsync(AccountUserDTO dto)
        {
            try
            {
                User user = await GetUserAsync(dto.Id);
                user.PhoneNumber = dto.PhoneNumber;
                user.Document = dto.Document;
                user.FirstName = dto.FirstName;
                user.LastName = dto.LastName;

                _context.Users.Update(user);

                await _context.SaveChangesAsync();

                return Response<AccountUserDTO>.Success(dto, "Datos actualizados con éxito");
            }
            catch (Exception ex)
            {
                return Response<AccountUserDTO>.Failure(ex);
            }
        }
        public async Task LogCurrentUserPermissions()
        {
            try
            {
                ClaimsPrincipal? claimsUser = _httpContextAccessor.HttpContext?.User;

                if (claimsUser?.Identity?.IsAuthenticated == true)
                {
                    string userName = claimsUser.Identity.Name;
                    User? user = await GetUserByEmailAsync(userName);

                    if (user != null)
                    {
                        _logger.LogInformation($"=== PERMISOS DEL USUARIO ACTUAL ===");
                        _logger.LogInformation($"Usuario: {userName}");
                        _logger.LogInformation($"Rol: {user.SistemaVentasRole?.Name} (ID: {user.SistemaVentasRoleId})");

                        var permissions = await _context.RolePermissions
                            .Include(rp => rp.Permission)
                            .Where(rp => rp.SistemaVentasRoleId == user.SistemaVentasRoleId)
                            .Select(rp => new { rp.Permission.Module, rp.Permission.Name, rp.Permission.Description })
                            .ToListAsync();

                        _logger.LogInformation($"Permisos asignados ({permissions.Count}):");
                        foreach (var perm in permissions)
                        {
                            _logger.LogInformation($" - {perm.Module}.{perm.Name}: {perm.Description}");
                        }

                        // También mostrar todos los permisos disponibles para comparar
                        var allPermissions = await _context.Permissions
                            .Select(p => new { p.Module, p.Name, p.Description })
                            .ToListAsync();

                        _logger.LogInformation($"=== TODOS LOS PERMISOS DISPONIBLES ({allPermissions.Count}) ===");
                        foreach (var perm in allPermissions)
                        {
                            _logger.LogInformation($" - {perm.Module}.{perm.Name}: {perm.Description}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loggeando permisos del usuario");
            }
        }
        public async Task<Response<User>> GetCurrentUserAsync()
        {
            try
            {
                ClaimsPrincipal? claimsUser = _httpContextAccessor.HttpContext?.User;

                if (claimsUser?.Identity?.IsAuthenticated != true)
                {
                    return Response<User>.Failure("Usuario no autenticado");
                }

                string userName = claimsUser.Identity.Name;
                User? user = await GetUserByEmailAsync(userName);

                if (user is null)
                {
                    return Response<User>.Failure($"Usuario no encontrado: {userName}");
                }

                return Response<User>.Success(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo usuario actual");
                return Response<User>.Failure($"Error obteniendo usuario actual: {ex.Message}");
            }
        }

        private async Task<User> GetUserAsync(string? id)
        {
            return await _context.Users.FindAsync(id);
        }
    }
}
