using Microsoft.Extensions.Options;

namespace SistemadeVentasSupermercado.Web.Core.ScreenLock
{
    /// <summary>
    /// Estado de bloqueo de cada sesión abierta. Vive en la MEMORIA del servidor (en la cookie solo va un
    /// Id de sesión que nunca cambia). No puede ir en la cookie: se reescribe en cada petición y una
    /// petición que iba en camino podría devolver una cookie sin bloquear.
    /// Una sesión que no se conoce (por ejemplo, tras reiniciar la aplicación) se considera BLOQUEADA.
    /// Sirve para una sola instancia de la aplicación.
    /// </summary>
    public class ScreenLockStore
    {
        // Más que lo que dura una sesión sin uso (8 horas).
        private static readonly TimeSpan Forget = TimeSpan.FromHours(24);

        private sealed class State
        {
            public DateTimeOffset LastActivity;
            public bool Locked;
        }

        private readonly object _sync = new();
        private readonly Dictionary<string, State> _sessions = new();
        private readonly ScreenLockOptions _options;
        private readonly TimeProvider _clock;

        // El reloj solo se reemplaza en las pruebas.
        public ScreenLockStore(IOptions<ScreenLockOptions> options, TimeProvider? clock = null)
        {
            _options = options.Value;
            _clock = clock ?? TimeProvider.System;
        }

        /// <summary>Sesión recién iniciada: desbloqueada.</summary>
        public void Start(string sessionId)
        {
            lock (_sync)
            {
                DateTimeOffset now = _clock.GetUtcNow();
                foreach (string old in _sessions.Where(s => now - s.Value.LastActivity > Forget).Select(s => s.Key).ToList())
                    _sessions.Remove(old);

                _sessions[sessionId] = new State { LastActivity = now };
            }
        }

        /// <summary>
        /// Para cada petición normal: si la sesión está bloqueada o pasó demasiado tiempo sin uso,
        /// devuelve false (y queda bloqueada). Si no, anota la actividad y devuelve true.
        /// Revisar y anotar es una sola operación: una petición tardía no "revive" una sesión vencida.
        /// </summary>
        public bool TryUse(string sessionId)
        {
            lock (_sync)
            {
                State state = Get(sessionId);
                if (IsLocked(state))
                    return false;

                state.LastActivity = _clock.GetUtcNow();
                return true;
            }
        }

        /// <summary>¿Está bloqueada? Preguntar no cuenta como actividad.</summary>
        public bool IsLocked(string sessionId)
        {
            lock (_sync) return IsLocked(Get(sessionId));
        }

        public void Lock(string sessionId)
        {
            lock (_sync) Get(sessionId).Locked = true;
        }

        /// <summary>Solo se llama después de comprobar la contraseña.</summary>
        public void Unlock(string sessionId)
        {
            lock (_sync)
            {
                State state = Get(sessionId);
                state.Locked = false;
                state.LastActivity = _clock.GetUtcNow();
            }
        }

        private State Get(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out State? state))
                _sessions[sessionId] = state = new State { Locked = true, LastActivity = _clock.GetUtcNow() };
            return state;
        }

        private bool IsLocked(State state)
        {
            if (!state.Locked && _options.ServerIdleLimit is TimeSpan limit
                && _clock.GetUtcNow() - state.LastActivity > limit)
                state.Locked = true;

            return state.Locked;
        }
    }
}
