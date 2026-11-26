using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SistemadeVentasSupermercado.Web.Data;
using SistemadeVentasSupermercado.Web.Data.Entities;
using SistemadeVentasSupermercado.Web.Services.Abstractions;

namespace SistemadeVentasSupermercado.Web.Services.Implementations
{
    public partial class UserActivityRecorder : IUserActivityRecorder
    {
        /// <summary>Lo que se guarda cuando lo escrito en el correo no tiene forma de correo.</summary>
        public const string InvalidUserText = "usuario no válido";
        private const int EmailMaxLength = 256;

        private readonly DataContext _context;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<UserActivityRecorder> _logger;
        private readonly TimeProvider _clock;

        // El reloj solo se reemplaza en las pruebas (para que no cambie el minuto a mitad de una).
        public UserActivityRecorder(DataContext context, IHttpContextAccessor http,
            ILogger<UserActivityRecorder> logger, TimeProvider? clock = null)
        {
            _context = context;
            _http = http;
            _logger = logger;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

        public async Task StageAsync(UserActivityType type, string? actorUserName, User? target = null,
            string? targetName = null, string? details = null)
        {
            User? actor = await FindAsync(actorUserName);

            _context.UserActivities.Add(new UserActivity
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = UtcNow,
                Type = type,
                ActorUserId = actor?.Id,
                ActorName = actor is null ? Limit(actorUserName, UserActivity.NameMaxLength) : Describe(actor),
                TargetUserId = target?.Id,
                TargetName = Limit(target is null ? targetName : Describe(target), UserActivity.NameMaxLength),
                Details = Limit(details, UserActivity.DetailsMaxLength),
                IpAddress = ClientIp()
            });
        }

        public async Task RecordAccessAsync(UserActivityType type, string userName)
        {
            UserActivity? activity = null;
            try
            {
                User? user = await FindAsync(userName);
                activity = new UserActivity
                {
                    Id = Guid.NewGuid(),
                    OccurredAtUtc = UtcNow,
                    Type = type,
                    ActorUserId = user?.Id,
                    ActorName = user is null ? Limit(userName, UserActivity.NameMaxLength) : Describe(user),
                    IpAddress = ClientIp()
                };
                _context.UserActivities.Add(activity);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Forget(activity);
                _logger.LogError(ex, "No se pudo registrar la actividad {Tipo}", type);
            }
        }

        public async Task RecordFailedLoginAsync(UserActivityType type, User? target, string? typedEmail)
        {
            try
            {
                string text = target?.Email ?? SafeEmail(typedEmail);
                string? ip = ClientIp();
                DateTime now = UtcNow;
                // Todos los textos no válidos comparten el mismo, así que se agrupan solo por IP.
                string key = $"{(int)type}|{text.ToLowerInvariant()}|{ip}|{now:yyyyMMddHHmm}";
                key = Limit(key, UserActivity.GroupKeyMaxLength)!;

                // 1. Si el grupo ya existe, se suma en la base de datos (atómico: no se pierden intentos).
                if (await AddAttemptAsync(key, now))
                    return;

                // 2. Si no, se crea. El índice único impide que dos intentos a la vez creen dos filas.
                UserActivity activity = new()
                {
                    Id = Guid.NewGuid(),
                    OccurredAtUtc = now,
                    LastAttemptAtUtc = now,
                    Type = type,
                    TargetUserId = target?.Id,
                    TargetName = Limit(target is null ? text : Describe(target), UserActivity.NameMaxLength),
                    IpAddress = ip,
                    GroupKey = key
                };
                _context.UserActivities.Add(activity);
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // 3. Otro intento creó el grupo justo ahora: se suma a ese.
                    Forget(activity);
                    if (!await AddAttemptAsync(key, now))
                        throw;
                }
                catch
                {
                    Forget(activity);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "No se pudo registrar el intento fallido {Tipo}", type);
            }
        }

        private async Task<bool> AddAttemptAsync(string key, DateTime now)
            => await _context.UserActivities
                .Where(a => a.GroupKey == key)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.AttemptCount, a => a.AttemptCount + 1)
                    // Con intentos simultáneos, la hora del último nunca retrocede.
                    .SetProperty(a => a.LastAttemptAtUtc,
                        a => a.LastAttemptAtUtc == null || a.LastAttemptAtUtc < now ? now : a.LastAttemptAtUtc)) > 0;

        /// <summary>Quita del contexto un registro que no se pudo guardar, para que no estorbe en el siguiente guardado.</summary>
        private void Forget(UserActivity? activity)
        {
            if (activity is not null)
                _context.Entry(activity).State = EntityState.Detached;
        }

        private async Task<User?> FindAsync(string? userName)
            => string.IsNullOrWhiteSpace(userName)
                ? null
                : await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == userName);

        private static string Describe(User user) => $"{user.FullName} ({user.Email})";

        private static string SafeEmail(string? typed)
        {
            string text = typed?.Trim() ?? string.Empty;
            return text.Length <= EmailMaxLength && EmailShape().IsMatch(text) ? text.ToLowerInvariant() : InvalidUserText;
        }

        // Algo@dominio.extensión (la extensión solo con letras). Más estricto que [EmailAddress], que acepta "a@b".
        [GeneratedRegex(@"^[^@\s]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$")]
        private static partial Regex EmailShape();

        /// <summary>La IP que ve la aplicación (en Docker puede ser la interna: ver CLAUDE.md). No usa X-Forwarded-For.</summary>
        private string? ClientIp()
        {
            IPAddress? ip = _http.HttpContext?.Connection.RemoteIpAddress;
            if (ip is null) return null;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            return Limit(ip.ToString(), UserActivity.IpAddressMaxLength);
        }

        private static string? Limit(string? text, int max)
            => text is null || text.Length <= max ? text : text[..max];

        private static bool IsUniqueViolation(DbUpdateException ex)
            => ex.GetBaseException() is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
    }
}
