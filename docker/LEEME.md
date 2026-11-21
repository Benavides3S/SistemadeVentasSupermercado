# Instalación con Docker

Docker empaqueta la aplicación y SQL Server en "contenedores", así el computador del negocio no necesita tener instalado .NET ni SQL Server.

| Pieza | Qué es |
|---|---|
| `SistemadeVentasSupermercado.Web/Dockerfile` | Receta de la imagen de la aplicación (compila y empaqueta). |
| `docker-compose.yml` | Une la aplicación (`web`) y SQL Server (`db`). |
| `docker-compose.https.yml` | Opcional: activa HTTPS. |
| `.env.example` → `.env` | Contraseñas y datos del negocio. **`.env` nunca va a git.** |
| `docker/migraciones.sql` | Script que crea o actualiza las tablas. |
| `docker/aplicar-migraciones.ps1` | Aplica ese script dentro del contenedor de SQL Server. |

Los datos viven en **volúmenes** de Docker (`db-data`: la base de datos; `dp-keys`: llaves de la sesión). Borrar o recrear los contenedores **no** borra los volúmenes. `docker compose down -v` **sí los borra**: no lo uses en un negocio.

## Requisitos

- **Docker Desktop** (Windows, con WSL 2) o Docker Engine (Linux).
- Procesador **x86-64** (Intel/AMD). La imagen de SQL Server no funciona en procesadores ARM.
- Al menos **4 GB de RAM** libres (SQL Server necesita 2 GB).

## Primera instalación

Todos los comandos se ejecutan en la carpeta raíz del repositorio.

1. **Configuración.** Copia `.env.example` como `.env` y llena los valores (contraseña de `sa`, contraseña inicial, datos del negocio).

2. **Script de migraciones.** Si `docker/migraciones.sql` no existe o le faltan migraciones nuevas, genéralo en Visual Studio, en la *Consola del Administrador de paquetes* (proyecto Web seleccionado):

   ```powershell
   Script-Migration -Idempotent -Output "<ruta del repositorio>\docker\migraciones.sql"
   ```

   "Idempotente" significa que el script revisa qué migraciones ya tiene la base y solo aplica las que faltan.

3. **Base de datos:**

   ```powershell
   powershell -ExecutionPolicy Bypass -File docker\aplicar-migraciones.ps1
   ```

   Enciende SQL Server, crea la base si no existe, aplica el script y muestra la lista de migraciones aplicadas.

4. **Aplicación:**

   ```powershell
   docker compose up -d --build
   ```

   Abre `http://localhost:8080` (o `http://<ip-del-servidor>:8080` desde las cajas). La primera vez se crean los permisos, el rol Admin y los usuarios iniciales con la contraseña de `SEED_PASSWORD_INICIAL`. Después de ese primer arranque puedes borrar ese valor de `.env`.

   Si la aplicación no arranca, mira el motivo con `docker compose logs web`. Si dice "La base de datos no está actualizada", repite los pasos 2 y 3.

## Actualizar a una versión nueva

1. Trae el código nuevo (`git pull`).
2. **Haz una copia de seguridad** de la base de datos.
3. Si hay migraciones nuevas: regenera `docker/migraciones.sql` (paso 2) y aplícalo (paso 3).
4. `docker compose up -d --build`

## HTTPS (recomendado en un negocio)

Sin HTTPS, la contraseña de cada cajero viaja sin cifrar por la red del local. Para activarlo:

1. Crea un certificado para el nombre o la IP del servidor. Ejemplo con PowerShell **como administrador** (cambia `192.168.1.50`, `SERVIDOR-CAJA` y la contraseña):

   ```powershell
   $cert = New-SelfSignedCertificate -DnsName "SERVIDOR-CAJA", "localhost" -TextExtension @("2.5.29.17={text}IPAddress=192.168.1.50&DNS=SERVIDOR-CAJA") -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(3)
   New-Item -ItemType Directory -Force certificados
   Export-PfxCertificate -Cert $cert -FilePath certificados\certificado.pfx -Password (Read-Host -AsSecureString "Contraseña del certificado")
   Export-Certificate -Cert $cert -FilePath certificados\certificado.cer
   ```

2. En `.env` agrega `CERT_PASSWORD=<la contraseña del certificado>` (y si quieres, `APP_HTTPS_PORT=8443`).
3. Enciende con los dos archivos:

   ```powershell
   docker compose -f docker-compose.yml -f docker-compose.https.yml up -d --build
   ```

4. En cada computador de caja, instala `certificado.cer` en *Entidades de certificación raíz de confianza*. Si no lo haces, el navegador mostrará una advertencia.

La carpeta `certificados/` está en `.gitignore`: **nunca subas el `.pfx` a git**.

## Comandos útiles

| Para | Comando |
|---|---|
| Ver si están encendidos | `docker compose ps` |
| Ver mensajes de la aplicación | `docker compose logs -f web` |
| Apagar (sin borrar datos) | `docker compose stop` |
| Encender | `docker compose up -d` |
| Conectarse con Management Studio | servidor `localhost,14330`, usuario `sa` |

Los contenedores tienen `restart: unless-stopped`: vuelven a encender solos cuando reinicia el computador, siempre que Docker Desktop arranque con Windows (*Settings → General → Start Docker Desktop when you sign in*).

## Seguridad: qué saber

- **`.env` tiene la contraseña de `sa`** (administrador total de SQL Server). Protege ese archivo y el computador servidor.
- La aplicación se conecta como `sa`. Mejora futura: un usuario de SQL con permisos limitados a esta base.
- SQL Server solo se publica en `127.0.0.1` (este computador); las cajas no pueden conectarse a la base, solo a la aplicación.
- Las llaves de sesión (volumen `dp-keys`) se guardan sin cifrar dentro del volumen. Quien tenga acceso a Docker en el servidor podría leerlas: no des acceso al servidor a personas que no lo necesitan.
- Copias de seguridad: pendiente (siguiente paso del plan).
