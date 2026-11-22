# Imagen de la aplicación web. Se construye desde la carpeta raíz del repositorio:
#   docker compose build      (ver docker/LEEME.md)

# ---------- Etapa 1: compilar ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Primero solo el .csproj: si no cambian los paquetes, Docker reutiliza la descarga (restore).
COPY SistemadeVentasSupermercado.Web/SistemadeVentasSupermercado.Web.csproj SistemadeVentasSupermercado.Web/
RUN dotnet restore SistemadeVentasSupermercado.Web/SistemadeVentasSupermercado.Web.csproj

COPY SistemadeVentasSupermercado.Web/ SistemadeVentasSupermercado.Web/
RUN dotnet publish SistemadeVentasSupermercado.Web/SistemadeVentasSupermercado.Web.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Etapa 2: ejecutar ----------
# Imagen Debian (no Alpine): trae ICU (formato es-CO) y tzdata (zona America/Bogota).
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Carpeta para las llaves de Data Protection (se monta un volumen encima).
# Debe pertenecer al usuario sin privilegios "app" que trae la imagen.
RUN mkdir -p /keys && chown $APP_UID /keys

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/keys
EXPOSE 8080

# No correr como root.
USER $APP_UID

ENTRYPOINT ["dotnet", "SistemadeVentasSupermercado.Web.dll"]
