// Copia el texto de cada encabezado (<th>) como data-label en las celdas de su columna,
// para que tablas-moviles.css pueda mostrar "Etiqueta: valor" en pantallas angostas.
(function () {
    document.querySelectorAll('table.tabla-apilable').forEach(function (tabla) {
        var cabeceras = Array.prototype.map.call(
            tabla.querySelectorAll('thead th'),
            function (th) { return th.textContent.trim(); });

        tabla.querySelectorAll('tbody tr').forEach(function (fila) {
            Array.prototype.forEach.call(fila.children, function (celda, i) {
                if (celda.tagName !== 'TD') return;
                celda.setAttribute('data-label', cabeceras[i] || '');
                if (!celda.children.length && celda.textContent.trim() === '' && cabeceras[i]) {
                    celda.classList.add('celda-vacia');
                }
            });
        });
    });
})();
