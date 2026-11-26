using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SistemadeVentasSupermercado.Web.Core;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.DTOs;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public class AuthorizationPinService : IAuthorizationPinService
    {
        private const string PermissionModule = "Ventas";
        private const string CannotAuthorize = "Esa persona no puede autorizar esta operación.";

        private readonly DataContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IPasswordHasher<User> _hasher;
        private readonly IUserActivityRecorder _activity;
        private readonly ILogger<AuthorizationPinService> _logger;
        private readonly TimeProvider _clock;

        // El reloj solo se reemplaza en las pruebas (para no esperar 15 minutos).
        public AuthorizationPinService(DataContext context, UserManager<User> userManager,
            IPasswordHasher<User> hasher, IUserActivityRecorder activity,
            ILogger<AuthorizationPinService> logger, TimeProvider? clock = null)
        {
            _context = context;
            _userManager = userManager;
            _hasher = hasher;
            _activity = activity;
            _logger = logger;
            _clock = clock ?? TimeProvider.System;
        }

        private static string[] PermissionsFor(SupervisorOperation? operation) => operation switch
        {
            SupervisorOperation.CancelSale => ["cancelSales"],
            SupervisorOperation.CreateReturn => ["createReturns"],
            null => ["cancelSales", "createReturns"],
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        /// <summary>
        /// Usuarios que ahora mismo pueden autorizar: activos, sin bloqueo, sin cambio de contraseña
        /// pendiente y con rol Admin o con el permiso. Sin operación = con cualquiera de los permisos.
        /// </summary>
        private IQueryable<User> Eligible(SupervisorOperation? operation)
        {
            string[] permissions = PermissionsFor(operation);
            DateTimeOffset now = _clock.GetUtcNow();

            return _context.Users.AsNoTracking().Where(u =>
                u.DeactivatedAt == null
                && (u.LockoutEnd == null || u.LockoutEnd <= now)
                && !u.MustChangePassword
                && (u.SistemaVentasRole!.Name == Env.SUPER_ADMIN_ROLE_NAME
                    || _context.RolePermissions.Any(rp => rp.SistemaVentasRoleId == u.SistemaVentasRoleId
                                                         && rp.Permission.Module == PermissionModule
                                                         && permissions.Contains(rp.Permission.Name))));
        }

        public Task<bool> CanHavePinAsync(string userName)
            => Eligible(null).AnyAsync(u => u.UserName == userName);

        public Task<bool> HasPinAsync(string userName)
            => _context.UserAuthorizationPins.AnyAsync(p => p.User!.UserName == userName);

        public async Task<Response<bool>> SetPinAsync(string userName, SetAuthorizationPinDTO dto)
        {
            if (!AuthorizationPinRules.HasValidFormat(dto.Pin))
                return Response<bool>.Failure($"El PIN debe tener {AuthorizationPinRules.Length} dígitos.");
            if (AuthorizationPinRules.IsTooEasy(dto.Pin))
                return Response<bool>.Failure("Ese PIN es muy fácil de adivinar. Evita números iguales, seguidos o repetidos.");
            if (dto.Pin != dto.ConfirmPin)
                return Response<bool>.Failure("Los PIN no coinciden.");

            try
            {
                if (!await CanHavePinAsync(userName))
                    return Response<bool>.Failure("Tu usuario no puede autorizar operaciones, así que no necesita PIN.");

                User? user = await _userManager.FindByNameAsync(userName);
                if (user is null)
                    return Response<bool>.Failure("No existe el usuario.");

                // Con una sesión que quedó abierta se podrían probar contraseñas aquí: los errores cuentan
                // para el mismo bloqueo que el inicio de sesión.
                if (!await _userManager.CheckPasswordAsync(user, dto.CurrentPassword ?? string.Empty))
                {
                    await _userManager.AccessFailedAsync(user);
                    return Response<bool>.Failure("La contraseña actual no es correcta.");
                }
                await _userManager.ResetAccessFailedCountAsync(user);

                // El PIN y su registro en el historial quedan juntos (o ninguno).
                await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

                string hash = _hasher.HashPassword(user, dto.Pin);
                int updated = await _context.UserAuthorizationPins
                    .Where(p => p.UserId == user.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.PinHash, hash)
                        .SetProperty(p => p.FailedAttempts, 0)
                        .SetProperty(p => p.LockedUntilUtc, (DateTime?)null));

                if (updated == 0)
                    _context.UserAuthorizationPins.Add(new UserAuthorizationPin { UserId = user.Id, PinHash = hash });

                await _activity.StageAsync(UserActivityType.AuthorizationPinChanged, userName, user);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Response<bool>.Success(true, "PIN guardado");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar el PIN de {UserName}", userName);
                return Response<bool>.Failure("No se pudo guardar el PIN.");
            }
        }

        public async Task<List<AuthorizerDTO>> GetAuthorizersAsync(SupervisorOperation operation, string requesterUserName)
            => await Eligible(operation)
                .Where(u => u.UserName != requesterUserName
                            && _context.UserAuthorizationPins.Any(p => p.UserId == u.Id))
                .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Id)
                .Select(u => new AuthorizerDTO { UserId = u.Id, FullName = u.FirstName + " " + u.LastName })
                .ToListAsync();

        public async Task<Response<AuthorizerDTO>> VerifyAsync(SupervisorOperation operation, string? authorizerUserId,
            string? pin, string requesterUserName)
        {
            // Error de programación: si la transacción se deshace, el intento fallido no quedaría contado.
            if (_context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("El PIN se comprueba antes de abrir la transacción.");

            if (string.IsNullOrWhiteSpace(authorizerUserId))
                return Response<AuthorizerDTO>.Failure("Elige quién autoriza.");
            if (!AuthorizationPinRules.HasValidFormat(pin))
                return Response<AuthorizerDTO>.Failure($"El PIN tiene {AuthorizationPinRules.Length} dígitos.");

            try
            {
                // La lista del formulario no basta: el navegador puede enviar cualquier Id.
                User? authorizer = await Eligible(operation).FirstOrDefaultAsync(u => u.Id == authorizerUserId);

                if (authorizer is not null
                    && string.Equals(authorizer.UserName, requesterUserName, StringComparison.OrdinalIgnoreCase))
                    return Response<AuthorizerDTO>.Failure("No puedes autorizarte a ti mismo.");

                IQueryable<UserAuthorizationPin> row =
                    _context.UserAuthorizationPins.Where(p => p.UserId == authorizerUserId);

                string? hash = authorizer is null
                    ? null
                    : await row.Select(p => p.PinHash).FirstOrDefaultAsync();

                if (authorizer is null || hash is null)
                    return await RejectAsync(authorizer, requesterUserName, CannotAuthorize);

                DateTime now = _clock.GetUtcNow().UtcDateTime;

                // Un bloqueo que ya venció se limpia y vuelve a haber 5 intentos.
                await row.Where(p => p.LockedUntilUtc != null && p.LockedUntilUtc <= now)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.FailedAttempts, 0)
                        .SetProperty(p => p.LockedUntilUtc, (DateTime?)null));

                // El intento se cuenta ANTES de comparar, en un solo UPDATE: así, aunque lleguen cien a la
                // vez, solo cinco llegan a comparar. El quinto ya deja el bloqueo puesto.
                DateTime lockUntil = now + AuthorizationPinRules.LockoutTime;
                int reserved = await row.Where(p => p.LockedUntilUtc == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.FailedAttempts, p => p.FailedAttempts + 1)
                        .SetProperty(p => p.LockedUntilUtc,
                            p => p.FailedAttempts + 1 >= AuthorizationPinRules.MaxFailedAttempts
                                ? (DateTime?)lockUntil : null));

                if (reserved == 0)
                {
                    DateTime? until = await row.Select(p => p.LockedUntilUtc).FirstOrDefaultAsync();
                    int minutes = Math.Max(1, (int)Math.Ceiling(((until ?? lockUntil) - now).TotalMinutes));
                    return await RejectAsync(authorizer, requesterUserName,
                        $"El PIN de {authorizer.FullName} está bloqueado. Intenta en {minutes} min.");
                }

                if (_hasher.VerifyHashedPassword(authorizer, hash, pin!) == PasswordVerificationResult.Failed)
                {
                    int attempts = await row.Select(p => p.FailedAttempts).FirstOrDefaultAsync();
                    int left = AuthorizationPinRules.MaxFailedAttempts - attempts;
                    return await RejectAsync(authorizer, requesterUserName, left > 0
                        ? $"PIN incorrecto. Intentos restantes: {left}."
                        : $"PIN incorrecto. El PIN quedó bloqueado {AuthorizationPinRules.LockoutTime.TotalMinutes:0} minutos.");
                }

                await row.ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.FailedAttempts, 0)
                    .SetProperty(p => p.LockedUntilUtc, (DateTime?)null));

                return Response<AuthorizerDTO>.Success(
                    new AuthorizerDTO { UserId = authorizer.Id, FullName = authorizer.FullName }, "PIN correcto");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al comprobar un PIN de autorización");
                return Response<AuthorizerDTO>.Failure("No se pudo comprobar el PIN.");
            }
        }

        private async Task<Response<AuthorizerDTO>> RejectAsync(User? authorizer, string requesterUserName, string message)
        {
            await _activity.RecordFailedAuthorizationAsync(authorizer, requesterUserName);
            return Response<AuthorizerDTO>.Failure(message);
        }
    }
}
