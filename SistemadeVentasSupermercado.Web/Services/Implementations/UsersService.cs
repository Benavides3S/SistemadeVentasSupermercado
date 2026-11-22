using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Core.Mappings;
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
        private readonly ILogger<UsersService> _logger; // ✅ Agregar esto


        public UsersService(DataContext context,
                        SignInManager<User> signInManager,
                        UserManager<User> userManager,
                        IHttpContextAccessor httpContextAccessor,
                        ILogger<UsersService> logger) : base(context) // ✅ Agregar logger

        {
            _context = context;
            _signInManager = signInManager;
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
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
                if (!result.Succeeded)
                {
                    _logger.LogWarning("Errores de Identity: {Errores}",
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
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

        /// <summary>
        /// Crea el usuario con una contraseña temporal generada por el sistema, que se devuelve para
        /// mostrarla UNA vez al administrador. El usuario debe cambiarla en su primer ingreso.
        /// </summary>
        public async Task<Response<TemporaryPasswordDTO>> CreateAsync(UserDTO dto, string requestedByUserName)
        {
            try
            {
                User? existingUser = await _userManager.FindByEmailAsync(dto.Email);
                if (existingUser != null)
                    return Response<TemporaryPasswordDTO>.Failure("El email ya está registrado.");

                if (!Guid.TryParse(dto.SistemaVentasRoleId, out Guid roleId)
                    || !await _context.SistemaVentasRoles.AnyAsync(r => r.Id == roleId))
                    return Response<TemporaryPasswordDTO>.Failure("Seleccione un rol válido.");

                // Quien crea un Admin conoce su contraseña temporal: solo otro Admin puede hacerlo.
                if (await IsSuperAdminRoleAsync(roleId) && !await IsSuperAdminUserAsync(requestedByUserName))
                    return Response<TemporaryPasswordDTO>.Failure(
                        $"Solo un usuario con rol '{Env.SUPER_ADMIN_ROLE_NAME}' puede crear usuarios con ese rol.");

                User user = dto.ToEntity();
                user.Id = Guid.NewGuid().ToString();
                user.UserName = dto.Email;
                user.EmailConfirmed = true;
                user.SistemaVentasRoleId = roleId;
                user.MustChangePassword = true;

                string temporaryPassword = PasswordGenerator.Generate();
                Response<IdentityResult> createResponse = await AddUserAsync(user, temporaryPassword);

                if (!createResponse.IsSuccess || createResponse.Result == null)
                {
                    List<string>? errors = createResponse.Result?.Errors.Select(e => e.Description).ToList();
                    return Response<TemporaryPasswordDTO>.Failure("No se pudo crear el usuario.", errors);
                }

                _logger.LogInformation("Usuario creado: {Email} por {CreadoPor}", dto.Email, requestedByUserName);

                return Response<TemporaryPasswordDTO>.Success(
                    ToTemporaryPasswordDTO(user, temporaryPassword, isNewUser: true), "Usuario creado con éxito");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear el usuario {Email}", dto.Email);
                return Response<TemporaryPasswordDTO>.Failure($"Error al crear usuario: {ex.Message}");
            }
        }

        /// <summary>
        /// Pone una contraseña temporal nueva (para quien olvidó la suya), desbloquea la cuenta y obliga
        /// a cambiarla al entrar. Cierra las sesiones abiertas de ese usuario (cambia su security stamp).
        /// </summary>
        public async Task<Response<TemporaryPasswordDTO>> ResetPasswordAsync(Guid userId, string requestedByUserName)
        {
            try
            {
                User? user = await GetUserByIdAsync(userId);
                if (user is null)
                    return Response<TemporaryPasswordDTO>.Failure("No existe el usuario.");

                // Si no, quien solo tiene "resetPasswords" podría quedarse con la cuenta del dueño.
                if (user.SistemaVentasRole?.Name == Env.SUPER_ADMIN_ROLE_NAME && !await IsSuperAdminUserAsync(requestedByUserName))
                    return Response<TemporaryPasswordDTO>.Failure(
                        $"Solo un usuario con rol '{Env.SUPER_ADMIN_ROLE_NAME}' puede restablecer la contraseña de otro '{Env.SUPER_ADMIN_ROLE_NAME}'.");

                string temporaryPassword = PasswordGenerator.Generate();

                // Se marcan ANTES de ResetPasswordAsync: Identity guarda el usuario completo en una sola
                // operación, así la contraseña nueva y estos cambios quedan juntos (o ninguno).
                user.MustChangePassword = true;
                user.AccessFailedCount = 0;
                user.LockoutEnd = null;

                string token = await _userManager.GeneratePasswordResetTokenAsync(user);
                IdentityResult result = await _userManager.ResetPasswordAsync(user, token, temporaryPassword);

                if (!result.Succeeded)
                    return Response<TemporaryPasswordDTO>.Failure("No se pudo restablecer la contraseña.",
                        result.Errors.Select(e => e.Description).ToList());

                _logger.LogInformation("Contraseña restablecida: {Email} por {RestablecidoPor}", user.Email, requestedByUserName);

                return Response<TemporaryPasswordDTO>.Success(
                    ToTemporaryPasswordDTO(user, temporaryPassword, isNewUser: false), "Contraseña restablecida");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al restablecer la contraseña del usuario {UserId}", userId);
                return Response<TemporaryPasswordDTO>.Failure($"Error al restablecer la contraseña: {ex.Message}");
            }
        }

        /// <summary>
        /// Cambio de contraseña del propio usuario (pide la actual). Apaga MustChangePassword.
        /// Devuelve el usuario para refrescar su sesión (RefreshSignInAsync).
        /// </summary>
        public async Task<Response<User>> ChangePasswordAsync(string userName, ChangePasswordDTO dto)
        {
            try
            {
                User? user = await _userManager.FindByNameAsync(userName);
                if (user is null)
                    return Response<User>.Failure("No existe el usuario.");

                if (dto.NewPassword == dto.CurrentPassword)
                    return Response<User>.Failure("La contraseña nueva debe ser distinta de la actual.");

                // Se guarda junto con la contraseña nueva, en la misma operación de Identity.
                user.MustChangePassword = false;

                IdentityResult result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

                if (!result.Succeeded)
                {
                    List<string> errors = result.Errors.Select(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)
                        ? "La contraseña actual no es correcta."
                        : e.Description).ToList();
                    return Response<User>.Failure(string.Join(" ", errors), errors);
                }

                return Response<User>.Success(user, "Contraseña cambiada con éxito");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar la contraseña de {UserName}", userName);
                return Response<User>.Failure($"Error al cambiar la contraseña: {ex.Message}");
            }
        }

        /// <summary>
        /// Cambiar la contraseña cambia el security stamp del usuario; sin esto, su propia sesión
        /// se cerraría sola al poco tiempo.
        /// </summary>
        public Task RefreshSignInAsync(User user) => _signInManager.RefreshSignInAsync(user);

        public async Task<bool> MustChangePasswordAsync(string userName)
            => await _context.Users.AnyAsync(u => u.UserName == userName && u.MustChangePassword);

        private async Task<bool> IsSuperAdminRoleAsync(Guid roleId)
            => await _context.SistemaVentasRoles.AnyAsync(r => r.Id == roleId && r.Name == Env.SUPER_ADMIN_ROLE_NAME);

        private async Task<bool> IsSuperAdminUserAsync(string userName)
            => await _context.Users.AnyAsync(u => u.UserName == userName
                                                  && u.SistemaVentasRole != null
                                                  && u.SistemaVentasRole.Name == Env.SUPER_ADMIN_ROLE_NAME);

        private static TemporaryPasswordDTO ToTemporaryPasswordDTO(User user, string temporaryPassword, bool isNewUser) => new()
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            TemporaryPassword = temporaryPassword,
            IsNewUser = isNewUser
        };


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

            return await GetPaginationAsync<User, UserDTO>(request, u => u.ToUserDTO(), queryable);
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
            // lockoutOnFailure: true → tras 5 intentos fallidos seguidos la cuenta se bloquea 15 minutos
            // (ver CustomConfiguration.ConfigureIdentityOptions).
            SignInResult result = await _signInManager.PasswordSignInAsync(dto.Email, dto.Password, isPersistent: false, lockoutOnFailure: true);

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

        private async Task<User> GetUserAsync(string? id)
        {
            return await _context.Users.FindAsync(id);
        }
    }
}
