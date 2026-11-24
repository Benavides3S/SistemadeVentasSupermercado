// Registro de compras a proveedores: buscador, lista de productos y guardado.
// Reglas importantes:
//  - Al servidor se envían ProductId, cantidad y costo por línea (el costo SÍ lo escribe quien compra:
//    es lo que cobró el proveedor). Quién registra la compra lo decide el servidor, no el navegador.
//  - El registro lleva el token antiforgery en el encabezado RequestVerificationToken.
//  - Cada registro lleva un código único (idempotencyKey) que se conserva hasta que la compra queda
//    guardada: si la red falla y se reintenta, el servidor no suma el stock dos veces.
//  - Los nombres se pintan con textContent (nunca innerHTML) para evitar inyección de HTML.
//  - Lector de código de barras: "escribe" el código y oprime Enter (igual que en el punto de venta).
(function () {
    'use strict';

    const root = document.getElementById('purchase');
    if (!root) return;

    const urls = {
        searchProducts: root.dataset.searchProductsUrl,
        findBarcode: root.dataset.findBarcodeUrl,
        create: root.dataset.createUrl,
        details: root.dataset.detailsUrl // vacío si el usuario no tiene permiso showPurchases
    };

    const el = {
        productSearch: document.getElementById('productSearch'),
        productResults: document.getElementById('productResults'),
        scanMessage: document.getElementById('scanMessage'),
        cartBody: document.getElementById('cartBody'),
        cartEmpty: document.getElementById('cartEmpty'),
        cartTotal: document.getElementById('cartTotal'),
        supplier: document.getElementById('supplier'),
        invoice: document.getElementById('invoice'),
        notes: document.getElementById('notes'),
        btnSave: document.getElementById('btnSave'),
        btnClear: document.getElementById('btnClear')
    };

    // ---------- Estado ----------

    /** productId → { id, name, unitAbbreviation, allowsDecimals, stock, quantity, cost } (cost: número o null) */
    const cart = new Map();
    let lastProductResults = [];
    let saving = false;
    let pendingKey = null; // código del registro en curso; se borra al guardar la compra o vaciarla

    // ---------- Formatos ----------

    const money = new Intl.NumberFormat('es-CO', {
        style: 'currency', currency: 'COP', minimumFractionDigits: 0, maximumFractionDigits: 2
    });
    const qtyFormat = new Intl.NumberFormat('es-CO', { maximumFractionDigits: 3 });

    const round = (value, decimals) => {
        const f = Math.pow(10, decimals);
        return Math.round((value + Number.EPSILON) * f) / f;
    };

    const lineTotal = item => round((item.cost || 0) * item.quantity, 2);
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

    // Pitido de error (Web Audio, sin archivos).
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
     * o en localhost, y la app puede abrirse por la red local con http://.
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
            empty.textContent = 'Sin resultados.';
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
            const cost = p.cost == null ? 'sin costo previo' : 'último costo ' + money.format(p.cost);
            info.textContent = cost + ' · stock ' + qtyFormat.format(p.stock) + ' ' + p.unitAbbreviation;

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

            if (lookup.item) {
                addToCart(lookup.item, { keepSearch: true });
                showScanMessage('✓ ' + lookup.item.name, false);
                return;
            }

            // No es un código: se busca por nombre y se agrega el primero.
            const data = await fetchJson(urls.searchProducts + '?term=' + encodeURIComponent(term));
            const items = data.items || [];
            if (items.length > 0) {
                addToCart(items[0], { keepSearch: true });
                showScanMessage('✓ ' + items[0].name, false);
            } else {
                beep();
                showScanMessage('No hay ningún producto con el código o nombre "' + term + '".', true);
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

    // ---------- Productos de la compra ----------

    /** keepSearch: no borrar el buscador (al escanear ya se limpió y puede estar llegando el siguiente código). */
    function addToCart(product, { keepSearch = false } = {}) {
        const existing = cart.get(product.id);
        if (existing) {
            existing.quantity = round(existing.quantity + 1, 3);
        } else {
            cart.set(product.id, {
                id: product.id,
                name: product.name,
                unitAbbreviation: product.unitAbbreviation,
                allowsDecimals: product.allowsDecimals,
                stock: product.stock,
                quantity: 1,
                cost: product.cost == null ? null : product.cost // se precarga el último costo
            });
        }
        if (keepSearch) {
            lastProductResults = [];
            clearResults(el.productResults);
        } else {
            resetProductSearch();
        }
        renderCart();

        // Producto nuevo: el cursor va a la cantidad (por peso) o al costo si no hay costo previo.
        const item = cart.get(product.id);
        const focusCost = !existing && item.cost == null;
        const selector = (focusCost ? 'input[data-cost-id="' : 'input[data-qty-id="') + CSS.escape(product.id) + '"]';
        if (!existing && (focusCost || product.allowsDecimals)) {
            const input = el.cartBody.querySelector(selector);
            if (input) { input.focus(); input.select(); }
        }
    }

    /** Devuelve un mensaje de error si la línea no es válida, o null si está bien. */
    function lineError(item) {
        if (!(item.quantity > 0)) return 'La cantidad debe ser mayor que cero';
        if (!item.allowsDecimals && !Number.isInteger(item.quantity)) return 'Este producto se maneja por unidad: usa números enteros';
        if (!(item.cost > 0)) return 'Escribe el costo (mayor que cero)';
        return null;
    }

    function renderCart() {
        el.cartBody.replaceChildren();

        for (const item of cart.values()) {
            const tr = document.createElement('tr');

            const tdName = document.createElement('td');
            tdName.textContent = item.name;
            const stockInfo = document.createElement('div');
            stockInfo.className = 'small text-muted';
            stockInfo.textContent = 'Stock actual: ' + qtyFormat.format(item.stock) + ' ' + item.unitAbbreviation;
            tdName.append(stockInfo);

            // Cantidad
            const tdQty = document.createElement('td');
            const qtyGroup = document.createElement('div');
            qtyGroup.className = 'input-group input-group-sm';
            const qtyInput = document.createElement('input');
            qtyInput.type = 'number';
            qtyInput.className = 'form-control';
            qtyInput.min = '0';
            qtyInput.step = item.allowsDecimals ? 'any' : '1';
            qtyInput.value = String(item.quantity);
            qtyInput.dataset.qtyId = item.id;
            const unit = document.createElement('span');
            unit.className = 'input-group-text';
            unit.textContent = item.unitAbbreviation;
            qtyGroup.append(qtyInput, unit);
            tdQty.append(qtyGroup);

            // Costo por unidad
            const tdCost = document.createElement('td');
            const costGroup = document.createElement('div');
            costGroup.className = 'input-group input-group-sm';
            const currency = document.createElement('span');
            currency.className = 'input-group-text';
            currency.textContent = '$';
            const costInput = document.createElement('input');
            costInput.type = 'number';
            costInput.className = 'form-control';
            costInput.min = '0';
            costInput.step = 'any';
            costInput.value = item.cost == null ? '' : String(item.cost);
            costInput.dataset.costId = item.id;
            costGroup.append(currency, costInput);
            const feedback = document.createElement('div');
            feedback.className = 'invalid-feedback d-block small';
            tdCost.append(costGroup, feedback);

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
                const quantityBad = !(item.quantity > 0) || (!item.allowsDecimals && !Number.isInteger(item.quantity));
                const costBad = !(item.cost > 0);
                qtyInput.classList.toggle('is-invalid', quantityBad);
                costInput.classList.toggle('is-invalid', costBad);
                feedback.textContent = lineError(item) || '';
                tdTotal.textContent = money.format(lineTotal(item));
            };

            // Los campos son type="number": el valor siempre llega con punto decimal.
            qtyInput.addEventListener('input', () => {
                const value = parseFloat(qtyInput.value);
                item.quantity = Number.isFinite(value) ? round(value, 3) : 0;
                refreshLine();
                refreshTotals();
            });
            costInput.addEventListener('input', () => {
                const value = parseFloat(costInput.value);
                item.cost = Number.isFinite(value) ? round(value, 2) : null;
                refreshLine();
                refreshTotals();
            });
            for (const input of [qtyInput, costInput]) {
                input.addEventListener('keydown', e => {
                    if (e.key === 'Enter') { e.preventDefault(); el.productSearch.focus(); }
                });
            }

            refreshLine();
            tr.append(tdName, tdQty, tdCost, tdTotal, tdRemove);
            el.cartBody.append(tr);
        }

        el.cartEmpty.classList.toggle('d-none', cart.size > 0);
        refreshTotals();
    }

    function purchaseIsValid() {
        return cart.size > 0
            && el.supplier.value !== ''
            && [...cart.values()].every(i => lineError(i) === null);
    }

    function refreshTotals() {
        el.cartTotal.textContent = money.format(cartTotal());
        el.btnSave.disabled = saving || !purchaseIsValid();
        el.btnClear.disabled = saving || (cart.size === 0 && !el.invoice.value && !el.notes.value);
    }

    el.supplier.addEventListener('change', refreshTotals);
    el.invoice.addEventListener('input', refreshTotals);
    el.notes.addEventListener('input', refreshTotals);

    el.btnClear.addEventListener('click', async () => {
        if (window.Swal) {
            const answer = await Swal.fire({
                icon: 'question', title: '¿Vaciar la compra?', showCancelButton: true,
                confirmButtonText: 'Sí, vaciar', cancelButtonText: 'No'
            });
            if (!answer.isConfirmed) return;
        }
        resetPurchase();
    });

    // Cerrar la lista de resultados al hacer clic fuera de ella.
    document.addEventListener('click', e => {
        if (!el.productResults.contains(e.target) && e.target !== el.productSearch) clearResults(el.productResults);
    });

    // ---------- Guardado ----------

    function resetPurchase() {
        pendingKey = null; // la próxima compra lleva un código nuevo
        cart.clear();
        el.supplier.value = '';
        el.invoice.value = '';
        el.notes.value = '';
        renderCart();
        resetProductSearch();
    }

    el.btnSave.addEventListener('click', async () => {
        if (saving || !purchaseIsValid()) return;

        // El mismo código se reutiliza en los reintentos de esta compra (ver resetPurchase).
        if (!pendingKey) pendingKey = newGuid();

        const payload = {
            supplierId: el.supplier.value,
            supplierInvoiceNumber: el.invoice.value.trim() || null,
            notes: el.notes.value.trim() || null,
            items: [...cart.values()].map(i => ({ productId: i.id, quantity: i.quantity, unitCost: i.cost })),
            idempotencyKey: pendingKey
        };

        saving = true; // evita registros dobles por doble clic
        refreshTotals();
        const originalHtml = el.btnSave.innerHTML;
        el.btnSave.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Guardando...';

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
                // La compra se conserva en pantalla para corregir y volver a guardar.
                await notify('error', 'No se pudo registrar la compra', result.message);
                return;
            }

            let text = 'Total: ' + money.format(result.total) + '. El stock ya subió.';
            if (result.alreadyRegistered) {
                // Reintento: la compra ya había quedado en el intento anterior.
                text = 'La compra ya había quedado registrada en el intento anterior; no se sumó dos veces. ' + text;
            }
            if (result.warning) {
                text += '\n\n⚠ ' + result.warning;
            }

            const purchaseId = result.purchaseId;
            resetPurchase();

            if (window.Swal) {
                const answer = await Swal.fire({
                    icon: result.warning ? 'warning' : 'success',
                    title: 'Compra #' + result.number + (result.alreadyRegistered ? ' ya estaba registrada' : ' registrada'),
                    text: text,
                    showCancelButton: !!urls.details,
                    confirmButtonText: 'Nueva compra',
                    cancelButtonText: 'Ver detalle'
                });
                if (urls.details && answer.dismiss === Swal.DismissReason.cancel) {
                    window.location.href = urls.details + '/' + encodeURIComponent(purchaseId);
                }
            } else {
                alert('Compra #' + result.number + ' registrada. ' + text);
            }
        } catch (err) {
            // No se sabe si la compra quedó. Se conserva el código: al reintentar, el servidor
            // devuelve la compra si ya estaba registrada, o la registra si no.
            await notify('error', 'No se sabe si la compra quedó registrada',
                err.message + ' Vuelve a guardar: el sistema no sumará el stock dos veces.');
        } finally {
            saving = false;
            el.btnSave.innerHTML = originalHtml;
            refreshTotals();
            el.productSearch.focus();
        }
    });

    // ---------- Inicio ----------
    renderCart();
})();
