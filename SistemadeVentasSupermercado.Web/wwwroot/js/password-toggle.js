// Botón de ojo para ver u ocultar la contraseña en todos los campos type="password".
// Se carga en _Scripts (layout principal y login). Para excluir un campo: data-no-toggle.
(function () {
    function addToggle(input) {
        if (input.dataset.noToggle !== undefined || input.dataset.toggleReady) return;
        input.dataset.toggleReady = '1';

        // En .form-floating no se puede envolver el input (se rompe la etiqueta flotante): se usa el contenedor.
        let container = input.parentElement;
        if (!container.classList.contains('form-floating')) {
            const wrapper = document.createElement('div');
            input.parentNode.insertBefore(wrapper, input);
            wrapper.appendChild(input);
            container = wrapper;
        }
        container.classList.add('position-relative');
        input.style.paddingRight = '2.75rem';

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-link text-secondary position-absolute end-0 translate-middle-y me-1 px-2';
        button.style.zIndex = '5';
        // Centrado sobre el input (no sobre el contenedor, que crece si aparece un mensaje de validación debajo).
        const place = () => { button.style.top = (input.offsetTop + input.offsetHeight / 2) + 'px'; };
        window.addEventListener('resize', place);
        button.tabIndex = -1; // Tab pasa directo al siguiente campo
        container.appendChild(button);
        place();

        function render() {
            const visible = input.type === 'text';
            button.innerHTML = visible ? '<i class="bi bi-eye-slash"></i>' : '<i class="bi bi-eye"></i>';
            button.title = visible ? 'Ocultar contraseña' : 'Mostrar contraseña';
            button.setAttribute('aria-label', button.title);
            button.setAttribute('aria-pressed', visible ? 'true' : 'false');
        }

        button.addEventListener('click', () => {
            input.type = input.type === 'password' ? 'text' : 'password';
            render();
            input.focus();
        });

        // Al enviar se vuelve a ocultar, para que el navegador la trate como contraseña (no la guarde como texto).
        if (input.form) {
            input.form.addEventListener('submit', () => {
                input.type = 'password';
                render();
            });
        }

        render();
    }

    function init() {
        document.querySelectorAll('input[type="password"]').forEach(addToggle);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
