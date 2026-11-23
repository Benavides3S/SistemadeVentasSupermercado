// Punto de venta: buscador, carrito y cobro.
// Reglas importantes:
//  - Al servidor solo se envían ProductId y cantidad: los precios siempre salen de la base de datos.
//  - El cobro lleva el token antiforgery en el encabezado RequestVerificationToken.
//  - Cada cobro lleva un código único (idempotencyKey) que se conserva hasta que la venta
//    queda registrada: si la red falla y el cajero reintenta, el servidor no la registra dos veces.
//  - Los nombres se pintan con textContent (nunca innerHTML) para evitar inyección de HTML.
//  - Lector de código de barras: "escribe" el código y oprime Enter. El Enter busca primero el
//    código exacto en el servidor; si no es un código, agrega el primer resultado por nombre.
(function () {
    'use strict';

    const root = document.getElementById('pos');
    if (!root) return;

    const urls = {
        searchProducts: root.dataset.searchProductsUrl,
        searchClients: root.dataset.searchClientsUrl,
        findBarcode: root.dataset.findBarcodeUrl,
        create: root.dataset.createUrl,
        details: root.dataset.detailsUrl // vacío si el usuario no tiene permiso showSales
    };

    const CASH = '1'; // PaymentMethod.Cash

    const el = {
        productSearch: document.getElementById('productSearch'),
        productResults: document.getElementById('productResults'),
        scanMessage: document.getElementById('scanMessage'),
        cartBody: document.getElementById('cartBody'),
        cartEmpty: document.getElementById('cartEmpty'),
        cartTotal: document.getElementById('cartTotal'),
        clientSearch: document.getElementById('clientSearch'),
        clientSearchBox: document.getElementById('clientSearchBox'),
        clientResults: document.getElementById('clientResults'),
        clientSelected: document.getElementById('clientSelected'),
        clientSelectedName: document.getElementById('clientSelectedName'),
        clientClear: document.getElementById('clientClear'),
        paymentMethod: document.getElementById('paymentMethod'),
        cashBox: document.getElementById('cashBox'),
        cashReceived: document.getElementById('cashReceived'),
        cashChange: document.getElementById('cashChange'),
        btnCharge: document.getElementById('btnCharge'),
        btnClear: document.getElementById('btnClear')
    };

    // ---------- Estado ----------

    /** productId → { id, name, price, stock, unitAbbreviation, allowsDecimals, quantity } */
    const cart = new Map();
    let selectedClient = null; // { id, name }
    let lastProductResults = [];
    let charging = false;
    let pendingKey = null; // código del cobro en curso; se borra al registrar la venta o vaciar el carrito

    // ---------- Formatos ----------

    const money = new Intl.NumberFormat('es-CO', {
        style: 'currency', currency: 'COP', minimumFractionDigits: 0, maximumFractionDigits: 2
    });
    const qtyFormat = new Intl.NumberFormat('es-CO', { maximumFractionDigits: 3 });

    const round = (value, decimals) => {
        const f = Math.pow(10, decimals);
        return Math.round((value + Number.EPSILON) * f) / f;
    };

    const lineTotal = item => round(item.price * item.quantity, 2);
    const cartTotal = () => round([...cart.values()].reduce((sum, i) => sum + lineTotal(i), 0), 2);

    // ---------- Utilidades ----------

    function debounce(fn, ms) {
        let timer;
        const debounced = (...args) => {
            clearTimeout(timer);
            timer = setTimeout(() => fn(...args), ms);
        };
        debounced.cancel = () => clearTimeout(timer); // el Enter del lector no espera la búsqueda pendiente
        return debounced;
    }

    // Pitido de error (Web Audio, sin archivos). El cajero no siempre mira la pantalla al escanear.
    let audioContext = null;
    function beep() {
        try {
            audioContext = audioContext || new (window.AudioContext || window.webkitAudioContext)();
            const oscillator = audioContext.createOscillator();
            const gain = audioContext.createGain();
            oscillator.type = 'square';
            oscillator.frequency.value = 330;
            gain.gain.value = 0.08;
            oscillator.connect(gain);
            gain.connect(audioContext.destination);
            oscillator.start();
            oscillator.stop(audioContext.currentTime + 0.3);
        } catch {
            // Sin audio en este navegador: queda el mensaje en pantalla.
        }
    }

    // Mensaje bajo el buscador. No se usa una ventana (Swal) porque le quitaría el foco al
    // buscador y el siguiente escaneo se perdería.
    let scanMessageTimer;
    function showScanMessage(text, isError) {
        clearTimeout(scanMessageTimer);
        el.scanMessage.textContent = text;
        el.scanMessage.className = 'small fw-bold mt-1 ' + (isError ? 'text-danger' : 'text-success');
        scanMessageTimer = setTimeout(() => { el.scanMessage.textContent = ''; }, isError ? 6000 : 2500);
    }

    function notify(icon, title, text) {
        if (window.Swal) {
            return Swal.fire({ icon, title, text });
        }
        alert(title + (text ? '\n' + text : ''));
        return Promise.resolve();
    }

    /**
     * Hace la petición y devuelve el JSON. Si el servidor responde con una página
     * (sesión vencida → login, o sin permiso → error), lanza un error con un mensaje claro.
     */
    async function fetchJson(url, options) {
        let response;
        try {
            response = await fetch(url, Object.assign({ credentials: 'same-origin' }, options));
        } catch {
            throw new Error('No hay conexión con el servidor. Revisa la red e inténtalo de nuevo.');
        }

        const type = response.headers.get('content-type') || '';
        if (response.redirected || !type.includes('application/json')) {
            if (response.status === 400) {
                throw new Error('La página caducó. Recárgala (F5) e inténtalo de nuevo.');
            }
            throw new Error('Tu sesión expiró o no tienes permiso. Vuelve a iniciar sesión.');
        }
        if (!response.ok) {
            throw new Error('El servidor respondió con un error (' + response.status + ').');
        }
        return response.json();
    }

    /**
     * Guid aleatorio (versión 4). No se usa crypto.randomUUID() porque solo existe con HTTPS
     * o en localhost, y una caja puede abrir la app por la red local con http://.
     */
    function newGuid() {
        const b = crypto.getRandomValues(new Uint8Array(16));
        b[6] = (b[6] & 0x0f) | 0x40; // versión 4
        b[8] = (b[8] & 0x3f) | 0x80; // variante RFC 4122
        const h = [...b].map(x => x.toString(16).padStart(2, '0')).join('');
        return h.slice(0, 8) + '-' + h.slice(8, 12) + '-' + h.slice(12, 16) + '-' + h.slice(16, 20) + '-' + h.slice(20);
    }

    function antiforgeryToken() {
        const input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function clearResults(container) {
        container.replaceChildren();
    }

    // ---------- Buscador de productos ----------

    const searchProducts = debounce(async term => {
        if (!term.trim()) {
            lastProductResults = [];
            clearResults(el.productResults);
            return;
        }
        try {
            const data = await fetchJson(urls.searchProducts + '?term=' + encodeURIComponent(term));
            // Si mientras llegaba la respuesta el usuario cambió el texto, se ignora.
            if (term !== el.productSearch.value) return;
            lastProductResults = data.items || [];
            renderProductResults();
        } catch (err) {
            notify('error', 'No se pudo buscar', err.message);
        }
    }, 250);

    function renderProductResults() {
        clearResults(el.productResults);

        if (lastProductResults.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'list-group-item text-muted';
            empty.textContent = 'Sin resultados (o sin stock).';
            el.productResults.append(empty);
            return;
        }

        for (const p of lastProductResults) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'list-group-item list-group-item-action d-flex justify-content-between';

            const name = document.createElement('span');
            name.textContent = p.name;

            const info = document.createElement('small');
            info.className = 'text-muted';
            info.textContent = money.format(p.price) + ' / ' + p.unitAbbreviation +
                ' · stock ' + qtyFormat.format(p.stock);

            btn.append(name, info);
            btn.addEventListener('click', () => addToCart(p));
            el.productResults.append(btn);
        }
    }

    function resetProductSearch() {
        el.productSearch.value = '';
        lastProductResults = [];
        clearResults(el.productResults);
        el.productSearch.focus();
    }

    // Los escaneos se atienden en orden, uno tras otro, aunque el lector dispare varios seguidos.
    let scanQueue = Promise.resolve();

    async function handleEnter(term) {
        try {
            const lookup = await fetchJson(urls.findBarcode + '?code=' + encodeURIComponent(term));
            if (!lookup.isSuccess) throw new Error(lookup.message);

            if (lookup.status === 'Found') {
                addToCart(lookup.item, { keepSearch: true });
                showScanMessage('✓ ' + lookup.item.name, false);
                return;
            }
            if (lookup.status !== 'NotFound') {
                // El producto existe pero no se puede vender (sin stock o categoría desactivada).
                beep();
                showScanMessage(lookup.message, true);
                return;
            }

            // No es un código: se busca por nombre (lo que se hacía antes con Enter) y se agrega el primero.
            const data = await fetchJson(urls.searchProducts + '?term=' + encodeURIComponent(term));
            const items = data.items || [];
            if (items.length > 0) {
                addToCart(items[0], { keepSearch: true });
                showScanMessage('✓ ' + items[0].name, false);
            } else {
                beep();
                showScanMessage('No hay ningún producto con el código o nombre "' + term + '" (o no tiene stock).', true);
            }
        } catch (err) {
            beep();
            showScanMessage(err.message, true);
        }
    }

    el.productSearch.addEventListener('input', () => searchProducts(el.productSearch.value));
    el.productSearch.addEventListener('keydown', e => {
        if (e.key === 'Enter') {
            e.preventDefault();
            const term = el.productSearch.value.trim();
            if (!term) return;

            // Se limpia de una vez: si el lector manda otro código enseguida, entra limpio.
            searchProducts.cancel();
            el.productSearch.value = '';
            lastProductResults = [];
            clearResults(el.productResults);

            scanQueue = scanQueue.then(() => handleEnter(term));
        } else if (e.key === 'Escape') {
            resetProductSearch();
        }
    });

    // ---------- Carrito ----------

    /** keepSearch: no borrar el buscador (al escanear ya se limpió y puede estar llegando el siguiente código). */
    function addToCart(product, { keepSearch = false } = {}) {
        const existing = cart.get(product.id);
        if (existing) {
            existing.quantity = round(existing.quantity + 1, 3);
        } else {
            cart.set(product.id, {
                id: product.id,
                name: product.name,
                price: product.price,
                stock: product.stock,
                unitAbbreviation: product.unitAbbreviation,
                allowsDecimals: product.allowsDecimals,
                quantity: 1
            });
        }
        if (keepSearch) {
            lastProductResults = [];
            clearResults(el.productResults);
        } else {
            resetProductSearch();
        }
        renderCart();

        // En productos por peso se selecciona la cantidad para escribir el peso de una vez.
        if (product.allowsDecimals) {
            const input = el.cartBody.querySelector(`input[data-id="${CSS.escape(product.id)}"]`);
            if (input) { input.focus(); input.select(); }
        }
    }

    /** Devuelve un mensaje de error si la cantidad no es válida, o null si está bien. */
    function quantityError(item) {
        if (!(item.quantity > 0)) return 'La cantidad debe ser mayor que cero';
        if (!item.allowsDecimals && !Number.isInteger(item.quantity)) return 'Este producto se vende por unidad: usa números enteros';
        if (item.quantity > item.stock) return 'Solo hay ' + qtyFormat.format(item.stock) + ' ' + item.unitAbbreviation + ' en stock';
        return null;
    }

    function renderCart() {
        el.cartBody.replaceChildren();

        for (const item of cart.values()) {
            const tr = document.createElement('tr');

            const tdName = document.createElement('td');
            tdName.textContent = item.name;

            const tdPrice = document.createElement('td');
            tdPrice.className = 'text-end';
            tdPrice.textContent = money.format(item.price) + ' / ' + item.unitAbbreviation;

            const tdQty = document.createElement('td');
            const group = document.createElement('div');
            group.className = 'input-group input-group-sm';
            const input = document.createElement('input');
            input.type = 'number';
            input.className = 'form-control';
            input.min = '0';
            input.step = item.allowsDecimals ? 'any' : '1';
            input.value = String(item.quantity);
            input.dataset.id = item.id;
            const unit = document.createElement('span');
            unit.className = 'input-group-text';
            unit.textContent = item.unitAbbreviation;
            group.append(input, unit);
            const feedback = document.createElement('div');
            feedback.className = 'invalid-feedback d-block small';
            tdQty.append(group, feedback);

            const tdTotal = document.createElement('td');
            tdTotal.className = 'text-end fw-semibold';

            const tdRemove = document.createElement('td');
            const btnRemove = document.createElement('button');
            btnRemove.type = 'button';
            btnRemove.className = 'btn btn-sm btn-outline-danger';
            btnRemove.title = 'Quitar';
            btnRemove.innerHTML = '<i class="bi bi-x-lg"></i>'; // contenido fijo, sin datos del usuario
            btnRemove.addEventListener('click', () => {
                cart.delete(item.id);
                renderCart();
            });
            tdRemove.append(btnRemove);

            const refreshLine = () => {
                const error = quantityError(item);
                input.classList.toggle('is-invalid', !!error);
                feedback.textContent = error || '';
                tdTotal.textContent = money.format(lineTotal(item));
            };

            input.addEventListener('input', () => {
                // El campo es type="number": el valor siempre llega con punto decimal.
                const value = parseFloat(input.value);
                item.quantity = Number.isFinite(value) ? round(value, 3) : 0;
                refreshLine();
                refreshTotals();
            });
            input.addEventListener('keydown', e => {
                if (e.key === 'Enter') { e.preventDefault(); el.productSearch.focus(); }
            });

            refreshLine();
            tr.append(tdName, tdPrice, tdQty, tdTotal, tdRemove);
            el.cartBody.append(tr);
        }

        el.cartEmpty.classList.toggle('d-none', cart.size > 0);
        refreshTotals();
    }

    function cartIsValid() {
        return cart.size > 0 && [...cart.values()].every(i => quantityError(i) === null);
    }

    function refreshTotals() {
        const total = cartTotal();
        el.cartTotal.textContent = money.format(total);

        const isCash = el.paymentMethod.value === CASH;
        el.cashBox.classList.toggle('d-none', !isCash);

        el.cashChange.classList.remove('text-danger');
        const received = parseFloat(el.cashReceived.value);
        if (!isCash || !Number.isFinite(received)) {
            el.cashChange.textContent = '—';
        } else if (received < total) {
            el.cashChange.textContent = 'Faltan ' + money.format(round(total - received, 2));
            el.cashChange.classList.add('text-danger');
        } else {
            el.cashChange.textContent = money.format(round(received - total, 2));
        }

        el.btnCharge.disabled = charging || !cartIsValid();
        el.btnClear.disabled = charging || cart.size === 0;
    }

    el.paymentMethod.addEventListener('change', refreshTotals);
    el.cashReceived.addEventListener('input', refreshTotals);

    el.btnClear.addEventListener('click', async () => {
        if (window.Swal) {
            const answer = await Swal.fire({
                icon: 'question', title: '¿Vaciar el carrito?', showCancelButton: true,
                confirmButtonText: 'Sí, vaciar', cancelButtonText: 'No'
            });
            if (!answer.isConfirmed) return;
        }
        resetSale();
    });

    // ---------- Cliente ----------

    const searchClients = debounce(async term => {
        if (!term.trim()) { clearResults(el.clientResults); return; }
        try {
            const data = await fetchJson(urls.searchClients + '?term=' + encodeURIComponent(term));
            if (term !== el.clientSearch.value) return;
            renderClientResults(data.items || []);
        } catch (err) {
            notify('error', 'No se pudo buscar', err.message);
        }
    }, 250);

    function renderClientResults(clients) {
        clearResults(el.clientResults);

        if (clients.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'list-group-item text-muted';
            empty.textContent = 'Sin resultados.';
            el.clientResults.append(empty);
            return;
        }

        for (const c of clients) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'list-group-item list-group-item-action';
            const name = document.createElement('div');
            name.textContent = c.name;
            btn.append(name);
            if (c.email) {
                const email = document.createElement('small');
                email.className = 'text-muted';
                email.textContent = c.email;
                btn.append(email);
            }
            btn.addEventListener('click', () => selectClient(c));
            el.clientResults.append(btn);
        }
    }

    function selectClient(client) {
        selectedClient = client ? { id: client.id, name: client.name } : null;
        el.clientSearch.value = '';
        clearResults(el.clientResults);

        el.clientSelectedName.textContent = selectedClient ? selectedClient.name : '';
        el.clientSelected.classList.toggle('d-none', !selectedClient);
        el.clientSelected.classList.toggle('d-flex', !!selectedClient);
        el.clientSearchBox.classList.toggle('d-none', !!selectedClient);
    }

    el.clientSearch.addEventListener('input', () => searchClients(el.clientSearch.value));
    el.clientSearch.addEventListener('keydown', e => {
        if (e.key === 'Escape') { el.clientSearch.value = ''; clearResults(el.clientResults); }
    });
    el.clientClear.addEventListener('click', () => selectClient(null));

    // Cerrar listas de resultados al hacer clic fuera de ellas.
    document.addEventListener('click', e => {
        if (!el.productResults.contains(e.target) && e.target !== el.productSearch) clearResults(el.productResults);
        if (!el.clientResults.contains(e.target) && e.target !== el.clientSearch) clearResults(el.clientResults);
    });

    // ---------- Cobro ----------

    function resetSale() {
        pendingKey = null; // la próxima venta lleva un código nuevo
        cart.clear();
        selectClient(null);
        el.cashReceived.value = '';
        renderCart();
        resetProductSearch();
    }

    el.btnCharge.addEventListener('click', async () => {
        if (charging || !cartIsValid()) return;

        const total = cartTotal();
        const isCash = el.paymentMethod.value === CASH;
        const received = parseFloat(el.cashReceived.value);
        if (isCash && Number.isFinite(received) && received < total) {
            notify('warning', 'Efectivo insuficiente', 'Lo recibido es menor que el total.');
            return;
        }

        // El mismo código se reutiliza en los reintentos de este cobro (ver resetSale).
        if (!pendingKey) pendingKey = newGuid();

        // Solo identificadores y cantidades. Nada de precios.
        const payload = {
            clientId: selectedClient ? selectedClient.id : null,
            paymentMethod: parseInt(el.paymentMethod.value, 10),
            items: [...cart.values()].map(i => ({ productId: i.id, quantity: i.quantity })),
            idempotencyKey: pendingKey
        };

        charging = true; // evita cobros dobles por doble clic
        refreshTotals();
        const originalHtml = el.btnCharge.innerHTML;
        el.btnCharge.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Cobrando...';

        try {
            const result = await fetchJson(urls.create, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': antiforgeryToken()
                },
                body: JSON.stringify(payload)
            });

            if (!result.isSuccess) {
                // El carrito se conserva para corregir y volver a cobrar.
                await notify('error', 'No se pudo registrar la venta', result.message);
                return;
            }

            let text = 'Total: ' + money.format(result.total);
            if (isCash && Number.isFinite(received)) {
                text += ' · Cambio: ' + money.format(round(received - result.total, 2));
            }
            if (result.alreadyRegistered) {
                // Reintento: la venta ya había quedado en el intento anterior. El total es el
                // registrado, que puede no coincidir con el carrito si se cambió después del error.
                text = 'La venta ya había quedado registrada en el intento anterior; no se cobró dos veces. ' + text;
            }

            const saleId = result.saleId;
            resetSale();

            if (window.Swal) {
                const answer = await Swal.fire({
                    icon: 'success',
                    title: 'Venta #' + result.number + (result.alreadyRegistered ? ' ya estaba registrada' : ' registrada'),
                    text: text,
                    showCancelButton: !!urls.details,
                    confirmButtonText: 'Nueva venta',
                    cancelButtonText: 'Ver comprobante'
                });
                if (urls.details && answer.dismiss === Swal.DismissReason.cancel) {
                    window.location.href = urls.details + '/' + encodeURIComponent(saleId);
                }
            } else {
                alert('Venta #' + result.number + ' registrada. ' + text);
            }
        } catch (err) {
            // No se sabe si la venta quedó. Se conserva el código: al reintentar, el servidor
            // devuelve la venta si ya estaba registrada, o la registra si no.
            await notify('error', 'No se sabe si la venta quedó registrada',
                err.message + ' Vuelve a cobrar: el sistema no registrará la venta dos veces.');
        } finally {
            charging = false;
            el.btnCharge.innerHTML = originalHtml;
            refreshTotals();
            el.productSearch.focus();
        }
    });

    // ---------- Inicio ----------
    renderCart();
})();
