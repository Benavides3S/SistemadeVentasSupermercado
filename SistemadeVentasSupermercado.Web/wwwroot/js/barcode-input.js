// Campos con data-barcode-input: el lector de código de barras "escribe" el código y oprime Enter.
// En un formulario, ese Enter no debe enviarlo: se pasa al siguiente campo.
(function () {
    document.addEventListener('keydown', e => {
        const input = e.target;
        if (e.key !== 'Enter' || !(input instanceof HTMLInputElement) || input.dataset.barcodeInput === undefined) return;

        e.preventDefault();
        if (!input.form) return;
        const fields = Array.from(input.form.elements)
            .filter(f => !f.disabled && f.type !== 'hidden' && f.tabIndex !== -1);
        const next = fields[fields.indexOf(input) + 1];
        if (next) next.focus();
    });
})();
