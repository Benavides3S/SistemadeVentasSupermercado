// Punto de venta: buscador, carrito y cobro.
// Reglas importantes:
//  - Al servidor solo se envían ProductId y cantidad: los precios siempre salen de la base de datos.
//  - El cobro lleva el token antiforgery en el encabezado RequestVerificationToken.
//  - Los nombres se pintan con textContent (nunca innerHTML) para evitar inyección de HTML.
(function () {
    'use strict';

    const root = document.getElementById('pos');
    if (!root) return;

    const urls = {
        searchProducts: root.dataset.searchProductsUrl,
        searchClients: root.dataset.searchClientsUrl,
        create: root.dataset.createUrl,
        details: root.dataset.detailsUrl // vacío si el usuario no tiene permiso showSales
    };

    const CASH = '1'; // PaymentMethod.Cash

    const el = {
        productSearch: document.getElementById('productSearch'),
        productResults: document.getElementById('productResults'),
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
        return (...args) => {
            clearTimeout(timer);
            timer = setTimeout(() => fn(...args), ms);
        };
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

    el.productSearch.addEventListener('input', () => searchProducts(el.productSearch.value));
    el.productSearch.addEventListener('keydown', e => {
        if (e.key === 'Enter') {
            e.preventDefault();
            if (lastProductResults.length > 0) addToCart(lastProductResults[0]);
        } else if (e.key === 'Escape') {
            resetProductSearch();
        }
    });

    // ---------- Carrito ----------

    function addToCart(product) {
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
        resetProductSearch();
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

        // Solo identificadores y cantidades. Nada de precios.
        const payload = {
            clientId: selectedClient ? selectedClient.id : null,
            paymentMethod: parseInt(el.paymentMethod.value, 10),
            items: [...cart.values()].map(i => ({ productId: i.id, quantity: i.quantity }))
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

            const saleId = result.saleId;
            resetSale();

            if (window.Swal) {
                const answer = await Swal.fire({
                    icon: 'success',
                    title: 'Venta #' + result.number + ' registrada',
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
            await notify('error', 'No se pudo registrar la venta', err.message);
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
