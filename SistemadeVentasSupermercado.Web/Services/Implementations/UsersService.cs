using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        private readonly IUserActivityRecorder _activity;


        public UsersService(DataContext context,
                        SignInManager<User> signInManager,
                        UserManager<User> userManager,
                        IHttpContextAccessor httpContextAccessor,
                        ILogger<UsersService> logger,
                        IUserActivityRecorder activity) : base(context) // ✅ Agregar logger

        {
            _context = context;
            _signInManager = signInManager;
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _activity = activity;
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

                // El usuario y su registro en el historial quedan juntos (o ninguno).
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                string temporaryPassword = PasswordGenerator.Generate();
                Response<IdentityResult> createResponse = await AddUserAsync(user, temporaryPassword);

                if (!createResponse.IsSuccess || createResponse.Result == null)
                {
                    List<string>? errors = createResponse.Result?.Errors.Select(e => e.Description).ToList();
                    return Response<TemporaryPasswordDTO>.Failure("No se pudo crear el usuario.", errors);
                }

                string? roleName = await RoleNameAsync(roleId);
                await _activity.StageAsync(UserActivityType.UserCreated, requestedByUserName, user, details: $"Rol: {roleName}");
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

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

                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                string token = await _userManager.GeneratePasswordResetTokenAsync(user);
                IdentityResult result = await _userManager.ResetPasswordAsync(user, token, temporaryPassword);

                if (!result.Succeeded)
                    return Response<TemporaryPasswordDTO>.Failure("No se pudo restablecer la contraseña.",
                        result.Errors.Select(e => e.Description).ToList());

                // Si la cuenta estuvo en manos de otro, su PIN de autorización tampoco debe servir.
                await _context.UserAuthorizationPins.Where(p => p.UserId == user.Id).ExecuteDeleteAsync();

                await _activity.StageAsync(UserActivityType.PasswordReset, requestedByUserName, user);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

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

                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                IdentityResult result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

                if (!result.Succeeded)
                {
                    List<string> errors = result.Errors.Select(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)
                        ? "La contraseña actual no es correcta."
                        : e.Description).ToList();
                    return Response<User>.Failure(string.Join(" ", errors), errors);
                }

                await _activity.StageAsync(UserActivityType.PasswordChanged, userName, user);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

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

        public Task<bool> CheckPasswordAsync(User user, string password) => _userManager.CheckPasswordAsync(user, password);

        private async Task<bool> IsSuperAdminRoleAsync(Guid roleId)
            => await _context.SistemaVentasRoles.AnyAsync(r => r.Id == roleId && r.Name == Env.SUPER_ADMIN_ROLE_NAME);

        /// <summary>Un Admin desactivado ya no cuenta como Admin.</summary>
        private async Task<bool> IsSuperAdminUserAsync(string userName)
            => await _context.Users.AnyAsync(u => u.UserName == userName
                                                  && u.DeactivatedAt == null
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


        /// <summary>
        /// Desactiva un usuario: no puede entrar y sus sesiones abiertas dejan de servir en la siguiente
        /// petición (ver ActiveUserCookieValidator). No se borra: se conservan sus ventas y turnos.
        /// Si tiene la caja abierta se permite igual y se devuelve el turno para avisar.
        /// </summary>
        public async Task<Response<UserDeactivationDTO>> DeactivateAsync(Guid userId, string requestedByUserName)
        {
            try
            {
                string id = userId.ToString();
                string fullName;

                await using (IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync())
                {
                    await LockAdminRoleAsync();

                    User? user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
                    if (user is null)
                        return Response<UserDeactivationDTO>.Failure("No existe el usuario.");

                    if (string.Equals(user.UserName, requestedByUserName, StringComparison.OrdinalIgnoreCase))
                        return Response<UserDeactivationDTO>.Failure("No puedes desactivar tu propio usuario.");

                    // Se revisa DESPUÉS del bloqueo: si otro Admin acaba de desactivar a quien pide esto, se rechaza.
                    if (!await _context.Users.AnyAsync(u => u.UserName == requestedByUserName && u.DeactivatedAt == null))
                        return Response<UserDeactivationDTO>.Failure("Tu usuario no está activo.");

                    if (await IsSuperAdminRoleAsync(user.SistemaVentasRoleId))
                    {
                        if (!await IsSuperAdminUserAsync(requestedByUserName))
                            return Response<UserDeactivationDTO>.Failure(
                                $"Solo un usuario con rol '{Env.SUPER_ADMIN_ROLE_NAME}' puede desactivar a otro '{Env.SUPER_ADMIN_ROLE_NAME}'.");

                        if (!await ExistsOtherActiveAdminAsync(user))
                            return Response<UserDeactivationDTO>.Failure("No se puede desactivar al único administrador activo.");
                    }

                    // También cambian los sellos: el de seguridad invalida las sesiones y el de concurrencia
                    // impide que un guardado de Identity con datos viejos vuelva a dejarlo activo.
                    DateTime now = DateTime.UtcNow;
                    int changed = await _context.Users
                        .Where(u => u.Id == id && u.DeactivatedAt == null)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(u => u.DeactivatedAt, now)
                            .SetProperty(u => u.SecurityStamp, Guid.NewGuid().ToString())
                            .SetProperty(u => u.ConcurrencyStamp, Guid.NewGuid().ToString()));

                    if (changed == 0)
                        return Response<UserDeactivationDTO>.Failure("El usuario ya estaba desactivado.");

                    await _activity.StageAsync(UserActivityType.UserDeactivated, requestedByUserName, user);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    fullName = user.FullName;
                }

                _logger.LogInformation("Usuario desactivado: {UserId} por {DesactivadoPor}", id, requestedByUserName);

                var openSession = await _context.CashSessions.AsNoTracking()
                    .Where(c => c.UserId == id && c.Status == CashSessionStatus.Open)
                    .Select(c => new { c.Number, c.OpenedAtUtc })
                    .FirstOrDefaultAsync();

                return Response<UserDeactivationDTO>.Success(new UserDeactivationDTO
                {
                    FullName = fullName,
                    OpenCashSessionNumber = openSession?.Number,
                    OpenCashSessionOpenedAtUtc = openSession?.OpenedAtUtc
                }, "Usuario desactivado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al desactivar el usuario {UserId}", userId);
                return Response<UserDeactivationDTO>.Failure("No se pudo desactivar el usuario.");
            }
        }

        /// <summary>Vuelve a activar un usuario. Entra con la contraseña que tenía.</summary>
        public Task<Response<string>> ReactivateAsync(Guid userId, string requestedByUserName)
            => ChangeAccessAsync(userId, requestedByUserName, "reactivar",
                user => user.DeactivatedAt is null ? "El usuario ya está activo." : null,
                query => query.ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.DeactivatedAt, (DateTime?)null)
                    .SetProperty(u => u.AccessFailedCount, 0)
                    .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null)
                    .SetProperty(u => u.ConcurrencyStamp, Guid.NewGuid().ToString())),
                UserActivityType.UserReactivated, "Usuario reactivado");

        /// <summary>Quita el bloqueo por intentos fallidos sin cambiar la contraseña.</summary>
        public Task<Response<string>> UnlockAsync(Guid userId, string requestedByUserName)
            => ChangeAccessAsync(userId, requestedByUserName, "desbloquear",
                user => user.DeactivatedAt is not null ? "El usuario está desactivado."
                      : user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow ? "El usuario no está bloqueado."
                      : null,
                query => query.ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.AccessFailedCount, 0)
                    .SetProperty(u => u.LockoutEnd, (DateTimeOffset?)null)
                    .SetProperty(u => u.ConcurrencyStamp, Guid.NewGuid().ToString())),
                UserActivityType.UserUnlocked, "Usuario desbloqueado");

        /// <summary>Parte común de reactivar y desbloquear. Un Admin solo lo puede tocar otro Admin.</summary>
        private async Task<Response<string>> ChangeAccessAsync(Guid userId, string requestedByUserName, string action,
            Func<User, string?> rejection, Func<IQueryable<User>, Task<int>> update, UserActivityType activityType,
            string successMessage)
        {
            try
            {
                // Con el bloqueo, dos clics a la vez no dejan dos registros: el segundo ya ve el cambio.
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();
                await LockAdminRoleAsync();

                string id = userId.ToString();
                User? user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
                if (user is null)
                    return Response<string>.Failure("No existe el usuario.");

                if (await IsSuperAdminRoleAsync(user.SistemaVentasRoleId) && !await IsSuperAdminUserAsync(requestedByUserName))
                    return Response<string>.Failure(
                        $"Solo un usuario con rol '{Env.SUPER_ADMIN_ROLE_NAME}' puede {action} a otro '{Env.SUPER_ADMIN_ROLE_NAME}'.");

                string? reason = rejection(user);
                if (reason is not null)
                    return Response<string>.Failure(reason);

                await update(_context.Users.Where(u => u.Id == id));
                await _activity.StageAsync(activityType, requestedByUserName, user);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("{Mensaje}: {UserId} por {Quien}", successMessage, id, requestedByUserName);

                return Response<string>.Success(user.FullName, successMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al {Accion} el usuario {UserId}", action, userId);
                return Response<string>.Failure($"No se pudo {action} el usuario.");
            }
        }

        /// <summary>
        /// Debe llamarse dentro de una transacción y ANTES de revisar quién es Admin. Bloquea la fila del
        /// rol Admin (sin cambiarla): así desactivar y cambiar roles se hacen de uno en uno, y dos
        /// operaciones a la vez no pueden dejar el sistema sin ningún Admin activo.
        /// </summary>
        private Task LockAdminRoleAsync()
            => _context.SistemaVentasRoles
                .Where(r => r.Name == Env.SUPER_ADMIN_ROLE_NAME)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Name, r => r.Name));

        private Task<bool> ExistsOtherActiveAdminAsync(User admin)
            => _context.Users.AnyAsync(u => u.Id != admin.Id
                                            && u.SistemaVentasRoleId == admin.SistemaVentasRoleId
                                            && u.DeactivatedAt == null);

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

        public async Task<Response<UserDTO>> EditAsync(UserDTO dto, string requestedByUserName)
        {
            try
            {
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();
                await LockAdminRoleAsync();

                User? user = await GetUserAsync(dto.Id?.ToString());

                if (user == null)
                {
                    return Response<UserDTO>.Failure("No existe usuario.");
                }

                // Si no llega rol, se conserva el actual.
                Guid newRoleId = user.SistemaVentasRoleId;
                if (!string.IsNullOrWhiteSpace(dto.SistemaVentasRoleId))
                {
                    if (!Guid.TryParse(dto.SistemaVentasRoleId, out newRoleId)
                        || !await _context.SistemaVentasRoles.AnyAsync(r => r.Id == newRoleId))
                        return Response<UserDTO>.Failure("Seleccione un rol válido.");
                }

                bool roleChanges = newRoleId != user.SistemaVentasRoleId;
                bool targetIsAdmin = await IsSuperAdminRoleAsync(user.SistemaVentasRoleId);
                bool newRoleIsAdmin = await IsSuperAdminRoleAsync(newRoleId);

                // Sin esto, quien tiene "updateUsers" podría darse el rol Admin a sí mismo o a un cómplice,
                // o cambiarle los datos a un Admin. Solo otro Admin puede tocar usuarios Admin o asignar ese rol.
                if ((targetIsAdmin || newRoleIsAdmin) && !await IsSuperAdminUserAsync(requestedByUserName))
                    return Response<UserDTO>.Failure(
                        $"Solo un usuario con rol '{Env.SUPER_ADMIN_ROLE_NAME}' puede editar usuarios con ese rol o asignarlo.");

                // Nadie cambia su propio rol: evita ascenderse y también que un Admin se quite el rol por error.
                if (roleChanges && string.Equals(user.UserName, requestedByUserName, StringComparison.OrdinalIgnoreCase))
                    return Response<UserDTO>.Failure("No puedes cambiar tu propio rol. Pídeselo a otro administrador.");

                // Quien perdió el acceso mientras tanto (otra operación lo desactivó) no puede editar.
                if (await _context.Users.AnyAsync(u => u.UserName == requestedByUserName && u.DeactivatedAt != null))
                    return Response<UserDTO>.Failure("Tu usuario no está activo.");

                // Siempre debe quedar al menos un Admin activo.
                if (roleChanges && targetIsAdmin && !newRoleIsAdmin && user.DeactivatedAt is null
                    && !await ExistsOtherActiveAdminAsync(user))
                    return Response<UserDTO>.Failure($"No se puede quitar el rol '{Env.SUPER_ADMIN_ROLE_NAME}' al único administrador.");

                Guid oldRoleId = user.SistemaVentasRoleId;
                List<string> changedFields = ChangedFields(user, dto.Document, dto.FirstName, dto.LastName, dto.PhoneNumber);

                user.PhoneNumber = dto.PhoneNumber;
                user.Document = dto.Document;
                user.FirstName = dto.FirstName;
                user.LastName = dto.LastName;
                user.SistemaVentasRoleId = newRoleId;

                await _context.SaveChangesAsync();

                if (roleChanges)
                {
                    await _activity.StageAsync(UserActivityType.UserRoleChanged, requestedByUserName, user,
                        details: $"Rol: {await RoleNameAsync(oldRoleId)} → {await RoleNameAsync(newRoleId)}");
                    // Uno por uno: en un mismo guardado EF los ordena por el Id (al azar) y el número no seguiría el orden.
                    await _context.SaveChangesAsync();
                }
                if (changedFields.Count > 0)
                    await _activity.StageAsync(UserActivityType.UserEdited, requestedByUserName, user,
                        details: "Cambió: " + string.Join(", ", changedFields));
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                if (roleChanges)
                    _logger.LogInformation("Rol cambiado: {Email} por {CambiadoPor}", user.Email, requestedByUserName);

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

        public async Task<Response<PaginationResponse<UserDTO>>> GetPaginatedListAsync(PaginationRequest request,
            UserStatus? status = null)
        {
            IQueryable<User> queryable = _context.Users.AsNoTracking().Include(u => u.SistemaVentasRole);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            queryable = status switch
            {
                UserStatus.Active => queryable.Where(u => u.DeactivatedAt == null && (u.LockoutEnd == null || u.LockoutEnd <= now)),
                UserStatus.Locked => queryable.Where(u => u.DeactivatedAt == null && u.LockoutEnd != null && u.LockoutEnd > now),
                UserStatus.Deactivated => queryable.Where(u => u.DeactivatedAt != null),
                _ => queryable
            };

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

            queryable = queryable.OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Id);

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
            // Un usuario desactivado no entra, y no se le dice por qué a quien está en el login.
            User? account = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == dto.Email);
            if (account?.DeactivatedAt is not null)
            {
                await _activity.RecordFailedLoginAsync(UserActivityType.DeactivatedLoginRejected, account, dto.Email);
                return new Response<SignInResult> { Result = SignInResult.NotAllowed, IsSuccess = false };
            }

            // lockoutOnFailure: true → tras 5 intentos fallidos seguidos la cuenta se bloquea 15 minutos
            // (ver CustomConfiguration.ConfigureIdentityOptions).
            SignInResult result = await _signInManager.PasswordSignInAsync(dto.Email, dto.Password, isPersistent: false, lockoutOnFailure: true);

            if (result.Succeeded)
                await _activity.RecordAccessAsync(UserActivityType.LoginSucceeded, dto.Email);
            else
                // Si alguien escribió su contraseña en los dos campos, el "correo" no se guarda.
                await _activity.RecordFailedLoginAsync(
                    result.IsLockedOut ? UserActivityType.AccountLocked : UserActivityType.LoginFailed,
                    account, dto.Email == dto.Password ? null : dto.Email);

            return new Response<SignInResult>
            {
                Result = result,
                IsSuccess = result.Succeeded,
            };
        }

        public Task RecordRejectedLoginFormAsync(LoginDTO dto)
            => _activity.RecordFailedLoginAsync(UserActivityType.LoginFailed, null,
                dto.Email == dto.Password ? null : dto.Email);

        public async Task LogoutAsync()
        {
            string? userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
            await _signInManager.SignOutAsync();

            if (userName is not null)
                await _activity.RecordAccessAsync(UserActivityType.Logout, userName);
        }

        private Task<string?> RoleNameAsync(Guid roleId)
            => _context.SistemaVentasRoles.Where(r => r.Id == roleId).Select(r => (string?)r.Name).FirstOrDefaultAsync();

        /// <summary>Solo los NOMBRES de los campos que cambian: el historial no copia los datos personales.</summary>
        private static List<string> ChangedFields(User user, string document, string firstName, string lastName, string? phone)
        {
            List<string> changed = [];
            if (user.Document != document) changed.Add("Documento");
            if (user.FirstName != firstName) changed.Add("Nombres");
            if (user.LastName != lastName) changed.Add("Apellidos");
            if ((user.PhoneNumber ?? string.Empty) != (phone ?? string.Empty)) changed.Add("Teléfono");
            return changed;
        }

        /// <summary>
        /// "Mi perfil". A quién se edita lo decide userName (el de la sesión), nunca el formulario:
        /// si viniera del navegador, cualquiera podría cambiar los datos de otro usuario.
        /// </summary>
        public async Task<Response<AccountUserDTO>> UpdateUserAsync(AccountUserDTO dto, string userName)
        {
            try
            {
                User? user = await _userManager.FindByNameAsync(userName);
                if (user is null)
                    return Response<AccountUserDTO>.Failure("No existe el usuario.");

                List<string> changedFields = ChangedFields(user, dto.Document, dto.FirstName, dto.LastName, dto.PhoneNumber);

                user.PhoneNumber = dto.PhoneNumber;
                user.Document = dto.Document;
                user.FirstName = dto.FirstName;
                user.LastName = dto.LastName;

                _context.Users.Update(user);

                // Se guarda junto con el cambio (un solo SaveChanges es una sola transacción).
                if (changedFields.Count > 0)
                    await _activity.StageAsync(UserActivityType.ProfileUpdated, userName, user,
                        details: "Cambió: " + string.Join(", ", changedFields));

                await _context.SaveChangesAsync();

                return Response<AccountUserDTO>.Success(dto, "Datos actualizados con éxito");
            }
            catch (Exception ex)
            {
                return Response<AccountUserDTO>.Failure(ex);
            }
        }

        private async Task<User> GetUserAsync(string? id)
        {
            return await _context.Users.FindAsync(id);
        }
    }
}
