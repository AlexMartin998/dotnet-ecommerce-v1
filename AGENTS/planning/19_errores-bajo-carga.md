# 19 — Los «errores» que no eran errores

> Lo último que quedaba abierto de `planning/17` §17.4. Sale de las pruebas de carga del
> 2026-09-06: el log se llenaba de incidentes falsos justo cuando más falta hace leerlo.

---

## 19.1 Un cliente que cuelga no es un fallo del servidor

Cuando un cliente corta la conexión a mitad, ASP.NET Core cancela `RequestAborted`; EF
cancela el `SqlCommand` en vuelo y SqlClient lanza un **`SqlException`** («A severe error
occurred on the current command» / «Operation cancelled by user», con un Win32 258
dentro). Eso **no** es una `OperationCanceledException` —que sí estaba mapeada a 499— así
que llegaba al final del pipeline como una excepción cualquiera: **500, nivel Error y traza
completa**.

El coste no es la respuesta —no hay nadie al otro lado— sino el **ruido**: las métricas de
error y el log se llenan de incidentes falsos, y un fallo real queda sepultado.

- [x] **`ClientAbortMiddleware`**: si `RequestAborted` está cancelado, la excepción se
      absorbe, se registra a **Information y sin traza**, y la petición queda en **499**
      (*Client Closed Request*: no es del RFC pero es la convención de facto —la de nginx—
      y es lo que hace que no cuente como 5xx en las métricas).
- [x] Se decide por el **estado de la petición**, no por el tipo de la excepción. La
      cancelación se propaga distinto según dónde pille (EF, el cliente AMQP, `HttpClient`,
      Kestrel leyendo el cuerpo) y perseguir cada tipo es una lista que nunca está completa.
      De hecho `SqlException` **no se puede construir en un test** —no tiene constructor
      público—, lo que ya dice bastante sobre perseguir tipos concretos.
- [x] ⚠️ **Va por DEBAJO de `UseExceptionHandler`, y el orden es lo único que lo hace
      funcionar.** El middleware de diagnóstico del framework escribe su «An unhandled
      exception has occurred» a nivel Error **antes** de llamar a ningún
      `IExceptionHandler`: decidirlo desde `GlobalExceptionHandler` llega tarde, la línea
      de Error ya está escrita. Se probó así primero y no servía.

## 19.2 Un timeout de base no es un 500

`SqlException` número **-2** es el timeout de comando (el Win32 258 que se ve dentro es
`WAIT_TIMEOUT`). No es un bug nuestro ni una petición mal formada: la base no llegó a
tiempo, casi siempre por contención. En las pruebas de carga salieron 83 como error interno.

- [x] **503** (`database_timeout`) **+ `Retry-After`**, porque **es reintentable** y el
      cliente necesita saberlo — un 500 le dice justo lo contrario. Es la misma idea que la
      cabecera `transient-error` de Adyen: no basta con rechazar, hay que decir si el
      rechazo es transitorio.
- [x] **El `Detail` de un 5xx mapeado deja de censurarse.** La regla era «≥500 → mensaje
      genérico», pensada para excepciones no controladas. Un 503 que escribimos nosotros no
      revela nada del servidor y su texto es accionable; censurarlo dejaba al cliente sin
      saber si podía reintentar. El corte pasa a ser **«¿lo mapeamos nosotros?»**, no
      «¿es 5xx?».
      ⚠️ Al hacerlo rompí el `Detail` de los 4xx —donde el mensaje de dominio
      («Insufficient stock for SKU 'X'») es justo el útil— y **lo cazó un test que ya
      existía**. Corregido: sólo cambia el caso de los 5xx mapeados.

---

## 19.3 Verificación

- [x] `dotnet build -warnaserror` sin warnings · **186 tests** en verde (eran 183).
- [x] **Ejecutando**: 180 peticiones abortadas a mitad *durante* el trabajo de base de
      datos, con 80 hilos de carga de fondo para que la cancelación pillara a SqlClient en
      vuelo.

| | Antes | Después |
|---|---|---|
| «An unhandled exception has occurred» | **27** | **0** |
| `SqlException` «Operation cancelled by user» en el log | **27** | **0** |
| «Client closed the request» (Information) | 0 | **30** |
| Respuesta | 500 | **499** |

✏️ **Corrección de método**: las primeras lecturas usaron `grep "\[ERR\]"`, que **no puede
casar** porque el formato de Serilog es `[18:55:27 ERR]`. Daba 0 siempre. Los números de
arriba están medidos con `^\[[0-9:]+ ERR\]`.

---

## 19.4 Lo que queda, y por qué se deja

- [ ] **EF Core sigue registrando lo suyo a nivel Error.** Quedan 7 líneas de
      *«An error occurred using the connection to database…»* que escribe
      `Microsoft.EntityFrameworkCore.Database.Connection`, cada una seguida de nuestra
      línea de Information y de un 499 con el mismo id de correlación.
      **Se deja a propósito**: bajar esa categoría a Warning escondería también las caídas
      reales de base de datos, que es lo último que uno quiere no ver. Un error correlado
      con su explicación a la línea siguiente es mucho mejor que una categoría silenciada.
- [ ] **El timeout de base no se verificó ejecutando.** Provocarlo exigía saturar o
      bloquear el SQL Server compartido. El mapeo es de una línea y el resto del `switch`
      —que tiene la misma forma— sí está cubierto; se deja dicho en vez de darlo por
      probado.
