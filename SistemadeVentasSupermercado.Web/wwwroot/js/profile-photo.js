// Foto de perfil: recorta al centro en cuadrado, reduce a 256 × 256 y envía un JPG. Dibujarla de
// nuevo también le quita los datos ocultos del archivo original (como la ubicación).
// El servidor vuelve a revisar todo: esto no es la protección.
(function () {
    const box = document.getElementById('profilePhoto');
    if (!box) return;

    const SIZE = 256;
    const MAX_SOURCE_BYTES = 15 * 1024 * 1024; // una foto de celular; algo mayor puede congelar el equipo
    const TYPES = ['image/jpeg', 'image/png', 'image/webp'];

    const input = box.querySelector('input[type="file"]');
    const current = box.querySelector('[data-photo-current]');
    const preview = box.querySelector('canvas');
    const save = box.querySelector('[data-photo-save]');
    const message = box.querySelector('[data-photo-message]');
    const token = box.querySelector('input[name="__RequestVerificationToken"]').value;

    let ready = false;
    let selection = 0; // para ignorar un archivo anterior que termine de cargar tarde

    function showMessage(text) {
        message.textContent = text;
        message.classList.toggle('d-none', !text);
    }

    function showPreview(visible) {
        ready = visible;
        preview.classList.toggle('d-none', !visible);
        current.classList.toggle('d-none', visible);
        save.classList.toggle('d-none', !visible);
    }

    input.addEventListener('change', async () => {
        const mine = ++selection;
        showPreview(false);
        showMessage('');

        const file = input.files[0];
        if (!file) return;
        if (!TYPES.includes(file.type)) {
            showMessage('Elige una foto JPG, PNG o WebP.');
            return;
        }
        if (file.size > MAX_SOURCE_BYTES) {
            showMessage('Ese archivo es demasiado grande (máximo 15 MB).');
            return;
        }

        let image;
        try {
            // 'from-image' respeta la orientación de las fotos tomadas con el celular.
            image = await createImageBitmap(file, { imageOrientation: 'from-image' });
        } catch {
            if (mine === selection) showMessage('No se pudo abrir esa imagen.');
            return;
        }
        if (mine !== selection) return;

        const side = Math.min(image.width, image.height);
        const context = preview.getContext('2d');
        context.fillStyle = '#fff'; // JPG no tiene transparencia
        context.fillRect(0, 0, SIZE, SIZE);
        context.imageSmoothingQuality = 'high';
        context.drawImage(image, (image.width - side) / 2, (image.height - side) / 2, side, side, 0, 0, SIZE, SIZE);
        image.close();
        showPreview(true);
    });

    save.addEventListener('click', async () => {
        if (!ready) return;
        save.disabled = true;
        showMessage('');
        try {
            const blob = await new Promise(resolve => preview.toBlob(resolve, 'image/jpeg', 0.85));
            const body = new FormData();
            body.append('photo', blob, 'foto.jpg');

            const response = await fetch(box.dataset.uploadUrl, {
                method: 'POST',
                credentials: 'same-origin',
                redirect: 'manual', // una redirección significa que la sesión ya no sirve
                headers: { 'RequestVerificationToken': token },
                body: body
            });

            if (response.status === 423) {
                if (window.sessionLock) window.sessionLock.showFromServer();
                showMessage('La pantalla estaba bloqueada. Desbloquéala y vuelve a guardar.');
            } else if (response.type === 'opaqueredirect') {
                showMessage('Tu sesión terminó. Recarga la página y vuelve a entrar.');
            } else if (!response.ok) {
                showMessage('No se pudo guardar la foto (error ' + response.status + ').');
            } else {
                const result = await response.json();
                if (result.isSuccess) {
                    window.location.reload();
                    return;
                }
                showMessage(result.message);
            }
        } catch {
            showMessage('No hay conexión con el servidor. Inténtalo de nuevo.');
        }
        save.disabled = false;
    });
})();
