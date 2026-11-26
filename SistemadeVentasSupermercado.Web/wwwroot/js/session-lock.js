// Bloqueo de pantalla por inactividad. La capa va ENCIMA de la página (no se cambia de página), así
// el carrito del punto de venta se conserva. El bloqueo de verdad lo aplica el servidor: esto avisa
// cuándo bloquear, tapa la pantalla y pide la contraseña.
(function () {
    const dialog = document.getElementById('sessionLock');
    if (!dialog) return;

    const urls = {
        lock: dialog.dataset.lockUrl,
        unlock: dialog.dataset.unlockUrl,
        activity: dialog.dataset.activityUrl
    };
    const idleMs = Number(dialog.dataset.idleMinutes) * 60000; // 0 = sin bloqueo automático

    const REPORT_EVERY_MS = 30000;  // cada cuánto se le avisa al servidor que hay uso (como máximo)
    const FORM_TIMEOUT_MS = 30000;  // el campo de la contraseña se vuelve a esconder si nadie escribe
    const ACTIVITY_KEY = 'sv-actividad'; // compartidas entre las pestañas del navegador
    const LOCK_KEY = 'sv-bloqueo';

    const form = dialog.querySelector('[data-lock-form]');
    const password = form.querySelector('input[type="password"]');
    const showButton = dialog.querySelector('[data-lock-show]');
    const message = dialog.querySelector('[data-lock-message]');
    const focusHolder = dialog.querySelector('[data-lock-focus]');
    const token = form.querySelector('input[name="__RequestVerificationToken"]').value;

    let locked = false;
    let serverKnows = true;   // false mientras el servidor no confirme un bloqueo pedido desde aquí
    let lockRequest = null;
    let lastActivity = Date.now();
    let reported = lastActivity;  // cargar la página ya contó como actividad en el servidor
    let lastReport = lastActivity;
    let lastShared = 0;
    let lastTyping = 0;
    let unlocking = false;

    // localStorage puede no estar disponible (modo privado, datos bloqueados).
    function read(key) {
        try { return localStorage.getItem(key); } catch { return null; }
    }
    function write(key, value) {
        try { localStorage.setItem(key, value); } catch { /* cada pestaña va sola */ }
    }

    function latestActivity() {
        return Math.max(lastActivity, Number(read(ACTIVITY_KEY)) || 0);
    }

    function isIdle() {
        return idleMs > 0 && Date.now() - latestActivity() >= idleMs;
    }

    /** POST con el token antiforgery. Devuelve null si no hubo respuesta. */
    async function post(url, body) {
        try {
            return await fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                redirect: 'manual', // una redirección significa que la sesión ya no existe
                headers: { 'RequestVerificationToken': token },
                body: body
            });
        } catch {
            return null;
        }
    }

    function showMessage(text) {
        message.textContent = text;
        message.classList.toggle('d-none', !text);
    }

    function hideForm() {
        password.value = '';
        form.classList.add('d-none');
        showButton.classList.remove('d-none');
        focusHolder.focus();
    }

    function sessionEnded() {
        showMessage('Tu sesión terminó. Cierra la sesión y vuelve a entrar.');
    }

    // ---------- Tapar y destapar ----------

    function show() {
        if (locked) return;
        locked = true;
        if (document.activeElement) document.activeElement.blur();
        document.body.classList.add('sv-bloqueado');
        showMessage('');
        if (!dialog.open) dialog.showModal();
        hideForm();
    }

    function hide() {
        locked = false;
        serverKnows = true;
        password.value = '';
        document.body.classList.remove('sv-bloqueado');
        if (dialog.open) dialog.close();
        lastActivity = reported = lastReport = Date.now();
        write(ACTIVITY_KEY, String(lastActivity));
    }

    /** Bloquea desde esta pestaña: por el botón o por inactividad. */
    function lock() {
        if (locked) return;
        show();
        serverKnows = false;
        write(LOCK_KEY, 'bloqueada');
        sendLock();
    }

    /** Le avisa al servidor; si no hay red, se vuelve a intentar. */
    function sendLock() {
        if (!lockRequest) {
            lockRequest = (async () => {
                const response = await post(urls.lock);
                if (response && response.status === 204) serverKnows = true;
                else if (response && response.type === 'opaqueredirect') sessionEnded();
                lockRequest = null;
            })();
        }
        return lockRequest;
    }

    // ---------- Actividad ----------

    /** Avisa que hay uso. Si el servidor ya tiene la sesión bloqueada, responde 423 y se tapa. */
    async function report() {
        lastReport = Date.now();
        const upTo = latestActivity();
        const response = await post(urls.activity);
        if (!response) return 0;
        if (response.status === 423) {
            show();
            serverKnows = true;
        } else if (response.status === 204) {
            reported = Math.max(reported, upTo);
        }
        return response.status;
    }

    function onActivity() {
        if (locked) return;
        // Primero se revisa el tiempo: al volver de una suspensión, la primera tecla no debe "salvar" la sesión.
        if (isIdle()) {
            lock();
            return;
        }
        lastActivity = Date.now();
        if (lastActivity - lastShared >= 1000) {
            lastShared = lastActivity;
            write(ACTIVITY_KEY, String(lastActivity));
        }
    }

    function tick() {
        if (locked) {
            if (!serverKnows) sendLock();
            if (!unlocking && !form.classList.contains('d-none') && Date.now() - lastTyping >= FORM_TIMEOUT_MS) hideForm();
            return;
        }
        if (isIdle()) {
            lock();
        } else if (idleMs > 0 && latestActivity() > reported && Date.now() - lastReport >= REPORT_EVERY_MS) {
            report();
        }
    }

    ['keydown', 'pointerdown', 'pointermove', 'wheel', 'touchstart'].forEach(type =>
        window.addEventListener(type, onActivity, { capture: true, passive: true }));
    setInterval(tick, 1000);

    // ---------- Con la pantalla tapada ----------

    window.addEventListener('keydown', e => {
        if (!locked) return;
        if (e.key === 'Escape') e.preventDefault();
        // Nada de lo que se escriba fuera de la capa llega a la página.
        if (!dialog.contains(e.target)) {
            e.preventDefault();
            e.stopImmediatePropagation();
        }
    }, true);

    // Un <dialog> se puede cerrar con Escape: mientras esté bloqueada, no.
    dialog.addEventListener('cancel', e => e.preventDefault());
    dialog.addEventListener('close', () => {
        if (locked) dialog.showModal();
    });

    // El campo no aparece hasta tocar el botón: así lo que escanee el lector no se envía como contraseña.
    showButton.addEventListener('click', () => {
        showButton.classList.add('d-none');
        form.classList.remove('d-none');
        lastTyping = Date.now();
        password.focus();
    });
    password.addEventListener('input', () => { lastTyping = Date.now(); });

    form.addEventListener('submit', async e => {
        e.preventDefault();
        if (unlocking || !password.value) return;
        unlocking = true;
        try {
            // El servidor debe tener el bloqueo ANTES de desbloquear; si no, podría llegar después.
            if (!serverKnows) await sendLock();
            if (!serverKnows) {
                showMessage('No hay conexión con el servidor. Inténtalo de nuevo.');
                return;
            }

            const response = await post(urls.unlock, new URLSearchParams({ password: password.value }));
            password.value = '';

            if (!response) {
                showMessage('No hay conexión con el servidor. Inténtalo de nuevo.');
            } else if (response.type === 'opaqueredirect') {
                sessionEnded();
            } else if (!response.ok) {
                showMessage('No se pudo desbloquear (error ' + response.status + ').');
            } else {
                const result = await response.json();
                if (result.isSuccess) {
                    hide();
                    write(LOCK_KEY, 'libre:' + Date.now());
                    return;
                }
                showMessage(result.message);
            }
            lastTyping = Date.now();
            password.focus();
        } finally {
            unlocking = false;
        }
    });

    // ---------- Pestañas, suspensión y botón ----------

    window.addEventListener('storage', async e => {
        if (e.key !== LOCK_KEY || !e.newValue) return;
        if (e.newValue === 'bloqueada') {
            show();
        } else if (locked && await report() === 204) {
            // Otra pestaña dice que se desbloqueó: se destapa solo si el servidor lo confirma.
            hide();
        }
    });

    function checkAfterReturning() {
        if (!locked && isIdle()) lock();
    }
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') checkAfterReturning();
    });
    // Al volver con "Atrás", el navegador puede mostrar una página guardada de antes del bloqueo.
    window.addEventListener('pageshow', e => {
        if (!e.persisted) return;
        checkAfterReturning();
        if (!locked) report();
    });

    // Si quedó la marca de bloqueada pero el servidor entregó esta página, se aclara a las demás pestañas.
    if (read(LOCK_KEY) === 'bloqueada') {
        report().then(status => {
            if (status === 204) write(LOCK_KEY, 'libre:' + Date.now());
        });
    }
    write(ACTIVITY_KEY, String(lastActivity));

    document.querySelectorAll('[data-lock-now]').forEach(button => button.addEventListener('click', e => {
        e.preventDefault();
        lock();
    }));

    // Para las pantallas que reciben un 423 en sus propias peticiones.
    window.sessionLock = {
        showFromServer() {
            show();
            serverKnows = true;
            write(LOCK_KEY, 'bloqueada');
        }
    };
})();
