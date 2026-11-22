# Copias de seguridad

La base de datos guarda todo: productos, ventas, turnos de caja y usuarios. Si el disco se daña, roban el computador o alguien borra algo por error, la copia de seguridad es lo único que permite recuperar el negocio. Estos scripts hacen copias completas, las verifican y borran las viejas solos.

| Script | Para qué |
|---|---|
| `scripts/backup.ps1` | Hace una copia ahora. |
| `scripts/programar-backup.ps1` | Programa la copia diaria en el Programador de tareas de Windows. |
| `scripts/restaurar.ps1` | Restaura una copia (reemplaza los datos actuales). |

Todos funcionan de dos formas:
- **Sin `-Docker`:** SQL Server instalado en Windows. Por defecto LocalDB (`(localdb)\MSSQLLocalDB`); para SQL Server Express usa `-Servidor .\SQLEXPRESS`. Entra con tu usuario de Windows, sin contraseñas.
- **Con `-Docker`:** la instalación de [docker/LEEME.md](../docker/LEEME.md). La copia se hace dentro del contenedor y se trae al computador. La contraseña de `sa` no sale del contenedor.

Los comandos se ejecutan en la carpeta raíz del repositorio.

## Hacer una copia ahora

```powershell
powershell -ExecutionPolicy Bypass -File scripts\backup.ps1            # SQL Server en Windows
powershell -ExecutionPolicy Bypass -File scripts\backup.ps1 -Docker    # Docker
```

- **Dónde queda:** en la carpeta `backups\` del repositorio (está en `.gitignore`, nunca va a git), con nombre por fecha y hora: `SistemaVentasSuperMercado_20261121_210000.bak`. Otra carpeta: `-Carpeta D:\Copias`.
- **Verificada:** después de crearla, el script comprueba que el archivo se puede leer. Si falla, borra el archivo malo.
- **Retención:** borra las copias automáticas de más de **30 días**, pero siempre deja las **7 más recientes**. Se cambia con `-DiasRetencion 60 -MinimoCopias 10`.
- **Registro:** todo queda anotado en `backups\copias.log`. Ahí debes mirar para saber si la copia de anoche funcionó.

## Programar la copia diaria

```powershell
powershell -ExecutionPolicy Bypass -File scripts\programar-backup.ps1 -Hora 21:30
powershell -ExecutionPolicy Bypass -File scripts\programar-backup.ps1 -Hora 21:30 -Docker
```

- No necesita permisos de administrador. Corre con tu usuario y solo mientras tengas la sesión de Windows iniciada (LocalDB y Docker Desktop funcionan por usuario).
- Si a esa hora el computador estaba apagado, la copia se hace apenas lo enciendas.
- Para probarla ya: `Start-ScheduledTask -TaskName 'SistemaVentas - Copia de seguridad'` y mira `backups\copias.log`.
- Para quitarla: `scripts\programar-backup.ps1 -Quitar`.
- **¿Cuántos datos puedo perder?** Lo que pasó desde la última copia. Con una copia diaria, hasta un día de ventas. Para tener varias copias al día, programa otra tarea con otro nombre: `-NombreTarea 'SistemaVentas - Copia mediodía' -Hora 13:00`.

## Una copia fuera del computador (muy importante)

Una copia que está en el mismo disco se pierde junto con el disco, o si roban el computador. Deja **siempre** otra copia fuera:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\programar-backup.ps1 -Hora 21:30 -CopiaExtra E:\CopiasSupermercado
```

`-CopiaExtra` puede ser una memoria USB, un disco externo o una carpeta sincronizada con la nube (OneDrive, Google Drive). En esa carpeta se aplica la misma retención. Si la USB no está conectada, la copia principal se hace igual y el registro muestra un ERROR por la copia extra.

**Las copias no van cifradas** (SQL Server Express no permite cifrarlas). Contienen datos personales de clientes (protegidos por la Ley 1581 de 2012) y los usuarios del sistema:
- guarda la USB en un lugar con llave;
- en la nube, protege la cuenta con verificación en dos pasos;
- nunca envíes una copia por correo o WhatsApp.

## Restaurar una copia

**Esto reemplaza todos los datos actuales** por los de la copia. Lo que se registró después de esa copia se pierde.

1. **Cierra la aplicación** en todas las cajas (con `-Docker`, el script la apaga y la vuelve a encender solo).
2. Ejecuta, con el archivo que quieres recuperar:

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\restaurar.ps1 -Archivo backups\SistemaVentasSuperMercado_20261121_210000.bak
   powershell -ExecutionPolicy Bypass -File scripts\restaurar.ps1 -Docker -Archivo backups\SistemaVentasSuperMercado_20261121_210000.bak
   ```

3. El script hace, en orden:
   - comprueba el archivo;
   - te pide escribir el nombre de la base para confirmar;
   - **guarda lo que tiene la base ahora** en `backups\<Base>_antes-de-restaurar_<fecha>.bak` (por si te equivocaste de archivo; esta copia no se borra sola);
   - restaura.

Después de restaurar:
- **Si la copia es de una versión anterior del programa,** la aplicación no arranca y avisa que faltan migraciones. Aplícalas: en Docker con `docker\aplicar-migraciones.ps1`; en desarrollo con `Update-Database`.
- **Los turnos de caja** quedan como estaban en la copia: si había uno abierto, ciérralo.

**Revisar una copia sin tocar la base real:** restáurala con otro nombre, por ejemplo `-Base CopiaParaRevisar`. Ábrela con Management Studio y bórrala cuando termines.

**Pruébalo una vez al mes.** Una copia que nunca se ha restaurado no se sabe si sirve. Restaura la copia más reciente con otro nombre y revisa que estén las ventas del día anterior.

## Cosas que conviene saber

- **Versiones de SQL Server:** una copia hecha en un SQL Server más nuevo **no se puede restaurar** en uno más viejo.
  - El LocalDB de este computador de desarrollo es SQL Server 2025 y el contenedor de Docker es SQL Server 2022, así que una copia de desarrollo no entra en Docker (el script lo detecta y no toca nada).
  - Al reemplazar el computador del negocio, instala la misma versión de SQL Server o una más nueva.
- **SQL Server Express instalado como servicio:** el archivo lo escribe el servicio de SQL Server, no tu usuario.
  - Dale permiso de escritura a la cuenta del servicio (`NT Service\MSSQL$SQLEXPRESS`) en la carpeta de copias, o usa una carpeta como `C:\CopiasSupermercado`.
  - La copia extra la hace tu usuario, así que no necesita ese permiso.
  - Con LocalDB no hace falta nada.
- **Lo que la copia NO incluye:**
  - El archivo `.env` de Docker (contraseñas). Guarda una copia aparte en un lugar seguro.
  - Los certificados HTTPS.
  - Las llaves de sesión (volumen `dp-keys`): si se pierden, solo hay que volver a iniciar sesión.
- **Docker:** mientras se hace, la copia ocupa espacio temporal dentro del volumen de la base. Se borra apenas llega al computador.
