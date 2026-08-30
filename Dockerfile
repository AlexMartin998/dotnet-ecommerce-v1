# syntax=docker/dockerfile:1

# ---- build ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Se copia SOLO el .csproj primero: mientras no cambien las dependencias, Docker
# reutiliza la capa del restore y no vuelve a bajar los paquetes en cada build.
COPY ApiEcommerce.csproj .
RUN dotnet restore ApiEcommerce.csproj

COPY . .
# Se nombra el .csproj explícitamente: el .sln también llega al contexto, y
# `dotnet publish` sin argumento lo resuelve a él, emitiendo
# `NETSDK1194: --output no se admite al compilar una solución`.
# (El código de referencia del curso ya lo excluye .dockerignore.)
RUN dotnet publish ApiEcommerce.csproj -c Release -o /app --no-restore

# ---- runtime ----------------------------------------------------------------
# Imagen `aspnet` (no `sdk`): ~110 MB en vez de ~800 MB. En la imagen final no
# tiene que haber compilador — menos peso y menos superficie de ataque.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl NO viene en la imagen aspnet: su base (runtime-deps) solo instala
# ca-certificates, libc6, libgcc-s1, libicu, libssl y tzdata. Sin instalarlo, el
# HEALTHCHECK de abajo resolvería `curl: not found` (exit 127) y el contenedor
# quedaría `unhealthy` PARA SIEMPRE — bloqueando cualquier `depends_on:
# condition: service_healthy` que apunte a este servicio.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# Carpeta de imágenes subidas. Se monta como volumen en compose: si no, cada
# redespliegue del contenedor borra las imágenes de los productos.
#
# Se usa $APP_UID, el usuario no-root que la imagen base YA crea (uid 1654) con
# `--create-home`. Crear otro usuario a mano con `--no-create-home` dejaba al proceso
# sin HOME, y entonces DataProtection no puede persistir su key ring: los tokens de
# Identity (reset de contraseña, confirmación de email) se invalidan en cada reinicio
# y no valen entre réplicas.
RUN mkdir -p /app/wwwroot/ProductsImages && chown -R $APP_UID:$APP_UID /app/wwwroot

USER $APP_UID

# ASPNETCORE_URLS no se fija: la imagen base ya define ASPNETCORE_HTTP_PORTS=8080, y
# ponerlo genera un WRN "Overriding HTTP_PORTS" en cada arranque.
# DOTNET_gcServer tampoco: el SDK Web ya emite System.GC.Server=true en el
# runtimeconfig, y forzar Server GC con poca memoria dispara el RSS.
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# Sonda de liveness: no toca base ni Redis a propósito (ver HealthController).
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/health || exit 1"]

ENTRYPOINT ["dotnet", "ApiEcommerce.dll"]
