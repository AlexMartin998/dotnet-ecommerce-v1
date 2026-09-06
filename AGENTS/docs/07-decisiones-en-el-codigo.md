# 07 — Decisiones en el código

> **Qué es esto.** El porqué largo de cada decisión no obvia del código, **indexado por ruta
> de archivo**. Aquí está la medición, la alternativa descartada y el bug que motivó cada
> cosa.
>
> **Por qué está aquí y no en el código.** El 2026-09-06 el 40 % del código fuente eran
> comentarios: `<remarks>` de tres párrafos para explicar un semáforo, emojis por todas
> partes, la historia de cada cambio incrustada entre las líneas que pretendía explicar. Eso
> no documenta, tapa. Se lee peor, se mantiene peor y se escala peor.
>
> El código se quedó con el porqué **en una línea**; el desarrollo vive aquí. El contrato de
> estilo está en [`rules.md` §1.1](../rules.md).

**Cómo se usa**: busca la ruta del archivo que vas a tocar. Si está aquí, hay algo que no
debes romper sin querer.

**Cómo se mantiene**: si quitas del código una razón que valía, **la escribes aquí**. Si
añades una decisión no obvia, igual. Un archivo que aparece en este índice y ya no existe es
un bug de documentación: se borra su sección.

---

## Índice

- [Shared — persistencia, CRUD, idempotencia, cache, documentos, HTTP](#shared--persistencia-crud-idempotencia-cache-documentos-http)
- [Shared/Messaging — outbox, inbox, RabbitMQ](#sharedmessaging--outbox-inbox-rabbitmq)
- [Features/Catalog](#featurescatalog)
- [Features/Accounts](#featuresaccounts)
- [Features/Ordering](#featuresordering)
- [Arranque, datos, excepciones y tests](#arranque-datos-excepciones-y-tests)

---

## Shared — persistencia, CRUD, idempotencia, cache, documentos, HTTP

### `Shared/Idempotency/ICommandLog.cs`

- **La garantía vive en la base, no en Redis.** La idempotencia con Redis vive fuera de la transacción y deja dos ventanas que ningún código cierra: el proceso puede morir entre el commit y el guardado de la marca (la compra ocurrió y nadie lo recuerda), y el almacén puede no responder justo con carga, que es cuando el cliente reintenta. Medido: 174 de 14.400 peticiones se ejecutaron sin garantía con Redis **sano**. Dentro de la transacción esas ventanas no existen porque la marca y el efecto son la misma escritura.
- **Redis sigue delante como atajo** (`[Idempotent]`): responde antes y evita que una tormenta de duplicados se apile sobre la misma fila. Al no ser ya la garantía, degradar en abierto ahí dejó de significar «doble ejecución» y pasa a significar «el atajo no estaba».
- **`Record` no hace `SaveChanges`.** Lo confirma la transacción de negocio, o la marca y el efecto no se confirmarían juntos.
- **La huella se calcula dentro de `FindResultAsync`**, no en quien llama: el servicio no tiene por qué saber cómo se compara una petición con otra ni acabar hablando de hashes.
- **`IsDuplicateIntent` mira el número de error, no el tipo.** Un `catch (DbUpdateException)` a secas se tragaría timeouts y deadlocks y los daría por «duplicado ignorado» — el mismo fallo que ya se corrigió en `ProductPurchasedConsumer`.

### `Shared/Idempotency/CommandIntent.cs`

- **`CommandIntent` no es una «Idempotency-Key».** Esa es la forma que toma la intención cuando llega por HTTP; un job que reprocesa una cola tiene la suya (el `MessageId`, el id del pedido) y no manda cabeceras. El servicio debe hablar de intenciones, no del protocolo, igual que habla de `ITransactionRunner` y no de `DbContext`.
- **Es parámetro obligatorio a propósito.** El bug que motivó mover la transacción del controller al servicio fue este: la garantía dependía de acordarse de poner un atributo, y llamar a `BuyAsync` desde otro sitio la perdía en silencio. `CommandIntent.None` existe para que «no quiero idempotencia» sea una frase que alguien escribió y no un parámetro olvidado.
- **`ExecutedCommand` es hermana de `ProcessedMessage`** y por la misma razón: si marca y efecto no se confirman juntos hay una ventana donde uno existe sin el otro. En el consumidor esa ventana produjo un P0 (el mensaje se reconocía como duplicado y desaparecía sin procesarse); por HTTP era la contraria (la compra se confirmaba, el proceso moría antes de memorizar la respuesta y el reintento del cliente volvía a comprar).
- **Entre réplicas arbitra la clave primaria, no el código.** Si dos instancias ejecutan el mismo intento a la vez, la segunda se bloquea en la clave hasta que la primera confirme y entonces choca: su transacción entera —marca y efecto— se deshace. No hace falta reserva, ni TTL, ni token de propiedad.
- **`Id` con `MaxLength`, no `nvarchar(max)`:** en SQL Server eso no es indexable y no podría ser clave primaria. Misma trampa que ya se pisó con el índice único de `SKU`.
- **En `Result` se guarda el DTO del servicio, no la respuesta HTTP.** Deja al servicio fuera del protocolo y de regalo arregla la identidad byte a byte: al repetirlo el DTO vuelve a pasar por el mismo formateador de MVC, así que sale idéntico en vez de «equivalente».
- **`CommandOutcome.WasReplayed` existe** porque, al bajar la garantía a la transacción, el atajo de Redis dejó de ser el único sitio donde se reproduce una respuesta. Sin esto la cabecera solo aparecía cuando contestaba el atajo, y una cabecera que *a veces* marca los replays es peor que no tenerla.

### `Shared/Idempotency/IIdempotencyStore.cs`

- **Esto era antes el mecanismo entero, y ahí estaba el error de diseño.** Un almacén fuera de la transacción no garantiza nada (mismas dos ventanas y misma medición: 174 de 14.400 con Redis sano). La garantía se movió a `ICommandLog`.
- **Lo que queda sí merece Redis:** frenar la tormenta de duplicados antes de que se apile sobre la misma fila. Sin la puerta, 350 reintentos simultáneos con la misma clave se quedarían bloqueados en la clave primaria reteniendo cada uno su conexión; con ella el primero pasa y el resto recibe 409 sin tocar SQL.
- **El marcador solo vive mientras la petición está en vuelo:** no hay respuestas memorizadas, ni ventana de 24 h, ni una segunda copia del cuerpo que pueda diferir de la de verdad.
- **`ReleaseAsync` exige el `fence`:** un borrado incondicional deja que una petición cuyo marcador ya caducó tire el marcador vivo de otra.
- **`Unavailable` se distingue de `Entered`** aunque las dos ejecuten: una pasó por la puerta y la otra se la encontró rota, y eso es lo que hay que ver en una métrica antes de que se convierta en carga sobre SQL.

### `Shared/Idempotency/IdempotentAttribute.cs`

- **El filtro dejó de ser la garantía.** Un filtro HTTP no puede participar en la transacción de negocio, así que la marca se confirmaba *después* del efecto: ventana donde la compra ocurría y nadie la recordaba, y ventana donde el almacén no respondía por carga (174 de 14.400, medido, con Redis sano). Ninguna se cierra con más código en el filtro; se cierran cambiando dónde vive la marca. Misma corrección que con `[Transactional]`: la política de negocio no vive en un atributo del controller.
- **Aquí solo queda protocolo:** leer la cabecera, validar su forma y traducir «hay otra igual en vuelo» a 409. Las respuestas memorizadas se fueron con la garantía porque creaban una segunda copia del cuerpo que no coincidía byte a byte con la real.
- **`Idempotency-Replayed` la pone el controller, no el filtro.** Si se emitiera desde los dos sitios marcaría solo los replays que resolviera el atajo, y una cabecera que a veces marca los replays es peor que ninguna.
- **Sin cabecera se sigue adelante:** la idempotencia la pide el cliente, que es quien sabe si reintenta, y omitirla es la vía de escape estándar (lo documenta Adyen).
- **Sin usuario identificado tampoco hay puerta:** un espacio de nombres compartido haría que dos clientes con la misma clave se pisaran.
- **Límite de longitud de clave:** sin él se aceptaban claves de 7000 caracteres (medido). Validar la forma de la petición es trabajo del adaptador, por eso se queda en el filtro.
- **La clave incluye el QueryString y va en minúsculas:** `request.Path` no lleva la query, así que `POST /x?page=1` y `POST /x?page=2` compartirían puerta; y `/Product/buy` y `/product/buy` generarían dos.
- **409 y no 429:** no es exceso de tráfico, es la misma operación duplicada. Se responde en vez de dejar pasar porque N reintentos simultáneos se bloquearían todos en la clave primaria reteniendo su conexión.
- **`Release` en `finally` y con `CancellationToken.None`:** hay que soltar también cuando la acción lanza o el cliente cuelga, o una compra fallida deja la puerta cerrada hasta el TTL y el cliente no puede reintentar de verdad.

### `Shared/Idempotency/CommandLog.cs`

- **No hay reserva, TTL, token de propiedad ni estado «en curso».** Toda esa maquinaria existía en la versión sobre Redis para emular, mal, lo que la base ya hace: la segunda réplica se bloquea en la clave primaria hasta que la primera confirme y entonces choca. El árbitro es el índice, no el código.
- **`HashOptions` debe ser estable entre procesos:** dos réplicas tienen que calcular la misma huella para el mismo comando.
- **Huella distinta con la misma clave = 422.** Devolver el resultado del primero sería el fallo silencioso que el mecanismo existe para evitar: el cliente pidió comprar 5 y recibiría el resultado de haber comprado 1, sin que nada lo indicara.
- **`Record` hace `Add` y no `SaveChanges`:** si el efecto falla después, la marca se deshace con él y el reintento puede volver a intentarlo. Es la lección del P0 del consumidor: confirmar la marca antes que el efecto deja reintentos inertes.
- **`IsDuplicateIntent` recorre la cadena de `InnerException`** en vez de mirar una forma concreta de anidamiento: `SaveChangesAsync` envuelve el `SqlException` en `DbUpdateException`, pero `ExecuteUpdate*` lo lanza desnudo (regla §6 del proyecto). Números: 2627 = PK/UNIQUE, 2601 = índice único; 1205 = deadlock, que no debe confundirse con duplicado.
- **`StorageKeyOf` valida la longitud** porque SQL Server truncaría o daría un error críptico; fallar aquí con el nombre de la operación delante se diagnostica solo. Por HTTP no puede pasar (el filtro acota antes), pero un llamador nuevo sí.
- **`MaxKeyLength = 400`:** `nvarchar(400)` = 800 bytes, por debajo de los 900 que admite una clave de índice en SQL Server.

### `Shared/Idempotency/RedisIdempotencyStore.cs`

- **`IConnectionMultiplexer` y no `IDistributedCache`** porque esa abstracción solo expone Get/Set/Remove y no tiene «set si no existe», que es la primitiva necesaria. Caso legítimo de bajar un nivel.
- **Falla en abierto, y ahora es barato de razonar:** se pierde el atajo, no la garantía. Antes la misma línea significaba «ejecuta sin garantía de idempotencia».
- **Un viaje para entrar y otro para salir, sin guardar cuerpos.** La versión anterior memorizaba aquí la respuesta HTTP: un tercer viaje y una segunda fuente de verdad, porque el replay re-serializaba con otras opciones y salía equivalente pero no idéntico.
- **El `ReleaseScript` es el release canónico de un lock distribuido** (comparar el fence antes de borrar).
- **`ReleaseAsync` traga excepciones** porque corre en el camino de salida, incluido el de error: si lanzara, sustituiría la excepción real del servicio (el 404/409 que el cliente debe ver) por un fallo de Redis.

### `Shared/Idempotency/IdempotencyHttpExtensions.cs`

- **La ausencia de cabecera es la vía de escape estándar** (Adyen): la renuncia es del cliente y explícita, no una degradación silenciosa nuestra.
- **La clave se acota al usuario:** sin eso, dos clientes que casualmente generen el mismo GUID se pisan y uno recibe la respuesta del otro — fuga de datos entre cuentas.

### `Shared/Idempotency/IdempotencyOptions.cs`

- **Antes eran `private static readonly` dentro del filtro.** Además de saltarse la regla del proyecto (toda sección enlazada a una clase tipada y validada), la caducidad del marcador no se podía probar: con 60 s clavados, el único test posible tardaba un minuto y no existía.
- **La retención de comandos ejecutados la gobierna `Outbox:RetentionDays`.** Ese plazo tiene que cubrir el peor reintento de un cliente: a partir de ahí, la misma clave vuelve a ejecutar de verdad. (Stripe recuerda 24 h; Adyen, 7-14 días.)
- **El TTL del marcador ya no es un lease de la garantía.** Cuando lo era, caducar antes de tiempo abría la ventana de doble ejecución; hoy solo hace que la duplicada choque contra la clave primaria.
- **`MaxKeyLength = 255`:** lo que aceptan Stripe y el borrador de la IETF. Sin límite se aceptaban claves de 7000 caracteres (medido).

### `Shared/Idempotency/IdempotencyMetrics.cs`

- **Nació de un hallazgo:** bajo carga el almacén se apagaba solo —el `SET` agotaba el timeout de 1000 ms con Redis sano— y la única señal era un `Warning` que nadie mira.
- **La dimensión a vigilar es `outcome=gate_unavailable`:** ya no es corrección, pero sin puerta las tormentas de reintentos pasan enteras a SQL y consumen conexiones bloqueándose en la clave primaria.

### `Shared/Idempotency/NoIdempotencyStore.cs`

- **Antes esta clase era un problema real:** al ser el almacén de idempotencia entero, devolver «adelante» significaba que en cualquier entorno sin Redis no había idempotencia en absoluto, y la única señal era su propio XML doc.

### `Shared/Crud/ICrudService.cs`

- **No menciona la entidad a propósito:** un controller que depende de `ICategoryService` no puede ni ver `Category`.
- **`GetByIdAsync` lanza `NotFoundAppException` en vez de devolver `null`:** así el 404 sale igual desde cualquier endpoint sin depender de que cada controller se acuerde de comprobarlo.
- **Listado y página nunca dan 404:** sin resultados, colección vacía; con `PageQuery` fuera de rango, página vacía con el total real.

### `Shared/Crud/CrudService.cs`

- **Composición, no herencia.** Los servicios por entidad *tienen* un `ICrudService` y le delegan. Por eso es `sealed`: sus invariantes («antes de escribir se evalúan las reglas», «el update mapea sobre la entidad rastreada») no se pueden sobreescribir por accidente desde una subclase.
- **Lo que se gana frente a una clase base abstracta con hooks `virtual`:** reglas testeables por separado, un servicio de entidad libre de componer varios colaboradores y cero reflexión (el `Id` lo garantiza `IEntity`). **Lo que se paga:** cinco métodos de delegación por entidad — precio explícito y aceptado, ver `AGENTS/docs/03-service.md`.
- **Las reglas corren antes del mapeo,** para que todavía vean el estado previo de la entidad.
- **`GetOrThrowAsync` es el único sitio del proyecto donde nace un `NotFoundAppException` por id.**

### `Shared/Crud/IEntityRules.cs`

- **Sustituye a los hooks `OnBeforeCreateAsync` / `OnBeforeUpdateAsync` de una clase base abstracta.** Diferencia práctica: se instancian y prueban solas (solo dependen del repositorio), se pueden reutilizar o decorar, y no hay forma de que una entidad «se salte» el CRUD sobreescribiéndolo.
- **Implementación por defecto vacía** (default interface members, C# 8+): una entidad sin reglas no escribe nada y una con una sola regla implementa solo ese método.
- **Todo `Ensure*` comunica el incumplimiento lanzando una `AppException`.** Nunca devuelve `bool` ni conoce HTTP.

### `Shared/Crud/CrudExtensions.cs`

- **`NoEntityRules<,,>` se registra como genérico abierto** para que una entidad nueva funcione sin escribir nada; las que tienen reglas registran su implementación cerrada en su `AddXxxFeature()`, y el contenedor prefiere siempre la coincidencia exacta, sin depender del orden de registro.

### `Shared/Persistence/BaseRepository.cs`

- **Es la única herencia del proyecto para reutilizar código, y es deliberada:** la base es mecanismo puro (acceso a datos, sin reglas de negocio) y las subclases solo agregan consultas. Los métodos no son `virtual` para que nadie altere en silencio la semántica documentada en `IBaseRepository<T>`. La política se compone en la capa de servicio (`AGENTS/docs/03-service.md`).
- **No hay unit of work:** cada escritura llama a `SaveChangesAsync()`; para atomicidad entre repositorios se usa `[Transactional]`.
- **`ApplyDefaultOrder` usa `EF.Property`** porque un cast a `IAuditable` dentro del árbol de expresión no es traducible a SQL.
- **El desempate por `Id` no es cosmético: sin él, paginar está roto.** `CreatedAt` no es único (lo estampa `DateTime.Now`, y el seeding crea cinco categorías en el mismo tick). Con un orden no total, SQL Server puede devolver las filas empatadas en distinto orden en cada consulta, y como cada página es un `OFFSET/FETCH` independiente, una fila puede salir en dos páginas y otra en ninguna. Los repositorios que paginan de verdad (`ProductRepository`, `OrderRepository`) ya lo tenían a mano; faltaba en el camino genérico que sirve `GET /api/v1/category/paged`.
- **`IsAuditable` está cacheado** porque un `typeof` por llamada sería desperdicio en un método caliente.
- **El `COUNT` va primero y sobre la misma consulta base,** para que el total corresponda al mismo filtro que la página (hoy no hay filtro, pero la forma se mantiene).
- **`UpdateAsync` solo adjunta si la entidad viene desconectada:** llamar a `Update()` sobre una entidad ya rastreada marcaría TODAS las columnas como modificadas y generaría un UPDATE de la fila entera.
- **`ExistsAsync` usa `AnyAsync()` (`SELECT 1`)**, que no materializa ni rastrea la entidad, a diferencia del `FindAsync()` que se usaba antes.
- **En `ExistsByFieldAsync` la comparación se normaliza sin `StringComparison`,** que EF Core no sabe traducir.

### `Shared/Persistence/IBaseRepository.cs`

- **El repositorio nunca lanza excepciones de negocio.** Id inexistente → `null`/`false`; listado vacío → colección vacía. Quien decide que «no encontrado» es un 404 es el servicio.
- **`GetByIdAsync` rastrea a propósito:** el update del servicio hace `_mapper.Map(dto, existing)` sobre esa misma instancia.
- **`SaveChangesAsync` existe** para el caso en que el servicio añade algo al contexto que debe confirmarse en la misma transacción (hoy, la fila del outbox junto al descuento de stock).
- **`ExistsByFieldAsync` es el recurso genérico:** si la entidad tiene método dedicado (`NameExistsAsync`), se usa el dedicado — más rápido y sin string mágico.
- **`DeleteAsync` con id inexistente es un no-op silencioso, no un throw.**

### `Shared/Persistence/IEntity.cs`

- **`IEntity` equivale al `<ID>` de `JpaRepository<T, ID>` de Spring Data** y evita reflexión en los genéricos.
- **`IAuditable` equivale a `@CreatedDate` / `@LastModifiedDate` de Spring Data Auditing:** las estampa `AppDbContext`, nunca se asignan a mano.

### `Shared/Persistence/PersistenceExtensions.cs`

- **`EnableRetryOnFailure` no es opcional.** Sin él `Database.CreateExecutionStrategy()` devuelve una estrategia no reintentante y deja sin efecto `[Transactional]`, escrito precisamente para sobrevivir a un corte transitorio; contra un SQL Server en contenedor, cada micro-corte de red se convertía en un 500. Contrapartida: EF prohíbe abrir transacciones a mano fuera de `strategy.ExecuteAsync(...)`.
- **La validación de `SeedOptions` es condicional a propósito.** Con `[Required]` en la propiedad, un despliegue con `Seed__Enabled=false` (lo normal en producción, sin contraseña definida) moría al arrancar en bucle, porque leer `.Value` valida antes de que nadie pueda mirar el flag.
- **`IBaseRepository<>` se registra como genérico abierto** para poder inyectar `IBaseRepository<X>` sin escribir un repositorio específico; los de cada contexto acotado van en su `AddXxxFeature()`.
- **`ITransactionRunner` es Scoped** para compartir el `AppDbContext` del request, que es lo que hace que la transacción cubra al repositorio y al outbox a la vez.
- **`ICommandLog` se registra aquí y no en `AddDistributedCaching`:** es la garantía de idempotencia y vive en la base, no en la cache. Registrarla junto a Redis daría a entender que se apaga cuando no hay Redis, y es justo al revés.

### `Shared/Db/ITransactionRunner.cs`

- **Existe porque `[Transactional]` no puede hacer esto bien.** Con `EnableRetryOnFailure`, EF exige que toda la transacción vaya dentro de `strategy.ExecuteAsync(...)`, y esa estrategia reejecuta el delegado ante un fallo transitorio. El `ActionExecutionDelegate` de un filtro no es reentrante: invocarlo dos veces corrompe el request. Una lambda del servicio sí se puede repetir.
- **Contrato: la operación DEBE ser replayable.** No puede asumir nada del intento anterior (por eso se limpia el change tracker antes de cada uno); si necesita datos, que los relea dentro de la lambda.
- **La transacción es política de negocio** («descontar stock y emitir el evento son atómicos»), no de HTTP, así que vive en el servicio; y el servicio sigue sin ver `AppDbContext`.

### `Shared/Db/TransactionRunner.cs`

- **Si ya hay transacción abierta se participa en ella:** anidar transacciones en EF no hace lo que la gente espera.
- **`ChangeTracker.Clear()` antes de cada intento:** sin esto el reintento no es equivalente — las entidades añadidas en el intento anterior siguen rastreadas y, tras su `SaveChanges`, quedaron `Unchanged`, así que el segundo intento haría commit de una transacción vacía (stock descontado sin evento, o al revés).
- **El commit va sin token a propósito:** cancelar un commit a medias es peor que esperar a que termine.
- **`ChangeTracker.Clear()` también en el `catch`:** la transacción se deshace sola al disponerse el `tx`, pero el tracker no, y las entidades del intento fallido quedan en `Added` sobre el `DbContext` del scope. Hoy no explota, pero cualquier `SaveChanges` posterior (un filtro, una auditoría futura) insertaría esas filas **fuera de toda transacción**. Afecta a todo el que use el runner, así que se limpia aquí.

### `Shared/Db/TransactionalAttribute.cs`

- **Limitación documentada:** `ActionExecutionDelegate` no es reentrante, así que reejecutarlo daría doble efecto o commit vacío según el estado interno de MVC. La guarda de reentrada convierte eso en un error ruidoso en vez de corrupción silenciosa: fallar es infinitamente mejor que descontar stock dos veces en silencio.
- **Para una unidad de trabajo transaccional de verdad se usa `ITransactionRunner`;** este atributo se queda para acciones simples sin reintento.
- **`Order = 0`, por dentro de `[Idempotent]` (−100).**
- **`await using` en vez de `RollbackAsync` explícito:** evita el bug clásico de que un rollback que lanza dentro del `catch` sustituya a la excepción original y el cliente vea el fallo del rollback en vez de la causa real.

### `Shared/Documents/IDocumentStore.cs`

- **Es el puerto que permite pasar de sistema de ficheros a S3, R2, MinIO o Cloudinary** sin tocar dominio, consumidor ni controller: solo cambia la implementación registrada en el composition root.
- **No es `IFileStorage` y no debe fusionarse con él.** Aquel guarda imágenes de producto en `wwwroot/` para que `UseStaticFiles` las sirva a cualquiera: son públicas y esa es su gracia. Un comprobante lleva el nombre del cliente, su dirección y lo que pagó. Dos necesidades opuestas no caben detrás de la misma abstracción por mucho que las dos «guarden ficheros».
- **La clave es opaca:** quien llama no puede construirla, interpretarla ni convertirla en ruta. Eso es lo que hace que persistirla en base de datos siga valiendo tras migrar de infraestructura; si ahí hubiera `/app/documents/2026/09/x.pdf`, el día del cambio habría que reescribir todas las filas.
- **La clave incluye parte aleatoria:** aunque el acceso esté protegido, una clave adivinable convierte cualquier despiste futuro en una fuga.
- **`OpenAsync` devuelve `null` y no lanza:** que el documento no esté (puede estar generándose todavía) es una condición tratable, no un fallo del sistema.
- **`ListAsync` existe para poder recoger la basura:** el documento se escribe dentro de la transacción que lo referencia, así que un commit fallido deja un fichero al que ninguna fila apunta. El filtro por fecha es contrato, no comodidad (ventana entre fichero y fila). Devuelve flujo y no lista porque todos los almacenes paginan y materializar un bucket entero para borrar tres ficheros no es opción.
- **`DocumentContent` lleva `Stream` y no `byte[]`** pensando en adjuntos grandes: meter el fichero entero en memoria por descarga solo duele cuando ya hay tráfico.
- **`FileName` es presentación:** al abrir, el almacén solo puede derivarlo de la clave (`cdfdcf87….pdf`), así que quien sirve lo reemplaza. Guardarlo en el almacén sería meter presentación en la infraestructura y obligaría a cada proveedor futuro a tener dónde ponerlo.

### `Shared/Documents/DocumentStorageOptions.cs`

- **`Provider` desconocido tumba el arranque, no cae a un valor por defecto.** Degradar ante un `"s3"` mal escrito significaría escribir comprobantes en el disco de un contenedor efímero creyendo que están en el bucket, y enterarse el día del reinicio.
- **`RootPath` fuera de `wwwroot/`** (si no, `UseStaticFiles` sirve cada comprobante a quien adivine la ruta) **y fuera del árbol de la app en despliegue real**: en contenedor tiene que ser un volumen.
- **`CleanupIntervalHours = 0` apaga el recolector:** es un job que borra ficheros y ante cualquier sospecha lo primero es poder pararlo sin desplegar.
- **`OrphanGraceHours` es la propiedad que no se puede equivocar.** El documento existe un rato antes que la fila que lo apunta; sin gracia, el recolector borraría comprobantes buenos a mitad de vuelo, y un comprobante borrado no vuelve. El valor por defecto está tres órdenes de magnitud por encima de esa ventana a propósito: lo barato es esperar y lo caro es acertar por poco.

### `Shared/Documents/DocumentsExtensions.cs`

- **Único sitio a tocar** el día que los comprobantes vivan en S3/R2/MinIO/Cloudinary.
- **La elección se hace al arrancar y no por petición,** que es lo correcto cuando lo que se decide es qué implementación se registra: el grafo de DI se construye una vez. Misma forma que `AddDistributedCaching` y `AddMessaging`.
- **Proveedor desconocido tumba el arranque** en vez de degradar a disco.
- **Singleton** porque no guarda estado por petición y sus dependencias ya lo son; el día que sea S3, el cliente del SDK también es caro de crear y va igual.

### `Shared/Documents/LocalDocumentStore.cs`

- **Escribe fuera de `wwwroot/`**, y se sirve por un endpoint que comprueba de quién es la orden.
- **Clave `aaaa/mm/<32 hex>.pdf`:** las carpetas por año y mes no son estética — un único directorio con cientos de miles de ficheros hace lento hasta un `ls` y en algunos FS degrada la apertura. La parte aleatoria son 16 bytes de un CSPRNG.
- **La raíz se resuelve contra el content root, no contra el `cwd`.** `Path.GetFullPath` usa el directorio de trabajo, así que un `dotnet /app/ApiEcommerce.dll` lanzado desde otra carpeta escribiría en otro sitio y **todas las claves guardadas darían 404**.
- **`TrimEndingDirectorySeparator`:** `GetFullPath` conserva la barra final y la comprobación de contención concatena una. Con `RootPath` acabado en "/", la raíz quedaba como `.../docs//` y ninguna clave pasaba el filtro: guardar lanzaba y abrir devolvía null para todo — todos los comprobantes en 404 permanente mientras la orden dice "available". Un carácter de más en la configuración, y en silencio.
- **Se comprueba que la raíz no caiga dentro de `wwwroot`,** porque nada más lo impide (es una cadena en configuración) y ahí los documentos privados quedarían en abierto.
- **La raíz se resuelve como enlace ya en el constructor,** o `TryResolve` compararía una ruta real contra otra que aún pasa por el enlace.
- **`FileMode.CreateNew` y no `Create`:** si la clave existiera (solo por fallo del generador aleatorio) es mejor reventar que pisar el comprobante de otro.
- **Un fallo a mitad de la copia deja un PDF truncado y se borra ahí mismo:** sería un huérfano más, pero corrupto, y el recolector tendría que distinguirlo.
- **`OpenAsync` abre directamente en vez de `File.Exists` + abrir:** entre las dos cosas cabe un borrado y salía una `FileNotFoundException` desnuda → 500. La comprobación previa no evita la carrera, solo la hace más rara y más difícil de diagnosticar.
- **`EnumerateFiles` y no `GetFiles`,** para no materializar el almacén entero antes de mirar el primer fichero.
- **Se lee `LastWriteTime` y no `CreationTime`:** en varios sistemas de ficheros la fecha de creación no se mantiene al copiar o restaurar un volumen, y aquí una fecha antigua significa «bórralo».
- **`ListAsync` devuelve la ruta relativa con `/`,** la misma forma que dio `SaveAsync`: devolver la absoluta convertiría al recolector en alguien que conoce el disco, que es justo lo que la clave opaca evita.
- **`TryResolve` es el control de seguridad de la clase.** La clave viaja desde la base, pero basta una fila manipulada, un endpoint futuro que la acepte del cliente o una migración descuidada para que llegue `../../appsettings.json`. Se compara la ruta canonicalizada contra la raíz: filtrar por la cadena `".."` no vale porque no cubre rutas absolutas.
- **Canonicalizar no es solo normalizar:** `Path.GetFullPath` resuelve `.` y `..` pero no sigue enlaces simbólicos. Con un enlace dentro del almacén (`2026 → ../secretos`) la ruta normalizada empieza por la raíz, pasa el filtro y se lee un fichero de fuera. Por eso se resuelve el destino real segmento a segmento (`ResolveLinkTarget` solo mira el último componente). El ataque exige poder escribir en el directorio del almacén, así que hoy es remoto, pero una comprobación que dice cubrir algo que no cubre es peor que no tenerla.
- **`IsInside` compara con el separador** para que `/data/docs-otro` no pase por estar dentro de `/data/docs`.
- **`FinalTargetOf` devuelve el temp ante enlace circular o roto:** no se puede decir que esté dentro, y la decisión la toma quien compara.

### `Shared/Caching/CachingExtensions.cs`

- **Leer configuración de forma eager aquí es correcto e inevitable:** lo que se decide es qué implementación se registra, y el grafo de DI se construye una vez al arrancar. No es lo mismo que leer configuración en caliente dentro de un servicio, que sí debe ir por `IOptionsMonitor`.
- **Las dos ramas registran el mismo lifetime a propósito.** Antes una era `Scoped` (Redis) y la otra `Singleton` (sin cache): un consumidor singleton funcionaba en la máquina sin Redis y reventaba con captured dependency justo en el entorno que sí la tiene.
- **`IdempotencyOptions` se registra siempre, haya Redis o no:** el filtro las lee aunque el store sea el Null Object, y una opción que solo existe en una rama es un fallo que aparece únicamente en el entorno sin infraestructura.
- **Un solo multiplexer.** Antes había dos: el que crea `AddStackExchangeRedisCache` por su cuenta y el nuestro. Además de duplicar conexiones, el de la cache se quedaba con los timeouts por defecto, así que los nuestros solo protegían la mitad del sistema — medido: acortarlos bajó la compra con `Idempotency-Key` de 34 s a 11 s, y esos 11 s restantes eran la cache esperando con sus 5 s de fábrica.
- **Con `ConnectionMultiplexerFactory`, `Configuration` se ignora.**
- **Conexión cruda además de `IDistributedCache`** porque la idempotencia necesita `SET NX`; el multiplexer es thread-safe y caro de crear, y se comparte como singleton según recomienda StackExchange.Redis.
- **`AbortOnConnectFail = false`:** la fábrica es perezosa (se ejecuta en la primera petición, no al arrancar); con Redis caído en ese instante lanzaba, el contenedor no cachea instancias fallidas y cada petición reintentaba una conexión bloqueante de 5 s. Con `false` conecta en segundo plano y se recupera solo.
- **Los timeouts de fábrica convierten «degradar en abierto» en una caída.** Medido con Redis inalcanzable y valores por defecto (ConnectTimeout 5 s × ConnectRetry 3, SyncTimeout 5 s): GET del catálogo 11 s, compra con `Idempotency-Key` 34 s. Respondía bien, pero a esa latencia el cliente ya cortó, los hilos se acumulan y la caída de una optimización se lleva la API entera. Con 1 s el peor caso por operación queda acotado.

### `Shared/Caching/ICacheService.cs`

- **Interfaz propia y no `IDistributedCache`** porque aquella habla en `byte[]`: cada llamante tendría que serializar a mano y el «get, si null calcula y set» se repetiría en cada servicio.
- **Se prefiere a `[ResponseCache]`** (lo del curso de referencia) por tres razones: vive en la memoria de un proceso (no sirve con varias réplicas), no cachea nada si el request lleva `Authorization`, y no se puede invalidar — una categoría borrada se sigue sirviendo hasta que expire el TTL.

### `Shared/Caching/RedisCacheService.cs`

- **La cache falla en abierto:** con Redis caído se loguea y se sirve desde la base. Una cache que derriba la API convierte una optimización en punto único de fallo.
- **Singleton** porque no guarda estado por request y sus tres dependencias ya lo son; un scoped puede depender de un singleton, así que `CachedCategoryService` lo inyecta sin riesgo.
- **Una invalidación perdida sirve datos viejos como mucho hasta el TTL:** preferible a devolver un 500 en un POST que sí guardó bien.

### `Shared/Caching/NoCacheService.cs`

- **Null Object:** los servicios decorados no necesitan `if (cache is not null)` ni una bandera repartida por el código; la decisión se toma una vez, en el registro de DI.

### `Shared/Caching/CacheOptions.cs`

- **`Configuration` vacía = cache desactivada y la app arranca igual:** la cache es una optimización, no una dependencia dura.
- **`InstanceName` permite compartir una instancia de Redis entre varias apps** sin que se pisen las claves.

### `Shared/Caching/CacheKeys.cs`

- **Claves en un solo sitio** para que la que se escribe y la que se invalida se construyan con la misma función. Evita el bug clásico: invalidar una clave que nadie escribió y servir datos rancios para siempre.

### `Shared/Storage/LocalFileStorage.cs`

- **Limitación conocida y aceptada:** el disco local no escala horizontalmente (cada réplica ve el suyo) y no sobrevive al redespliegue de un contenedor. `IFileStorage` existe para que el cambio a blob storage sea de una clase.
- **Allowlist y no denylist:** una denylist siempre se olvida de algo (`.svg` lleva JavaScript, `.html` se sirve como HTML) y estos archivos se publican desde el mismo origen que la API, así que un archivo malicioso es XSS almacenado con las cookies de la API.
- **Triple comprobación:** tamaño, extensión y magic bytes. Extensión y Content-Type los pone el cliente y se pueden mentir; la firma del archivo, no.
- **El nombre lo genera el servidor:** usar el del cliente, «aunque sea solo la extensión», es la puerta de entrada al path traversal. Aun con GUID se verifica que la ruta resultante siga dentro de la carpeta gestionada.
- **Copia asíncrona** para que una subida no bloquee un hilo del pool.
- **`DeleteAsync` traga y registra:** un archivo huérfano es basura en disco, un 500 en el DELETE es un bug visible.
- **`WebRootPath` y no `Directory.GetCurrentDirectory()`:** el directorio de trabajo del proceso no es el del proyecto al publicar, con systemd o en contenedor.

### `Shared/Storage/IFileStorage.cs`

- **Devuelve ruta relativa y no URL absoluta a propósito.** El código de referencia construía `{Request.Scheme}://{Request.Host}/...` y lo persistía: `Host` es una cabecera que controla el cliente (host header injection almacenada) y la URL guardada queda rota en cuanto cambia el dominio o entra un proxy delante.

### `Shared/Storage/FileUpload.cs`

- **Existe para que la capa de servicio no reciba un `IFormFile`.** En el código de referencia el `IFormFile` viajaba dentro de los DTOs de `Models/Dtos`, acoplando el modelo al framework web.
- **`FileName` y `ContentType` son datos no confiables:** solo se usa la extensión, y el MIME se verifica contra el contenido real.

### `Shared/Auth/ClaimsPrincipalExtensions.cs`

- **Equivale a `SecurityContextHolder` de Spring Security, pero sin estado global:** el principal viaja en el request.
- **`GetRequiredUserId` es una red de seguridad** ante un `[Authorize]` olvidado: donde se usa, el 401 ya lo habría dado el middleware.
- **`GetEmail` busca por los dos nombres del mismo claim.** El token se emite con `JwtRegisteredClaimNames.Email` (`"email"`), pero el validador traduce los claims estándar a las URIs de `ClaimTypes` salvo que se desactive el mapeo — una bandera que está en otro archivo. Preguntar por uno solo funciona hasta que alguien la toca, y entonces el email desaparece en silencio de los comprobantes.
- **`GetTokenExpiry` devuelve UTC:** el claim `exp` es epoch UTC (RFC 7519) y es la excepción al `DateTime.Now` local del resto del proyecto; convertirlo a local daría a la denylist un TTL con horas de desfase.

### `Shared/Auth/IAccessTokenDenylist.cs` y `RedisAccessTokenDenylist.cs`

- **Es una optimización, no la garantía.** Lo que corta una sesión es revocar su familia de refresh tokens, que vive en la base y se confirma en una transacción; sin eso la sesión se puede extender indefinidamente. La denylist solo adelanta el efecto al access token que el cliente ya tiene en la mano.
- **Por eso puede vivir en Redis y degradar en abierto:** sin ella, un logout sigue cortando la sesión y solo sobrevive el access token actual, como mucho lo que dure (15 min). Es la distinción de `rules.md` §8: degrada lo que tiene plan B, no lo que es la garantía.
- **El TTL de cada entrada es la vida restante del token, ni un segundo más:** pasado eso la firma ya no vale y guardarlo sería pagar memoria por nada.
- **`IsRevokedAsync` falla en abierto** porque corre en cada petición autenticada: fallar en cerrado convertiría un corte de Redis en «nadie puede usar la API», mucho peor que un token revocado sobreviviendo unos minutos.
- **`RevokeAsync` no propaga el fallo:** dejar que un fallo de Redis convierta un logout correcto en un 500 sería cambiar una molestia por un error.

### `Shared/Auth/SeedOptions.cs`

- **`Enabled = false` por defecto es deliberado:** el seeder del código de referencia se ejecutaba sin guarda de entorno y sembraba un `admin` con contraseña conocida también en producción.
- **`AdminEmail` sin `[EmailAddress]` y `AdminPassword` sin `[Required]`.** Una anotación incondicional se evalúa al leer `.Value`, antes de que nadie pueda mirar `Enabled`: un despliegue con el seeding apagado que definiera `Seed__AdminEmail=` (vacío) moría al arrancar, en bucle con `restart: unless-stopped`. Lo destapó el test de arranque de la fase 5. Si la regla es condicional, va en `.Validate(...)`.

### `Shared/Auth/Roles.cs`

- **`const` y no `static readonly`** porque `[Authorize(Roles = ...)]` es un atributo y solo admite constantes de compilación. Un string mágico repetido en 12 endpoints es un typo esperando a pasar en producción.

### `Shared/Paging/PagedResult.cs`

- **Lleva `TotalItems` y no solo `TotalPages`:** es el dato del «mostrando 1-10 de 137», y calcularlo para tirarlo (como el código de referencia) es pagar el `COUNT(*)` sin cobrarlo.
- **Es un `record`:** valor inmutable de transporte, sin identidad ni comportamiento.
- **Página fuera de rango → 200 con lista vacía, no 404:** el recurso (la colección) existe; lo que no hay son resultados. El código de referencia devolvía 404 con la tabla vacía, que es semánticamente falso.

### `Shared/Paging/PageQuery.cs`

- **El tope de `PageSize` no es decorativo:** sin él, un `?pageSize=1000000` en un endpoint anónimo es una denegación de servicio de una sola petición.
- **`Skip` se calcula en `long` y se satura.** En `int`, `(Page - 1) * PageSize` desborda con `?page=2147483647`, da negativo y SQL Server responde «The offset specified in a OFFSET clause may not be negative»: un 500 con traza a partir de un query string.

### `Shared/Mapping/MappingExtensions.cs`

- **Los ensamblados llegan por parámetro** en vez de resolverse con `typeof(CategoryProfile).Assembly`: aquello obligaba a `Shared/` a nombrar un tipo de `Features/`, que es la dirección de dependencia al revés. Quien puede conocer ambos lados es el composition root.

### `Shared/Observability/ObservabilityExtensions.cs`

- **Se instrumenta siempre, se exporta solo si hay a dónde.** Sin recolector las trazas siguen existiendo en proceso y dan el `TraceId` de los logs. Misma decisión que con Redis y RabbitMQ: la observabilidad no puede ser el motivo de que la API no arranque.
- **OpenTelemetry y no un SDK de proveedor:** el vendor lock-in de la observabilidad se paga tarde y caro; cambiar de backend debe ser cambiar un endpoint, no reinstrumentar.
- **El `.ValidateOnStart()` no cubre este camino:** se dispara al resolver `IOptions<ObservabilityOptions>` y aquí se lee el POCO en crudo para decidir qué se registra, así que la validación se hace explícita y sin lanzar.
- **`SamplingRatio` se acota con `Clamp`** porque un valor fuera de rango reventaba con excepciones crudas de terceros («Must be in the range: [0, 1]») en vez de con un mensaje accionable.
- **Muestreo padre-consciente:** si el servicio que nos llamó decidió trazar, se traza; decidir por nuestra cuenta partiría las trazas distribuidas por la mitad.
- **`/health` se filtra:** las sondas corren cada pocos segundos, siempre igual, y ahogarían cualquier traza que importe.
- **El texto de las consultas SQL no se captura**, y no hay que hacer nada: desde la 1.10 está apagado salvo bandera experimental `OTEL_DOTNET_EXPERIMENTAL_SQLCLIENT_ENABLE_TRACE_TEXT_COMMAND`. **No activarla:** llevaría correos, nombres y precios al backend de trazas, que casi nunca tiene la protección de la base de datos.
- **La métrica propia a vigilar es `outcome=unguaranteed`:** si deja de ser cero, la protección contra el doble cobro está apagada para esa parte del tráfico aunque todas las respuestas sean 200.

### `Shared/Observability/CorrelationIdMiddleware.cs`

- **Es lo primero que se pide en un incidente** («mándame el id de la petición que falló»): sin esto, correlacionar logs entre réplicas es imposible y el cliente que reporta no tiene nada que dar salvo la hora.
- **Se respeta el id entrante** para que una cadena de servicios comparta el mismo de punta a punta, y **se acota su longitud** porque acaba en el log: un valor de 10 KB elegido por el cliente es un vector de ruido barato.
- **La cabecera se registra con `OnStarting` antes de seguir:** hacerlo al terminar no funcionaría con una respuesta ya empezada a enviar, justo en los casos lentos.
- **El `TraceId` se lee de `Activity.Current`** en vez de añadir un paquete enriquecedor: dos líneas y una dependencia menos.
- **Convive con el `TraceId` sin sustituirlo:** el `TraceId` es para las herramientas, el correlation id para las personas y las cabeceras.

### `Shared/Observability/ObservabilityOptions.cs`

- **`ExportsTraces` comprueba que el endpoint sea un URI absoluto, no solo que no esté vacío.** Sin eso, un espacio de más en una variable de entorno hacía que `new Uri(...)` lanzara en el arranque —y después de migrar y sembrar—, con `restart: unless-stopped` un crash-loop por un endpoint de métricas. Mismo error de fondo que el `[Required]` de `SeedOptions.AdminPassword`: una pieza opcional decidiendo si la API arranca.

### `Shared/Http/GlobalExceptionHandler.cs`

- **Aquí no se comprueba si el cliente colgó:** eso va en `ClientAbortMiddleware`, por debajo de `UseExceptionHandler`, porque el middleware de diagnóstico del framework escribe su «unhandled exception» a nivel Error *antes* de llamar a este handler.
- **`Retry-After` en los 503:** misma idea que la cabecera `transient-error` de Adyen — no basta con rechazar, hay que decir si el rechazo es transitorio.
- **El corte para censurar `Detail` es «¿lo mapeamos nosotros?», no «¿es 5xx?».** Una excepción no controlada nunca filtra su mensaje; el 503 por timeout de base trae un texto nuestro, accionable y sin información del servidor, y censurarlo dejaba al cliente sin saber si podía reintentar. En 4xx el mensaje de dominio («Insufficient stock for SKU 'X'») es parte del contrato, no una fuga.
- **`correlationId` sale de `HttpContext.Items`, no de `TraceIdentifier`.** El escritor de ProblemDetails del framework machaca `TraceIdentifier` con `Activity.Id`: el cuerpo y la cabecera llevaban dos ids distintos para la misma petición.
- **Las ramas de carrera no son código sin migrar:** son el caso en que dos peticiones simultáneas pasan las dos la validación y arbitra la base. Sin traducirlas, arreglar la carrera (índice único, `RowVersion`) empeoraría la respuesta convirtiendo el 409 correcto en un 500.
- **`FindSqlException` recorre la cadena** porque el anidamiento no es estable: `SaveChangesAsync` → `DbUpdateException { SqlException }`, `ExecuteUpdateAsync` → `SqlException` desnudo, `EnableRetryOnFailure` agotado → `RetryLimitExceededException { ... }`. La versión anterior solo acertaba el primer caso, así que un choque en `TryDecrementStockAsync` —la sentencia con más contención del sistema— salía 500.
- **Números SQL mapeados:** 2601/2627 conflicto, 1205 deadlock (reintentable), 547 FK, −2 timeout de comando (el Win32 258 es WAIT_TIMEOUT). El −2 va a **503 y no 500 porque es reintentable**; medido en carga, 83 de estos salían como error interno.
- **`InvalidOperationException` no está mapeada a propósito.** EF la usa para errores de programación («the instance of entity type X cannot be tracked…», «the configured execution strategy does not support user-initiated transactions»), no de negocio: mapearla a 409 daba el código equivocado y filtraba mensajes internos del ORM, porque `Detail` solo se censura a partir de 500.
- **`BrokerUnavailableException` solo llega desde los endpoints de administración de mensajería:** el outbox la trata por su cuenta y nunca la deja escapar a HTTP.
- **Las ramas BCL son red de seguridad** mientras quede código viejo; en código nuevo los servicios lanzan `AppException`.

### `Shared/Http/ClientAbortMiddleware.cs`

- **Qué pasa:** al cortar el cliente, ASP.NET Core cancela `RequestAborted`, EF cancela el `SqlCommand` y SqlClient lanza un **`SqlException`** («A severe error occurred on the current command» / «Operation cancelled by user», Win32 258 dentro) — que **no** es una `OperationCanceledException`, así que llegaba al final del pipeline como excepción cualquiera: 500, log a nivel Error y traza completa.
- **Medido en las pruebas de carga: 95 «errores» que no eran errores.** El coste no es la respuesta (no hay nadie al otro lado) sino el ruido: métricas de error y log llenos de incidentes falsos justo cuando más falta hace leerlos.
- **Va por dentro de `UseExceptionHandler`:** el middleware de diagnóstico escribe su línea de Error antes de llamar a ningún `IExceptionHandler`, así que decidirlo en `GlobalExceptionHandler` llega tarde.
- **Se decide por el estado de la petición y no por el tipo de excepción:** la cancelación se propaga distinto según dónde pille (EF, cliente AMQP, `HttpClient`, Kestrel leyendo el cuerpo) y perseguir cada tipo es una lista que nunca está completa.
- **499 (Client Closed Request)** no es del RFC pero es la convención de facto de nginx, y es lo que evita que estas peticiones cuenten como 5xx. Solo se pone si la respuesta no ha empezado; y no se escribe cuerpo.
- **Se loguea a Information y sin la excepción,** dejando el tipo en el mensaje porque saber por dónde pilló la cancelación sigue siendo útil.

### `Shared/Http/CorsPolicies.cs`

- **Nunca `AllowAnyOrigin()`:** con un token en `Authorization`, el comodín deja que cualquier página del mundo llame a la API desde el navegador de un usuario logueado. Lista vacía = ningún origen permitido; falla cerrado.
- **Se filtran los orígenes vacíos** porque la configuración de .NET **fusiona** colecciones por clave en vez de reemplazarlas: definir `Cors__AllowedOrigins__0` por entorno no borra los índices 1, 2… de `appsettings.json`. Con la lista base vacía y este filtro, cada entorno declara los suyos sin heredar los de desarrollo.
- **`AllowCredentials()` es imprescindible desde que el refresh token viaja en cookie:** sin él el navegador no la manda entre orígenes ni acepta la respuesta que la establece — el login funciona y el refresh no, sin ningún error en el servidor. Es legal precisamente porque hay lista explícita de orígenes: combinarlo con `AllowAnyOrigin()` lo prohíbe la especificación y ASP.NET Core lanza. La mejor razón para no haber puesto nunca el comodín.
- **`WithExposedHeaders`** deja que el front lea las cabeceras de versión de `ReportApiVersions`.

### `Shared/Http/RateLimitPolicies.cs` y `RateLimitOptions.cs`

- **Dos políticas y no una:** el límite global protege de un cliente pesado; el de `auth` es el que importa, porque sin él el lockout de Identity se sortea probando contraseñas contra muchos usuarios distintos (password spraying).
- **Los límites se resuelven del contenedor por petición** y no se capturan de un `IConfiguration` leído al registrar: es la regla de `IOptions<T>` del proyecto, y así un cambio en caliente se respeta en la siguiente partición.
- **Partición por IP remota:** detrás de un proxy hay que activar `UseForwardedHeaders` o todas las peticiones compartirán la IP del proxy y el límite se agotará para todos a la vez.
- **Pasaron de constantes a configuración por dos razones concretas:** una API tras un gateway o con un cliente móvil que hace ráfagas necesita otro número sin recompilar; y los tests de integración y concurrencia, que mandan decenas de peticiones desde una sola IP, se limitaban a sí mismos y fallaban con 429 por algo ajeno a lo que probaban. Los valores por defecto son los que había antes.

### `Shared/Http/ApiDocumentationExtensions.cs`

- **Versionado por segmento de URL** y no por query string ni cabecera: es el único que se ve en un log, se puede cachear y se puede compartir como enlace.
- **`AssumeDefaultVersionWhenUnspecified` en `false` a propósito:** con versionado por ruta esa opción es humo — `/api/category` no matchea ninguna plantilla y da 404 antes de que el versionador opine.
- **`ReportApiVersions`** hace que el cliente se entere de que su versión va a morir sin leer documentación.

### `Shared/Http/ConfigureSwaggerOptions.cs`

- **Patrón `IConfigureOptions<SwaggerGenOptions>`:** DI lo construye con el `IApiVersionDescriptionProvider` ya poblado por `AddApiExplorer`, así que añadir una `v3` es poner `[ApiVersion("3.0")]` en un controller, cero cambios en `Program.cs`.
- **Aquí vive la definición de seguridad Bearer,** que es lo que pinta el botón Authorize de la UI.
- **La deprecación sale del atributo `[ApiVersion(..., Deprecated = true)]`,** que además emite la cabecera `api-deprecated-versions`.

### `Shared/Http/HttpContextExtensions.cs`

- **Pasar `{version}` explícita elimina una clase entera de bug:** un `CreatedAtRoute("GetCategory", new { id })` sobre `api/v{version:apiVersion}/...` depende de que el generador reutilice el valor ambiente; cuando no lo hace no falla el 201, falla la generación de la cabecera `Location` con un 500 sin relación aparente.

### `Shared/Http/Health/HealthCheckExtensions.cs` y `HealthController.cs`

- **`/health` es liveness y no toca la base ni Redis:** si la sonda de vida dependiera de la base, una caída de la base haría que el orquestador reiniciara procesos sanos. `/health/ready` es la de readiness.
- **`[ApiVersionNeutral]` es obligatorio** desde que la API está versionada: sin él el versionador exige una versión que esa ruta no tiene y `/health` da 404. Y es lo correcto: una sonda de infraestructura no forma parte del contrato versionado.
- **Solo se comprueba Redis si está configurado:** exigirlo en readiness dejaría el servicio fuera de rotación por una dependencia que ni siquiera usa.
- **Al health check se le pasa el multiplexer del contenedor, no la cadena de conexión.** Con la cadena abría su propia conexión —una tercera, con timeouts de fábrica— y podía decir «Healthy» usando una conexión sana mientras la de la aplicación estaba rota, que es exactamente lo que una sonda no puede hacer.
- **Redis en `Degraded`, no `Unhealthy` (el valor por defecto).** Es una optimización y la app degrada en abierto (rules §8). Como su caída la ven todas las réplicas a la vez, marcarlo `Unhealthy` hacía que `/health/ready` respondiera 503 y el orquestador las sacara de rotación todas: una dependencia opcional provocando una caída total. Medido: con Redis muerto, `GET /api/v1/category` respondía 200 y `/health/ready` respondía 503. `Degraded` responde 200 y sigue visible en el detalle — visible para quien mira, invisible para el balanceador.
- **Sonda de outbox:** sin ella, un broker caído el tiempo suficiente entierra eventos en silencio mientras el servicio se reporta sano y los datos divergen. `Degraded` y no `Unhealthy` porque la API atiende bien; lo que hay es trabajo pendiente.
- **El umbral se lee de `OutboxOptions.MaxPublishAttempts`**, el mismo valor que aplica el publicador. Antes era una `const` local con un comentario pidiendo mantenerla sincronizada: subir el máximo en el publicador habría dejado la sonda contando como perdidos mensajes que aún se reintentaban.

### `Shared/DependencyInjection/ServiceCollectionExtensions.cs`

- **Composition root que no registra nada propio.** Las dos únicas excepciones son `AddControllers()` y `AddHsts()`, de la superficie HTTP, que no tienen otra carpeta a la que pertenecer.
- **Organización por vertical slicing por contexto acotado:** cada carpeta de `Features/` lleva todo lo suyo dentro y su propio `AddXxxFeature()`.
- **Un slice es un contexto acotado, no una entidad.** `Category` y `Product` viven juntos en `Catalog`, y ahí irían `UnitOfMeasurement`, `ProductTag` o `Brand`. Un slice por entidad reproduce la dispersión que el slicing venía a quitar, con más carpetas. La pregunta es de DDD: ¿esto tiene su propio lenguaje y sus propias invariantes, o es parte del vocabulario de otro?
- **Los tres bloques declaran la dirección de dependencias Web → Features → Shared.** En un proyecto único es convención, pero son las costuras exactas por donde se parte la solución en proyectos.
- **Lifetimes:** todo lo que dependa de `AppDbContext` va `Scoped`; lo que no guarda estado por request y solo depende de singletons va `Singleton`, **igual en todas sus ramas de registro**.
- **`AddObjectMapping` recibe el ensamblado desde aquí** porque el composition root es el único sitio de `Shared/` que puede nombrar tipos de `Features/`: es literalmente su trabajo.
- **`AddFileStorage` y `AddDocumentStorage` son dos almacenes distintos, no una duplicación:** aquel sirve estáticos públicos desde `wwwroot`, este guarda documentos privados fuera de él.
- **HSTS a un año** (el valor de fábrica son 30 días, que no sirve de mucho: la protección vale mientras el navegador recuerde la política), **sin `Preload` ni `IncludeSubDomains` a propósito**: `Preload` es una puerta de un solo sentido (entrar en la lista de los navegadores lleva meses y salir, más) e `IncludeSubDomains` rompe cualquier subdominio que aún se sirva en claro. Se activan cuando alguien lo decida a sabiendas.

---

## Shared/Messaging — outbox, inbox, RabbitMQ

### `Shared/Messaging/Events/IDomainEvent.cs`

- **`EventType` es parte del contrato público entre servicios.** Renombrar la clase C# no debe romper a los consumidores, así que el nombre va explícito y no se deriva de `typeof(T).Name`.

### `Shared/Messaging/BrokerUnavailableException.cs`

- **No hereda de `AppException` a propósito:** nunca viaja a un cliente HTTP. Una compra con el broker caído se responde 200 y el evento se queda en el outbox (degradar en abierto), así que no tiene código ni estado que mapear.
- **Existe para que `OutboxPublisher` distinga "infraestructura caída" de "este mensaje concreto falla".** Cuando ambas cosas eran la misma `InvalidOperationException`, las dos incrementaban `Attempts`: medido, **25 segundos** de broker caído bastaban para enterrar un evento de forma permanente — menos de lo que tarda en arrancar el propio contenedor de RabbitMQ (`start_period: 30s`).

### `Shared/Messaging/IEventOutbox.cs`

- **El servicio de negocio depende de `IEventOutbox`, no de RabbitMQ.** Cambiar a Kafka o Azure Service Bus no toca `ProductService`: solo la implementación de `IEventPublisher`.
- **`EnqueueAsync` no hace `SaveChanges` a propósito:** manda la transacción de negocio, y el evento debe confirmarse con ella o no confirmarse en absoluto.
- **`IEventPublisher` debe esperar publisher confirms.** Sin ellos, "publicado" solo significa "escrito en un socket".

### `Shared/Messaging/MessageInbox.cs`

- **La comprobación previa de `AnyAsync` es solo un atajo barato** para el caso normal (ya procesado): evita abrir el efecto. No es la garantía — esa es la clave primaria de `ProcessedMessages`.
- **El efecto va entre el `Add` y el `SaveChangesAsync`.** Ese orden es el arreglo del P0: si el efecto lanza, la marca se deshace con él y el reintento puede volver a intentarlo. Confirmar la marca antes dejaba los reintentos inertes.
- **Se recorre toda la cadena de `InnerException`** en vez de mirar una forma concreta de anidamiento: `SaveChangesAsync` envuelve el `SqlException` en `DbUpdateException`, pero `ExecuteUpdate*` lo lanza desnudo. Es la regla §6 del proyecto.

### `Shared/Messaging/OutboxOptions.cs`

- **Sección propia y no dentro de `RabbitMq`.** El outbox es agnóstico al broker; tener sus mandos bajo `RabbitMq:` daba a entender lo contrario, y al cambiar de transporte habría que mover configuración que no tenía nada que ver con él.
- **`MaxPublishAttempts` vive aquí para ser una sola fuente.** Lo leen el publicador y la sonda `outbox-backlog`; cuando eran dos `const` separadas, subir el máximo en uno dejaba al otro contando como perdidos mensajes que aún se reintentaban.
- **`RetentionDays` no es un detalle de limpieza.** `OutboxMessages` y `ProcessedMessages` crecen con cada compra y para siempre. El índice de pendientes se mantiene barato (está filtrado) pero la tabla no, y las copias de seguridad y el disco crecen sin techo.

### `Shared/Messaging/IMessageInbox.cs`

- **Es el gemelo de `IEventOutbox` (inbox pattern)** y el mismo razonamiento que `ICommandLog` para las peticiones HTTP: una garantía de «esto no se ejecuta dos veces» no puede vivir fuera de la transacción que protege.
- **Existe porque aquí hubo un P0.** Antes la marca se confirmaba *antes* del efecto, con el razonamiento de que la restricción única «abría la puerta» y quien perdía el choque no ejecutaba. Ese razonamiento valía sin reintentos; en cuanto los hubo, si el efecto fallaba la marca ya estaba confirmada, en la reentrega el mensaje se reconocía como duplicado, se hacía ack y **desaparecía sin haberse procesado nunca**.
- **Existe como pieza propia** —en vez de unas líneas dentro del consumidor— para poder probarse sin broker: un test le pasa un efecto que lanza y comprueba que la marca se deshizo. Mientras vivió dentro de un `BackgroundService` atado a AMQP, ese test no se podía escribir y el arreglo del P0 se quedó sin red.
- **El efecto debe ser replayable:** la transacción puede reintentarse ante un fallo transitorio, así que no puede dar por buena ninguna lectura del intento anterior. Mismo contrato que `ITransactionRunner`.
- **`IsConcurrentDuplicate` mira el NÚMERO de error, no el tipo.** Un `catch (DbUpdateException)` a secas se tragaría timeouts y deadlocks (1205), haría ack, y el mensaje desaparecería de la cola sin procesarse con un log que dice «duplicado ignorado».

### `Shared/Messaging/OutboxMessage.cs`

- **Outbox transaccional:** no se puede escribir en base de datos y broker atómicamente. Publicar y luego fallar el commit anuncia una compra que no existe; hacer commit y fallar la publicación deja la compra sin que nadie se entere.
- **Consecuencia práctica: la API funciona con RabbitMQ caído.** Las compras se completan y los eventos se acumulan hasta que el broker vuelve.
- **Garantía at-least-once:** si el publicador muere entre publicar y marcar como procesado, el mensaje sale dos veces; por eso el consumidor debe ser idempotente.
- **`Sequence` es un `IDENTITY` y no `OccurredAt`.** `OccurredAt` es `DateTime.Now` del proceso que escribió la fila: con dos réplicas dependía del reloj de cada máquina y no desempataba filas del mismo milisegundo. Un `IDENTITY` lo asigna un único árbitro (el servidor SQL) y es determinista y repetible.
- **Pero `Sequence` NO garantiza el orden de publicación.** El `IDENTITY` se asigna al `INSERT`; la fila se ve al `COMMIT`. Bajo READ COMMITTED, una transacción lenta con secuencia 54 puede confirmar *después* de que ya se haya publicado la 55 — verificado. Los huecos en la tabla (rollbacks que consumieron el IDENTITY) son la otra cara del mismo hecho.
- **Se deja así a propósito, no por pendiente.** Hoy hay un evento por compra y ningún consumidor exige orden entre agregados; arreglarlo de verdad pide un *watermark* que espere a las transacciones abiertas, no una columna.
- **Señal para reabrirlo:** el día que un consumidor necesite ver dos eventos del MISMO agregado en orden (un `ProductUpdated` seguido de un `ProductPurchased`). Ver `planning/18` §18.6.
- **`ProcessedMessage` es la otra mitad del at-least-once:** el broker *va a* reentregar (reinicio del consumidor, nack, publicación duplicada por la outbox).

### `Shared/Messaging/DeadLetterController.cs`

- **Es la salida de la DLQ.** Antes, un comprobante que agotaba sus intentos dejaba la orden en `failed` —correcto, el cliente deja de esperar— pero reemitirlo exigía entrar a la consola del broker. Una cola de la que no se sale no es una red de seguridad, es un vertedero.
- **Vive en `Shared/Messaging` y no en un slice** porque no es de ningún dominio: opera sobre el mecanismo, y no nombra ningún tipo de `Features/`, que es lo que la regla de dirección de dependencias prohíbe.
- **Mover mensajes es operación de operador, no de usuario:** de ahí `[Authorize(Roles = Roles.Admin)]` a nivel de clase.
- **503 y no 500 sin broker:** que la feature no esté no es un error del cliente ni un fallo nuestro.
- **`GetDeadLetters` devuelve recuentos, no contenido:** volcar payloads sería una fuga esperando a que alguien publique un evento más rico que los de hoy.
- **`queue` se valida contra las suscripciones registradas (allowlist por construcción); una desconocida da 404.** Sin ello el endpoint movería mensajes de cualquier cola del broker, que es compartido con otros proyectos.
- **El replay es idempotente en el EFECTO, no en la operación:** reemitir dos veces publica dos veces, pero el inbox deduplica por `MessageId` y el trabajo se hace una.
- **`max` tiene techo (500):** sin él, un `max` enorme deja la petición HTTP moviendo mensajes de uno en uno durante minutos. Quien necesite más, llama otra vez.

### `Shared/Messaging/MessagingExtensions.cs`

- **El outbox, el inbox y la purga se registran SIEMPRE; el broker solo si está configurado.** Escribir el evento y procesar exactamente una vez son garantías sobre la base de datos: registrarlas dentro del `if` de RabbitMQ ataría piezas transaccionales al transporte y dejaría sus tests dependiendo de que hubiera broker.
- **La purga va fuera del `if`** porque las tablas crecen aunque nadie publique — y con RabbitMQ apagado crecen MÁS.
- **El Null Object `NoDeadLetterAdmin` se registra con el MISMO lifetime que el real.** Sin esa rama, el controller de dead-letters no se podría construir sin broker y saldría un 500 de DI en vez del 503. Y falla en CERRADO, al revés que los de cache o idempotencia: "no hay mensajes muertos" sería mentira, no degradación (rules.md §8).
- **`AddEventConsumer` es la costura para que cada slice registre SUS consumidores.** Si `ProductPurchasedConsumer` se registrara en `Shared`, `Shared` dependería de `Features` y la dirección declarada **Web → Features → Shared** se invertiría.
- **La suscripción se registra dos veces a propósito:** como `EventSubscription` para que `RabbitMqConnection` declare la topología sin conocer al consumidor, y como `EventSubscriptionOf<TConsumer>` para que el consumidor pida *la suya* sin poder recibir la de otro.
- **La condición "hay broker" se evalúa en `AddEventConsumer` y no en cada slice:** es la misma decisión para todos, y duplicarla garantiza que algún día un slice la comprueba distinto. Por eso la suscripción se registra **dentro** del `if`: sin broker no hay topología que declarar, y una réplica sin mensajería declararía colas que nadie consume.
- **`ValidateOnStart()`:** configuración inválida = no arranca, en vez de fallar en la primera petición.

### `Shared/Messaging/OutboxCleaner.cs`

- **Se conserva una ventana en vez de borrar al procesar:** las filas son la evidencia de qué se publicó y qué se consumió cuando alguien pregunta por un pedido de la semana pasada.
- **No se toca nada sin procesar.** El filtro exige `ProcessedAt != null`: un evento que agotó sus reintentos sigue pendiente de revisión manual, y borrarlo sería perder el hecho de negocio en silencio.
- **El `catch (Exception)` va SIN filtro que excluya `OperationCanceledException`.** Una OCE que no venga del `stoppingToken` (la cancelación de un `SqlCommand`, por ejemplo) se escapaba y, con `BackgroundServiceExceptionBehavior.StopHost` por defecto desde .NET 6, tumbaba la API entera.
- **El corte (`cutoff`) se calcula en una VARIABLE LOCAL.** Dentro del árbol de expresión, `DateTime.Now` se traduce a `GETDATE()` y lo evaluaría el reloj del servidor SQL — otro reloj distinto del que escribió las filas.
- **El plazo de `ExecutedCommands` NO se copia de nadie:** tiene que cubrir el PEOR reintento de un cliente, porque a partir de ahí la misma `Idempotency-Key` vuelve a ejecutar de verdad. (Stripe recuerda 24 h, Adyen 7-14 días.)
- **Borrado troceado (`DeleteBatchSize = 5000`):** un `ExecuteDeleteAsync` de cientos de miles de filas escala el bloqueo a toda la tabla y bloquea a las compras que están escribiendo su evento.
- **Un minuto de espera antes de la primera pasada:** al arrancar hay cosas más urgentes (migraciones, seeding, el primer drenaje del outbox).

### `Shared/Messaging/OutboxPublisher.cs`

- **Es un `BackgroundService` y no parte del request:** publicar dentro de la petición volvería a atar la latencia y la disponibilidad de la compra a la del broker, que es justo lo que el outbox desacopla.
- **`catch (Exception)` SIN filtro sobre `OperationCanceledException`.** Una OCE ajena al `stoppingToken` (SqlCommand, timeout interno del cliente AMQP, el token enlazado del presupuesto) escapaba de `ExecuteAsync` y con `StopHost` tumbaba la API entera. Verificado reproduciendo la forma del bucle en un host mínimo. El consumidor ya lo tenía corregido y este no.
- **Presupuesto de dos intervalos para la tanda.** La transacción —y con ella el `sp_getapplock`— se mantiene abierta durante todo el diálogo con el broker. En estado sano son ~0,3 s, pero con la conexión abierta y el broker sin responder cada publicación espera hasta el `ContinuationTimeout` (20 s): una tanda de 50 podía tener la transacción abierta ~16 minutos (`log_reuse_wait_desc = ACTIVE_TRANSACTION`) y el resto de réplicas saltándose la vuelta.
- **`alreadyPublished` vive FUERA del delegado de la execution strategy.** Ante un fallo transitorio, EF **reejecuta el delegado entero**, incluidas las publicaciones ya hechas al broker, que no se pueden deshacer. Es la regla que este repo enuncia para `[Transactional]`, aquí incumplida hasta este arreglo.
- **El broker caído no consume intentos.** Medido: con 5 intentos cada 5 s bastaban **25 segundos** de broker caído —menos que el `start_period` de su propio contenedor— para dejar un mensaje con `Attempts=5`, fuera del filtro de pendientes y por tanto sin republicarse NUNCA, ni al volver el broker. Se corta la tanda (los siguientes fallarían igual) sin tocar `Attempts`; lo ya publicado sí se guarda.
- **Un fallo atribuible al mensaje sí consume intento y hace `continue`, no `break`:** el broker está vivo, así que un mensaje envenenado no debe bloquear la cabecera de la tanda (head-of-line blocking).
- **Traza completa solo cuando el mensaje se agota;** los intentos intermedios son ruido esperado.
- **`sp_getapplock` global en vez de claim por filas.** El claim (`LockedUntil` + `UPDATE ... OUTPUT`) obliga a gestionar la expiración para que una réplica muerta no deje filas bloqueadas para siempre, y añade dos columnas y una consulta con `UPDLOCK`/`READPAST` a cambio de un paralelismo que aquí no hace falta.
- **Corrección honesta registrada en el código original:** la primera versión justificaba el applock diciendo que el claim "destruye la garantía de orden que da `Sequence`". **Esa garantía no existe** (el IDENTITY se asigna al INSERT y la fila se ve al COMMIT), así que el argumento era falso aunque la decisión siga siendo la buena.
- **`@LockOwner = 'Transaction'`:** el bloqueo se suelta solo al commit o rollback, incluso si el proceso muere. Con `'Session'` quedaría atado a una conexión del pool, que se reutiliza para otra cosa: la receta para un bloqueo que no suelta nadie.
- **`@LockTimeout = 0`:** no esperamos. Si otro lo tiene, se salta la vuelta; encolar réplicas esperando un bloqueo solo acumula latencia.
- **Sin el applock**, con dos réplicas ambas leen el mismo lote y publican los mismos mensajes: no corrompe nada (el consumidor deduplica) pero dobla el tráfico del broker y del consumidor.

### `Shared/Messaging/RabbitMq/EventConsumer.cs`

- **Demuestra las cuatro cosas que hay que hacer bien en un consumidor:** ack manual, idempotencia por `MessageId`, reintentos acotados con DLQ, y prefetch.
- **Se HEREDA y aquí sí toca** (rules.md §3: heredar para reutilizar mecanismo, componer para reutilizar política). Es mecanismo puro y no sabe qué significa el mensaje. La alternativa era copiar doscientas líneas por consumidor, varias de ellas arreglos de bugs caros (el reintento publicado al exchange por defecto, los publisher confirms antes del ack, el contador propio de intentos): con dos copias, el próximo arreglo entra en una y la otra se queda con el bug.
- **Los consumidores viven en el mismo proceso que el publicador solo para que el ejemplo sea autocontenido;** el diseño no cambia si mañana son otro servicio: lo único compartido es el contrato del evento y el nombre de la cola.
- **`_subscription` cerrado por tipo:** pedir `EventSubscription` a secas devolvería «la última registrada» en cuanto haya dos consumidores, y el fallo sería un consumidor escuchando la cola de otro.
- **`OnExhaustedAsync` es el único momento en que «ya no habrá más intentos» es cierto.** Sin ese punto, un slice no puede distinguir «todavía no» de «no va a pasar»: marcar el fallo desde el `catch` de cada intento sería mentir mientras quedan reintentos, y no marcarlo nunca deja al cliente esperando. Lo destapó una revisión: `ReceiptStatus.Failed` existía, se había migrado y no lo escribía nadie.
- **`OnExhaustedAsync` corre en su propio scope y su propia transacción** (la del efecto ya se deshizo con el fallo) y **no puede lanzar**: si lanzara, el mensaje no llegaría a la DLQ y se quedaría dando vueltas.
- **El canal de consumo usa publisher confirms porque también PUBLICA (`ScheduleRetryAsync`).** Sin ellos, `BasicPublishAsync` vuelve sin excepción aunque el mensaje no llegue a ninguna cola (`mandatory: true` sin handler de retorno lo descarta en silencio) y justo después se hacía ack del original: pérdida silenciosa. Medido: borrando el binding del exchange de reintento, el mensaje se evaporaba sin un solo log de error.
- **Compartir el canal entre consumo y publicación es seguro solo porque `ConsumerDispatchConcurrency` vale 1** y las entregas se despachan de una en una. Si alguien la sube, la publicación necesita su propio canal: los `IChannel` no prometen ser thread-safe.
- **`CallbackExceptionAsync` suscrito:** sin él, una excepción dentro del dispatcher del canal (p. ej. un ack que falla porque el canal se cerró) se pierde — fallo completamente mudo.
- **QoS/prefetch:** sin él, RabbitMQ empuja la cola entera a la primera réplica que se conecte y las demás quedan ociosas mientras esa se atraganta.
- **Se comprueba el TIPO antes de deserializar.** `System.Text.Json` sobre un record posicional NO falla con un payload ajeno: rellena con default (0, null). Si el binding pasa a `product.*` —que es el motivo de usar un exchange topic—, un `product.created` se convertiría en un `ProductPurchased` con ceros y dispararía una alerta de stock falsa.
- **`catch (JsonException)` propio:** el `catch` genérico gastaba los 3 intentos y dos TTL para acabar igual en la DLQ. Un cuerpo que no parsea lanza antes de la comprobación de `@event is null`.
- **Contador REAL de intentos en cabecera propia.** `args.Redelivered` es una BANDERA del broker y no un contador: se pone a `true` en cuanto el mensaje se entregó alguna vez sin ack —incluido un reinicio del pod sin ningún fallo— así que eran 2 intentos como mucho y con 0 ms entre ellos, porque un requeue devuelve el mensaje a la CABEZA de la cola. Después se leyó de `x-death`, que sí cuenta pero es del broker.
- **`NotifyExhaustedAsync` va antes del nack:** después, el mensaje ya está en la DLQ y morirse entremedias dejaría el hecho sin registrar; en este orden, morir entremedias solo provoca una reentrega más.
- **El reintento se publica al exchange POR DEFECTO con la COLA como routing key.** Con un exchange de por medio, TODAS las colas de espera ligadas a él reciben una copia — y como el nombre lleva el TTL dentro, las de plazos anteriores siguen ahí y ligadas. Medido: un solo reintento aparecía en las tres colas de espera a la vez. El inbox deduplica, así que no se ejecuta de más, pero multiplica el tráfico y hace ilegible lo que pasa.
- **Publicar el reintento primero y confirmar después:** al revés, morir entremedias pierde el mensaje. Se prefiere duplicar a perder, misma regla que el outbox.
- **Con confirms activos, un mensaje que no encuentra cola destino lanza `312 NO_ROUTE`** en vez de perderse; si no se puede encolar el reintento, va a la DLQ.
- **`IsConcurrentDuplicate` se pregunta a través de un scope nuevo** porque el filtro de un `catch` corre fuera del scope que abrió `HandleAsync`.
- **La idempotencia vivía dentro del consumidor y por eso el arreglo del P0 se quedó sin test:** no había forma de hacer fallar el efecto sin un broker delante.

### `Shared/Messaging/RabbitMq/RabbitMqConnection.cs`

- **Una conexión TCP por proceso, muchos canales.** Abrir una conexión por mensaje es el error clásico: el handshake AMQP es caro y el broker limita las conexiones. Los `IChannel` son baratos pero no thread-safe, así que cada componente usa el suyo.
- **La conexión es perezosa y reintentable:** si el broker no está arriba al arrancar, la app no cae, y la API sigue aceptando compras acumulando eventos en el outbox.
- **Las suscripciones llegan por DI desde los slices;** aquí no se nombra ninguna cola. Vacío es un estado legítimo (una réplica que solo publica) y entonces solo se declara el exchange.
- **El fast-path fuera del semáforo es deliberado:** serializar cada publicación detrás de un lock sería un cuello de botella. Es seguro porque el campo solo se asigna cuando la conexión está lista del todo.
- **La conexión anterior se libera SIEMPRE antes de crear otra.** Sobrescribir el campo sin liberarla dejaría la vieja viva con su temporizador de recuperación: al volver el broker se recuperaría sola, `TopologyRecoveryEnabled` le devolvería su canal y su consumidor, y acabaríamos con DOS conexiones y DOS consumidores sobre la misma cola.
- **Se usa variable LOCAL y el campo se publica solo tras declarar la topología.** Si se asignara antes, el fast-path devolvería una conexión abierta pero SIN exchange y el publicador fallaría con un 404 de canal; y si `DeclareTopologyAsync` fallara, el campo quedaría asignado para siempre y la topología no se declararía nunca más.
- **Dispose en el `catch` de la topología: fuga de conexiones.** Sin él, una topología que falla deja la conexión huérfana y, con `AutomaticRecoveryEnabled`, viva y enganchada al broker para siempre porque nadie tiene la referencia. Medido: 22 fallos consecutivos = 22 conexiones fugadas, exactamente 1:1; a ese ritmo son ~8.600 al día hasta agotar los descriptores del broker.
- **Se loguea el MENSAJE y no la excepción entera** en el fallo de conexión: un broker caído es una condición esperada y recuperable, y el bucle reintenta cada pocos segundos. Volcar la traza inunda el log justo cuando hace falta leerlo.
- **Un `406 PRECONDITION_FAILED` NO es "el broker está caído":** es una cola que ya existe con argumentos distintos (caso típico: cambiar `RabbitMq:RetryDelaySeconds`, que fija el `x-message-ttl` de la cola de espera). Es un error de configuración, permanente, y tratarlo como caída dejaba la mensajería entera abajo reintentando en bucle con un simple WARNING.
- **Exchange `topic`:** un consumidor puede suscribirse a `product.*` sin que el publicador sepa quién escucha. Es lo ÚNICO compartido entre suscripciones.
- **Todo `durable` y mensajes persistentes:** sin eso, reiniciar el broker pierde la cola y su contenido.
- **Una DLX por suscripción.** La heredada es `fanout`, así que dos colas apuntando a la misma repartirían cada mensaje muerto a las DOS dead-letters: un fallo de órdenes aparecería también en la DLQ del catálogo.
- **La cola de espera se añade como topología NUEVA** en vez de cambiar el `x-dead-letter-exchange` de la principal: redeclarar una cola existente con argumentos distintos da 406 y cierra el canal, así que habría que borrar la cola en producción con sus mensajes. Así el despliegue es aditivo y sin parada.
- **La cola de espera NO se liga a ningún exchange,** por el mismo motivo del punto de `EventConsumer`: con un exchange de por medio, todas las colas de espera reciben copia de cada reintento (medido: aparecía en las tres a la vez).
- **`x-dead-letter-routing-key` explícita en la cola de espera:** sin ella conservaría la routing key con la que entró y el mensaje no encontraría la cola principal al volver.

### `Shared/Messaging/RabbitMq/RabbitMqEventPublisher.cs`

- **El canal se reutiliza entre publicaciones.** Antes se abría y cerraba uno por mensaje, y abrir un canal es un viaje de ida y vuelta al broker: con `Outbox:BatchSize` en 50, eran 50 canales por cada vuelta del publicador, cada uno con su negociación.
- **El acceso se serializa con un semáforo.** Los `IChannel` no prometen ser thread-safe. Hoy solo publica un `BackgroundService` en serie, pero esa suposición se rompe sola con el tiempo.
- **El canal se recrea si se ha cerrado** (caída del broker, un 406, un `NO_ROUTE` que tumbe el canal). Guardar uno muerto y publicar por él daría un error genérico en vez de reconectar.
- **`DeliveryModes.Persistent`:** con colas durables pero mensajes transitorios, la cola sobrevive vacía — lo peor de los dos mundos.
- **`mandatory: true`:** si ninguna cola encaja, el broker devuelve el mensaje.
- **`BrokerUnavailableException` y no `InvalidOperationException`:** el outbox necesita distinguir "broker caído" (no cuenta como intento) de "este mensaje falla" (sí cuenta).
- **Publisher confirms:** sin ellos, el outbox marcaría como enviado algo que el broker nunca recibió.

### `Shared/Messaging/RabbitMq/EventSubscription.cs`

- **Existe porque la topología vivía entera en `RabbitMqOptions`, escrita para UNA cola y UN consumidor.** En cuanto hubo un segundo evento (`order.placed`) el fallo era silencioso: el publicador usa `mandatory: true` con publisher confirms, así que un evento sin cola vuelve como **312 NO_ROUTE**, el outbox lo cuenta como intento fallido y el mensaje se agota en `MaxPublishAttempts`. La compra funciona y el consumidor no se entera nunca.
- **El nombre de la cola es del SLICE y el mecanismo es de `Shared/Messaging`.** Por eso es un dato que el slice pasa a `AddEventConsumer` y no una sección de configuración más: añadir un consumidor no puede exigir tocar `Shared`, o la dirección Web → Features → Shared se invierte.
- **`DeadLetterExchange` es un campo y no una fórmula.** La cola del catálogo ya existe en los brokers con `x-dead-letter-exchange = apiecommerce.events.dlx`; redeclararla con otro valor da 406, cierra el canal y deja la mensajería abajo hasta que alguien borre la cola a mano. El catálogo conserva la heredada y los slices nuevos usan una por cola.
- **Una DLX por cola importa:** la heredada es `fanout`, así que dos colas dead-letterando a ella harían que un fallo de órdenes apareciera también en la DLQ del catálogo.
- **El nombre de la cola de espera lleva el TTL dentro, y eso es lo que hace configurable `RetryDelaySeconds`.** `x-message-ttl` se fija al declarar la cola: cambiarlo sobre una existente da 406, así que desplegar un plazo nuevo obligaba a borrar la cola en producción con sus mensajes dentro. Con el plazo en el nombre, cambiarlo declara una cola nueva: despliegue aditivo y sin parada, y la vieja se vacía sola porque su dead-letter sigue apuntando a la principal.
- **El precio es una cola huérfana por cada plazo usado:** vacías, se ven en la UI y se borran a mano cuando estorben — mucho más barato que una parada.
- **Se descartó poner el TTL en el MENSAJE:** en una cola FIFO, un mensaje con TTL largo bloquea a todos los de detrás aunque ya hayan caducado (head-of-line blocking), porque el broker solo mira la cabeza.
- **`EventSubscriptionOf<TConsumer>` no es ceremonia:** con varias suscripciones registradas, inyectar `EventSubscription` a secas daría «la última registrada», un bug que solo aparece al añadir el segundo consumidor. Con el tipo cerrado, el que se equivoque no compila.

### `Shared/Messaging/RabbitMq/IDeadLetterAdmin.cs`

- **Existe porque la DLQ era un callejón sin salida.** El consumidor sabe mandar un mensaje allí, y desde `planning/20` el slice se entera y marca la orden como fallida, pero reemitir el trabajo exigía entrar a la consola del broker a mano.
- **Reemitir es una decisión HUMANA, no un job.** Si un mensaje agotó sus reintentos es porque algo estaba roto de verdad; reencolarlo automáticamente solo repite el fallo, convierte la DLQ en un bucle caro y esconde el incidente. De ahí endpoint de administración y no `BackgroundService`.
- **`GetStatusAsync` devuelve recuentos y no contenido:** volcar los payloads de la DLQ es una fuga esperando a que alguien publique un evento más rico.
- **Reiniciar el contador de reintentos es la mitad del valor del replay.** Sin eso el mensaje vuelve con el presupuesto gastado y muere en la primera entrega: la herramienta para recuperar mensajes no recuperaría ninguno. Es exactamente la razón de que el contador sea nuestro (`RetryAttempts`) y no `x-death`, que sobrevive al paso por la DLQ.
- **Publicar primero, confirmar después:** al revés, morir entremedias pierde el mensaje. Lo peor que pasa en este orden es una reentrega, que el inbox deduplica por `MessageId`.
- **No hace falta llevar la cuenta de qué se reemitió:** reemitir algo que sí se procesó no ejecuta nada, lo reconoce el inbox.
- **`DeadLetterQueueNotFoundException` es un control de seguridad, no una validación de formulario.** El nombre llega en la petición; sin comprobarlo contra las suscripciones registradas, el endpoint movería mensajes de cualquier cola del broker, que es compartido con otros proyectos.
- **404 y no 400:** para quien llama, una cola que este servicio no consume no existe.

### `Shared/Messaging/RabbitMq/NoDeadLetterAdmin.cs`

- **Null Object que falla en CERRADO, al revés que `NoCacheService` o `NoIdempotencyStore`.** Esos degradan en abierto porque envuelven optimizaciones con fuente de verdad alternativa; aquí no hay nada que devolver — «no hay mensajes muertos» sería mentira, no degradación (rules.md §8).
- **Existe para que el controller no tenga que preguntar si hay broker:** la decisión se toma una vez, al construir el grafo de DI.

### `Shared/Messaging/RabbitMq/RabbitMqDeadLetterAdmin.cs`

- **`_subscriptions` es la allowlist.** Son las mismas suscripciones que declara `RabbitMqConnection`, o sea las que registró cada slice: un slice nuevo aparece aquí solo, y una cola que este servicio no consume no es alcanzable desde el endpoint.
- **`QueueDeclarePassiveAsync`:** pregunta por una cola que ya existe y devuelve su recuento sin crearla ni tocar sus argumentos. Declararla en activo sería pedir un 406 el día que alguien cambie el TTL, y además este método es de solo lectura y debe serlo.
- **Publisher confirms en el canal del replay:** sin ellos `BasicPublishAsync` vuelve sin excepción aunque el mensaje no llegue a ninguna cola, y justo después haríamos ack en la DLQ: pérdida silenciosa del único mensaje que quedaba.
- **`BasicGet` y no un consumidor:** la operación tiene que TERMINAR y quien la lanza necesita saber cuántos movió.
- **Presupuesto de reintentos a cero al reemitir** (misma razón que en `IDeadLetterAdmin`).
- **Se publica al exchange principal con la routing key de la suscripción:** el mensaje recorre el MISMO camino que uno nuevo, en vez de colarse por la puerta de atrás.
- **Si no se puede publicar, se devuelve el mensaje a la dead-letter (nack con requeue) y se para:** es el sitio del que se recupera.
- **El ack va DESPUÉS del publish.**

### `Shared/Messaging/RabbitMq/RabbitMqOptions.cs`

- **`Queue`/`RoutingKey` siguen aquí solo por compatibilidad** con los despliegues que ya los traen en su configuración. Un consumidor nuevo NO añade una opción aquí: declara su `EventSubscription` en su slice. Meter cada cola en esta clase haría que `Shared` conociera todos los slices, justo la dependencia que el vertical slicing evita.
- **`PublishIntervalSeconds`, `MaxPublishAttempts` y `BatchSize` se movieron a `OutboxOptions`:** son del outbox, que es agnóstico al broker.
- **`MaxDeliveryAttempts` existió, se quitó y volvió.** Se quitó porque documentaba algo que no ocurría: el consumidor miraba `args.Redelivered`, una bandera y no un contador, así que eran 2 intentos con 0 ms entre ellos (un requeue devuelve el mensaje a la cabeza de la cola). Vuelve ahora que hay un contador real detrás (`RetryAttempts`).
- **`RetryDelaySeconds`:** sin espera, reintentar no arregla nada — si el fallo es un timeout de la base o un servicio saturado, los reintentos caen dentro del mismo incidente y se agotan antes de que nada se haya recuperado.
- **`DeadLetterExchange` es la heredada del catálogo y no se generaliza:** las colas ya declaradas la llevan en su `x-dead-letter-exchange` y redeclararlas con otro valor da 406. Los slices nuevos usan `EventSubscription.For`, que además evita que el fanout reparta los muertos de un slice a la DLQ del otro.
- **`DeadLetterQueue` y `RetryQueue` se movieron a `EventSubscription`:** son de UNA cola, no del broker; con dos consumidores solo podían describir a uno.

### `Shared/Messaging/RabbitMq/RetryAttempts.cs`

- **Cabecera nuestra (`x-retry-attempt`) y no `x-death`, por dos problemas reales:**
  1. **Un replay desde la DLQ no reseteaba el presupuesto.** `x-death` sobrevive al paso por la DLQ, así que un mensaje reencolado por un operador volvía con el contador agotado y moría en la primera entrega. Con cabecera propia, el procedimiento de replay es borrarla — una, con nombre conocido.
  2. **El parseo era frágil.** `x-death` es una lista de diccionarios cuyos valores de texto viajan como `byte[]`: compararlos con un `string` sin convertir devuelve `false` en silencio (ya pasó). Y había que filtrar por el NOMBRE de la cola de reintento, que lleva el TTL dentro, o sea que el contador se habría reseteado solo al cambiar el plazo.
- **Es una función pura y vive fuera del consumidor para poder probarla sin broker** — misma razón por la que salieron de ahí el efecto y la unidad transaccional.
- **`Read` acepta los tres formatos** (`int` propio, texto crudo o `byte[]` de quien la ponga a mano desde la UI del broker). Un valor ilegible cuenta como cero: preferible un reintento de más que descartar un mensaje por no saber leer una cabecera.
- **Un valor negativo se normaliza a cero:** solo puede venir de una edición manual y daría un presupuesto infinito de reintentos.
- **`With` copia y no muta:** el diccionario de entrada es del mensaje que estamos consumiendo, y reutilizarlo acopla lo que publicamos a lo que recibimos.

---

## Features/Catalog

### `Features/Catalog/Controllers/ProductController.cs`

- **La clase lleva el requisito de autorización más DÉBIL.** En ASP.NET Core varios `[Authorize]` se combinan (AND), no se sobreescriben: poner `[Authorize(Roles = "admin")]` en la clase y `[Authorize]` en una acción no relaja nada, la acción seguiría exigiendo admin. El único atributo que gana sobre la clase es `[AllowAnonymous]`.
- **La compra es el caso que obliga a ese diseño.** `POST /buy` solo pide estar autenticado, con cualquier rol. El código de referencia del curso exigía admin para comprar, que en una tienda no tiene sentido.
- **`If-Match` se parsea con `EntityTagHeaderValue.TryParseList`, no a mano.** La cabecera admite una lista (`If-Match: "a", "b"`) y la precondición se cumple si alguna casa; tratarlo como token único devolvía 400 a un cliente conforme, y también a cualquiera que mandara la cabecera dos veces, porque `StringValues.ToString()` las une con coma.
- **El `TrimStart('W', '/')` anterior era una trampa latente.** Con un ETag sin comillas que empezara por 'W' o '/' se comía caracteres del token. Hoy no podía pasar (el base64 de un rowversion empieza siempre por 'A'), pero dependía del formato del dato, no del código.
- **RFC 9110 §13.1.1: `If-Match` exige comparación FUERTE.** Un validador débil (`W/"..."`) no puede satisfacer la precondición. Antes se aceptaba.
- **Sin quitar el envoltorio del RFC (comillas, prefijo `W/`), el token no casaría nunca con el de la base** y todo PATCH con `If-Match` devolvería 412, un fallo desagradable porque parece un conflicto real.
- **`[ProducesResponseType(412)]` y el 400 del token ilegible se declaran a propósito:** sin ellos Swagger no documenta lo único que un cliente necesita para usar la feature.
- **El ETag va entre comillas porque el RFC 9110 lo exige**, y se publica para que el cliente pueda devolverlo en `If-Match` y no pisar el cambio de otro.
- **`dto.IfMatch` lo rellena el controller** para que el servicio y las reglas no conozcan `HttpContext`. Es opcional: sin `If-Match` el PATCH se comporta como siempre.
- **`[Consumes("multipart/form-data")]` es explícito** para que Swagger pinte el selector de archivo y para que un cliente que mande JSON reciba un 415 claro en vez de un 400 raro.
- **`[RequestSizeLimit]` corta la petición antes de leerla entera:** validar el tamaño después de haber recibido 500 MB no protege de nada.
- **El controller adapta `IFormFile` (framework) a `FileUpload` (dominio):** el servicio no debe conocer ASP.NET Core.
- **`[Idempotent]` es un ATAJO, no la garantía.** Responde un reintento sin tocar la base y frena una tormenta de duplicados antes de que se apile sobre la misma fila. Si Redis no contesta, se salta y no pasa nada: quien garantiza que no se compra dos veces es `ProductService`, que escribe la marca en la MISMA transacción que el descuento de stock.
- **Sin `[Transactional]` en `buy`:** la transacción la abre `ProductService` con `ITransactionRunner`, que sí es compatible con la estrategia de reintentos de EF.
- **La cabecera `Idempotency-Replayed` se pone desde `outcome.WasReplayed`,** un hecho de negocio que reporta el servicio; así marca TODOS los replays y no solo los que resuelve el atajo de Redis. El controller traduce protocolo ↔ dominio.

### `Features/Catalog/Controllers/CategoryController.cs`

- **Misma regla de autorización que en `ProductController`:** clase con el requisito más débil (autenticado), `[AllowAnonymous]` para abrir los GET, `[Authorize(Roles = ...)]` por acción para las escrituras, porque varios `[Authorize]` se combinan con AND.
- **Sin `try/catch` de negocio:** las excepciones de dominio del servicio las traduce `GlobalExceptionHandler` a `ProblemDetails` (`AGENTS/docs/04-error-handling.md`).
- **Una página fuera de rango devuelve 200 con `[]` y el total real, no 404.**

### `Features/Catalog/Service/ProductService.cs`

- **Composición, no herencia:** este servicio necesita el CRUD genérico *y* consultas propias *y* el mapper; con composición cada colaborador entra por el constructor y se ve de un vistazo qué usa. Es el caso que la herencia hacía incómodo.
- **`GetAll`/`GetById`/`GetPaged` no se delegan** porque el CRUD genérico no carga la navegación `Category` y `ProductDto.CategoryName` saldría siempre nulo. Poder sustituir dos de las cinco operaciones sin tocar el componente CRUD es justo lo que compra la composición.
- **`Delete` no se delega tal cual:** además de borrar la fila hay que borrar el archivo, o cada producto eliminado deja su imagen huérfana en disco para siempre. El borrado del archivo va DESPUÉS del de base: si la fila no se pudo borrar (409 por FK), el archivo debe seguir existiendo.
- **`SetImage` guarda la nueva imagen antes de borrar la vieja:** si la validación falla, el producto conserva la que ya tenía.
- **La transacción de la compra vive en el servicio, no en un atributo del controller.** «Descontar stock + emitir el evento + dejar constancia del intento» es una regla de NEGOCIO. Antes dependía de que alguien no olvidara poner `[Transactional]` en la acción: llamar a `BuyAsync` desde un job o desde otro endpoint descontaba stock sin emitir el evento, en silencio y sin error.
- **La lambda del `ITransactionRunner` es REPLAYABLE** (relee todo lo que necesita): es lo que exige el runner para poder reintentar ante un fallo transitorio.
- **La comprobación de idempotencia va DENTRO de la transacción**, y ahí está la diferencia con la versión que vivía en Redis: la marca y el efecto se confirman juntos o no se confirman. No existe la ventana en la que la compra ocurrió y nadie la recuerda (proceso muerto entre el commit y el guardado), ni la de «el almacén no contestó, ejecuto sin garantía». Si la base no está, tampoco hay compra.
- **`TryDecrementStockAsync` en vez de comprobar y descontar por separado:** `product.Stock < dto.Quantity` seguido de un descuento es read-then-write, y entre las dos cosas cabe otra compra que vende dos veces la última unidad.
- **Relectura tras el descuento:** `ExecuteUpdate` no toca el change tracker, así que la instancia que ya teníamos sigue con el stock anterior.
- **El evento se ESCRIBE en el outbox y se PUBLICA después (`OutboxPublisher`).** Publicar directo a RabbitMQ en esa línea ataría la compra a que el broker esté vivo y dejaría anunciada una compra que todavía podría no confirmarse.
- **`_commands.Record` no hace `SaveChanges`:** lo confirma el `SaveChangesAsync` de después, junto al evento y al descuento. El choque de clave primaria de `ExecutedCommands` sale ahí.
- **El `catch` de intento duplicado no es una carrera que evitar, es la carrera resolviéndose.** Otra réplica ejecutó el mismo intento y confirmó primero; nuestra transacción entera se deshizo —incluido el descuento de stock—, así que no hay nada que compensar: basta devolver lo que hizo el ganador. La base bloquea a la segunda inserción en la clave hasta que la primera confirma, por eso no hacen falta ni reserva, ni TTL, ni un estado «en curso».

### `Features/Catalog/Service/CachedCategoryService.cs`

- **Es el principio del proyecto («componer para reutilizar política») aplicado a un aspecto transversal.** `CategoryService` no sabe que existe una cache: sigue siendo el servicio de negocio puro y se puede testear sin Redis. Quitar la cache es borrar una línea del registro de DI.
- **Equivalente a `@Cacheable`/`@CacheEvict` de Spring, pero explícito:** se ve qué clave se lee y qué claves se tiran, en vez de deducirlo de una anotación y una convención de nombres.
- **Solo se cachean lecturas de catálogo**, que son públicas (`[AllowAnonymous]`) e iguales para todos. Nada que dependa del usuario autenticado entra aquí: una cache compartida con datos por-usuario es una fuga de datos entre cuentas.
- **TTL de 60 s:** el catálogo cambia poco, pero la ventana de datos rancios tampoco debe ser grande.
- **Las páginas no se cachean:** cada combinación `page`/`pageSize` sería una clave distinta que ninguna invalidación conoce. Cachear listados paginados exige invalidar por prefijo (o versionar la clave de colección), y eso ya no es una línea de decorador.
- **La invalidación va después de que la escritura haya ido bien:** si el servicio lanza (409 por nombre duplicado, 404...), no se tira una cache que sigue siendo válida.

### `Features/Catalog/Service/ProductRules.cs`

- **Validar la FK aquí es obligatorio:** si se deja pasar un `CategoryId` inexistente, EF revienta con un error de clave foránea y el cliente recibe un 500 en vez del 400 que corresponde.
- **`EnsureVersionMatches` cierra el *lost update* entre dos administradores.** Escenario: A lee el producto, B lo edita, A guarda. Sin esto A pisa el cambio de B sin que nadie se entere, y no lo salva que `Product.RowVersion` exista: el PATCH relee la fila, así que EF compara contra el rowversion que acaba de leer —el de B— y todo cuadra. Lo único que rompe el empate es el token que A leyó en SU GET, y ese solo puede llegar del cliente.
- **`If-Match` es opcional a propósito:** exigirlo rompería a todos los clientes actuales, y la protección la pide quien sabe que está editando algo que leyó antes.
- **La ventana entre la comprobación y el UPDATE la cubre EF:** `[Timestamp]` mete el rowversion en el `WHERE` y, si cambia entremedias, sale `DbUpdateConcurrencyException` → 409. Dos redes, cada una para su carrera.
- **Un `If-Match` ilegible es 400 y no 412:** no es que la precondición falle, es que ni siquiera es un token.
- **Categoría inexistente es 400 y no 404:** el recurso pedido es el producto; la categoría inexistente hace que el request sea inválido en sí mismo.

### `Features/Catalog/Service/CategoryRules.cs`

- **Se puede instanciar con un `ICategoryRepository` falso y probar sola.** Es todo lo que `Category` tiene de propio: el CRUD lo pone `CrudService`.

### `Features/Catalog/Service/CategoryService.cs`

- **Los cinco reenvíos al CRUD son el precio explícito de la composición.** A cambio, la clase no tiene estado heredado que pueda romperse, y el día que `Category` necesite algo propio (p. ej. `GetWithProductCountAsync`) se agrega aquí sin tocar el CRUD.
- **La versión anterior (conservada comentada)** estaba escrita a mano, sin reutilizar nada: cada entidad nueva copiaba y pegaba esos cinco métodos, y lanzaba excepciones BCL (`InvalidOperationException`/`KeyNotFoundException`) como señal de negocio, que el controller traducía a HTTP con `try/catch`.

### `Features/Catalog/Service/IProductService.cs`

- **La subida de imagen es un endpoint aparte y no un campo del PATCH:** subir un binario es `multipart/form-data`, y meterlo en el mismo endpoint que el JSON obliga a cambiar el content-type de todos los clientes existentes. El código de referencia hizo exactamente eso y rompió su propio PUT.
- **`intent` no tiene valor por defecto a propósito.** La idempotencia de la compra es una invariante suya, no algo que dependa de que el controller lleve puesto un atributo: llamarla desde un job o desde otro endpoint sin declarar la intención perdería la garantía en silencio. Es el bug que motivó mover la transacción al servicio. Renunciar se escribe `CommandIntent.None`.
- **`WasReplayed` es un hecho de negocio;** traducirlo a una cabecera es cosa del adaptador HTTP.

### `Features/Catalog/Repository/ProductRepository.cs`

- **`ThenByDescending(p => p.Id)` es un desempate necesario:** dos productos creados en el mismo tick harían el orden no determinista y una fila podría repetirse entre páginas.
- **El `COUNT` va sobre la misma consulta base que la página** (EF elimina el `Include` y el `OrderBy` al traducirlo). Contar sobre `_db.Products` a secas funciona solo mientras no haya filtros; en cuanto se añada un `Where`, `TotalItems` mentiría.
- **`GetBySkuAsync` no rastrea:** desde que la compra usa `TryDecrementStockAsync`, nadie modifica esa instancia; rastrearla solo costaba memoria y un snapshot inútil.
- **`ExecuteUpdateAsync` emite un solo UPDATE con su WHERE,** sin cargar la entidad ni pasar por el change tracker. La condición `Stock >= quantity` se evalúa dentro de la sentencia, así que entre comprobar y descontar no cabe nadie: si dos peticiones llegan a la vez, la base serializa los dos UPDATE sobre la misma fila y la segunda ve el stock ya descontado. Devuelve filas afectadas: 0 = la condición no se cumplió.
- **El instante se captura FUERA del árbol de expresión.** Con `_ => DateTime.Now` dentro, EF no lo evalúa en cliente: lo traduce a `GETDATE()`, o sea el reloj del servidor SQL. El resto del proyecto estampa con el reloj del PROCESO (`AppDbContext.StampAuditFields`), así que en contenedores con zonas horarias distintas un producto comprado y el mismo producto editado por PATCH acababan con marcas de tiempo desfasadas horas.
- **`ExecuteUpdate` no pasa por `SaveChangesAsync`,** así que la auditoría automática de `AppDbContext` no se dispara y `UpdatedAt` hay que estamparlo a mano.
- **La versión anterior de `BuyProduct` (conservada comentada)** aplicaba la regla de negocio (stock suficiente) dentro del repositorio y devolvía un `bool` que no distinguía 404 de 409. Hoy esa decisión vive en `ProductService.BuyAsync` y el `UpdatedAt` lo estampa `AppDbContext`.

### `Features/Catalog/Repository/IProductRepository.cs`

- **Las variantes `...WithCategoryAsync` existen aparte de las genéricas** porque sin el `Include` de la navegación, `ProductDto.CategoryName` sale vacío en silencio.
- **`TryDecrementStockAsync` devuelve `bool` y no lanza:** el repositorio informa de un hecho («no se pudo descontar») y es el servicio quien decide que eso es un 409.
- **Es la herramienta correcta para un CONTADOR.** La concurrencia optimista (`Product.RowVersion`) sirve para *editar* una entidad —dos admins tocando el mismo producto—, pero aplicada a un contador con mucha contención hace que peticiones válidas se rechacen al agotar los reintentos. Aquí no hay nada que reintentar: la base evalúa la condición y decrementa en la misma sentencia.
- **El `BuyProduct` antiguo (conservado comentado)** devolvía un `bool` que mezclaba «no existe» (404) con «stock insuficiente» (409) y obligaba al repositorio a aplicar una regla que no le corresponde.

### `Features/Catalog/Messaging/ProductPurchasedConsumer.cs`

- **Toda la fontanería AMQP —ack manual, deduplicación por `MessageId`, reintentos con espera, DLQ, prefetch— vive en `EventConsumer<TConsumer,TEvent>`,** con los porqués de cada decisión; aquí solo queda lo que es del catálogo: qué evento se escucha y quién lo atiende.
- **Estaba todo en esta clase hasta que apareció el segundo consumidor (`order.placed`).** Copiarla habría duplicado doscientas líneas donde varias son arreglos de bugs medidos: el próximo arreglo entraría en una copia y la otra se quedaría con el bug.
- **El handler se resuelve del scope del mensaje y no se inyecta:** el consumidor es singleton y el efecto es `Scoped`, y así comparte el `AppDbContext` con la transacción del inbox, que es lo que hace que la marca y el efecto se confirmen juntos.

### `Features/Catalog/Messaging/IProductPurchasedHandler.cs`

- **El efecto se extrajo a una interfaz porque, siendo un método privado del consumidor, no había forma de hacerlo fallar,** así que el arreglo del P0 —marca y efecto en la misma transacción— se quedó sin un test que lo cubriera. Un efecto inyectable permite escribir el único test que importa: el del efecto que revienta.
- **Y deja al consumidor siendo lo que debe ser:** fontanería AMQP (ack, reintentos, DLQ) que no sabe qué significa el mensaje que transporta.
- **`LowStockNotifier` no debe hacer llamadas de red.** En un sistema real esto notificaría a compras, escribiría una proyección de lectura o llamaría a un webhook; si hace una llamada de red deja de ser transaccional, porque lo que corre dentro de la transacción del inbox tiene que poder deshacerse con ella. Un efecto externo se emite como OTRO evento del outbox.

### `Features/Catalog/CatalogExtensions.cs`

- **El slice registra todo lo suyo —repositorios, reglas, CRUD compuesto y servicios— en un solo sitio.** Añadir una entidad al catálogo (unidad de medida, etiqueta, marca) es tocar esta carpeta y este archivo, no siete carpetas repartidas por el proyecto.
- **Una entidad así NO crea un slice nuevo:** `UnitOfMeasurement` o `ProductTag` no son contextos acotados, son parte del catálogo. Un slice por entidad degenera en la misma dispersión que se venía a quitar, solo que con más carpetas.
- **Registro CERRADO de reglas por entidad:** el contenedor prefiere siempre la coincidencia exacta sobre el genérico abierto `NoEntityRules<,,>`, así que las cerradas ganan.
- **`ICrudService` no lleva `TEntity`** (para que el controller no pueda ver la entidad), así que su registro también es cerrado: una línea por entidad.
- **Category se registra DECORADO:** el contenedor construye el servicio real y lo envuelve en el que cachea; quien pide `ICategoryService` recibe el decorador y no se entera. El tipo CONCRETO se registra aparte porque, si solo estuviera la interfaz, el decorador no tendría de dónde sacar el servicio interno sin recursión infinita.
- **Quién reacciona a un evento del catálogo es asunto DEL CATÁLOGO.** `Shared/Messaging` pone el mecanismo (conexión, outbox, publicador) y la condición de «hay broker»; registrarlo allí obligaría a `Shared` a conocer este tipo e invertiría la dirección de dependencias declarada en el composition root.
- **El efecto (`LowStockNotifier`) se registra SIEMPRE, también sin broker:** es lógica del slice y así se puede probar sin AMQP delante, que es justo lo que faltaba para cubrir el P0 del consumidor.
- **La dead-letter de esta cola es la HEREDADA (`{Exchange}.dlx`), no una por cola como en los slices nuevos.** No es incoherencia: la cola ya existe en los brokers con ese `x-dead-letter-exchange`, y redeclararla con otro da 406 PRECONDITION_FAILED y deja la mensajería abajo. Cambiarlo exige borrar la cola, que es una parada, y no compensa por estética.

### `Features/Catalog/Mapping/ProductProfile.cs`

- **PATCH parcial con `s.X ?? d.X` en vez de `ForAllMembers(o => o.Condition(...))`:** la `Condition` recibe el valor ya convertido al tipo del destino, así que un `int?` nulo llegaba como 0 y el PATCH machacaba `CategoryId`/`Stock`/`Price` con ceros (`CategoryId = 0` reventaba la FK y salía un 500). La forma explícita no depende de la semántica interna de AutoMapper.
- **Semántica del PATCH:** omitir el campo (o enviarlo null) significa «no tocar»; para vaciar `Description`/`ImageUrl` hay que enviar `""`, no null.
- **`CategoryName` viaja plano en la lectura** porque la navegación puede venir sin cargar.
- **`RowVersion` se convierte a base64 al leer:** es binario en la base y texto en una cabecera HTTP.
- **`UpdateProductDto.RowVersion` NO se mapea a la entidad:** lo gestiona SQL Server, y el valor del cliente sirve solo para comparar (ver `ProductRules`).

### `Features/Catalog/Mapping/CategoryProfile.cs`

- **Los campos de auditoría los estampa `AppDbContext`, no el mapper**, por eso se ignoran en los mapeos de escritura.

### `Features/Catalog/Models/Category.cs`

- **El índice único en BD es la ÚNICA garantía real de unicidad.** `CategoryRules` comprueba el nombre antes de escribir, pero entre esa comprobación y el INSERT cabe otra petición: dos POST simultáneos con el mismo nombre pasaban los dos. La regla sigue existiendo porque da un 409 con mensaje útil en el caso normal; el índice es la red que atrapa la carrera.
- **`MaxLength` no es cosmético:** sin él la columna es `nvarchar(max)` y SQL Server no puede indexarla (el límite de clave son 900 bytes). Además hace que la BD imponga lo mismo que promete el DTO.

### `Features/Catalog/Models/Product.cs`

- **Longitudes alineadas con las DataAnnotations de los DTOs:** la base impone lo mismo que promete el contrato de la API.
- **`RowVersion`: lo que SÍ garantiza hoy** es que un PATCH de administrador que toque `Stock` choque (409) con una compra concurrente, porque `TryDecrementStockAsync` cambia el rowversion por fuera del change tracker.
- **El *lost update* entre dos administradores está cerrado (2026-09-06) pero NO por esta columna sola:** por sí misma no puede, porque el PATCH relee la fila y EF compara contra el rowversion recién leído. Lo cierra publicar el token como `ETag` en el GET y compararlo contra el `If-Match` del cliente (`ProductRules`); la columna sigue siendo necesaria como segunda red, para la ventana entre esa comparación y el UPDATE.
- **`RowVersion` NO es lo que impide sobrevender stock:** eso lo resuelve el UPDATE condicional atómico de `TryDecrementStockAsync`. La concurrencia optimista aplicada a un contador con mucha contención rechaza compras válidas al agotar los reintentos.
- **La navegación es opcional en C# (`Category?`) a propósito:** la relación sigue siendo obligatoria en la base porque `CategoryId` es `int` no-nullable, pero dejarla como `required Category` impedía que AutoMapper construyera un `Product` desde `CreateProductDto` (`AGENTS/docs/05-convenciones.md` → Mapping).
- Referencia que estaba enlazada en el archivo: <https://learn.microsoft.com/es-mx/ef/core/modeling/relationships>
- `SKU` = Stock Keeping Unit, con formato tipo `PROD-001-BLK-M`.

### `Features/Catalog/Events/ProductPurchased.cs`

- **Vive en `Catalog` y no en `Shared/Messaging` porque es vocabulario del catálogo** (SKU, stock, producto). Lo que sí es de todos es el contrato `IDomainEvent` —el mecanismo—, y ese vive en `Shared`. La regla: ¿esto tiene lenguaje propio de un contexto, o es mecanismo de ninguno?
- **Lleva los datos que el consumidor necesita para actuar sin volver a consultar al emisor:** un evento que obliga a llamar de vuelta reintroduce el acoplamiento que la mensajería venía a quitar.

### `Features/Catalog/Dtos/ProductDto.cs`

- **Se expone el id de la categoría, no la navegación completa.** El nombre viaja plano porque el listado de productos lo necesita (`AGENTS/docs/01-capas-y-contratos.md`).
- **`RowVersion` viaja como `string` y no como `byte[]`** porque su destino es una cabecera HTTP, que es texto.

### `Features/Catalog/Dtos/UpdateProductDto.cs`

- **Todos los campos son nullable a propósito:** un `decimal Price` no-nullable llegaría como 0 cuando el cliente no lo envía y borraría el precio. El profile solo mapea los miembros no nulos.
- **`IfMatch` es una LISTA** porque el RFC 9110 permite `If-Match: "a", "b"` y la precondición se cumple si alguna casa. Tratarlo como token único devolvía 400 a un cliente conforme, y a cualquiera que mandara la cabecera dos veces, porque `StringValues.ToString()` las une con coma.
- **`[JsonIgnore]` en `IfMatch`: no se enlaza desde el cuerpo.** Sin eso, Swagger la publicaba como un campo más del body y un cliente podía mandarla ahí para que el controller la pisara en silencio.
- **Vive en el DTO y no como parámetro de `ICrudService.UpdateAsync`** para no meter una preocupación de HTTP en el contrato genérico del CRUD, que comparten todas las entidades.

### `Features/Catalog/Dtos/UpdateCategoryDto.cs`

- **Todo opcional:** lo que no venga en el JSON no se toca, porque el profile ignora los miembros nulos.

---

## Features/Accounts

### `Features/Accounts/RefreshTokenCookie.cs`

- **Por qué cookie y no cuerpo de la respuesta.** Con `HttpOnly` el JavaScript de la página no puede leerla: un XSS puede hacer peticiones en nombre del usuario mientras la pestaña esté abierta, pero no puede robarse la sesión y usarla desde otro sitio durante semanas. El access token sí viaja en el cuerpo porque dura 15 minutos y no puede renovarse solo; el refresh es el que de verdad hay que proteger.
- **El precio de la cookie**, que conviene tener presente: exige CORS con credenciales, obliga a HTTPS y complica a un cliente móvil o de escritorio, que no tiene cookies de balde.
- **`Secure` se decide por `Request.IsHttps`, no por entorno.** Una cookie `Secure` sobre HTTP el cliente ni la guarda: el login respondería 200, la cookie no se guardaría y el refresh fallaría siempre, sin un solo error en el servidor. La primera versión miraba `IsDevelopment()`, y eso rompía en cuanto el entorno se llamaba de otra forma: el host de tests usa `"Testing"` y sirve por HTTP, así que marcaba la cookie como Secure y ningún test de sesión podía pasar. Lo cazaron los tests.
- **Con el esquema real se ajusta solo**: en producción se sirve por HTTPS (hay HSTS), así que la cookie sale `Secure`; en local y en los tests, que van por HTTP, no. Detrás de un proxy TLS esto depende de `UseForwardedHeaders`, que ya está puesto: sin él, `IsHttps` sería false y la cookie viajaría sin `Secure` en producción.
- **`SameSite=Strict` y no `Lax`.** `Lax` bastaría para navegación de primer nivel, pero aquí no hace falta: el refresh siempre lo dispara la propia aplicación, así que Strict corta el CSRF de raíz sin coste.
- **`Clear` tiene que usar las mismas opciones con las que se escribió.** Un `Delete` con Path o SameSite distintos no borra nada: el navegador lo trata como otra cookie y la original sigue ahí. Es un fallo silencioso clásico del logout.

### `Features/Accounts/RefreshTokenOptions.cs`

- **Nada de prefijo `__Host-` en el nombre de la cookie.** Ese prefijo obliga al navegador a exigir `Secure` **y** `Path=/` **y** ningún `Domain`, y si algo no cuadra descarta la cookie sin decir nada. Aquí choca dos veces: la cookie va con `Path` acotado a `/api/v1/auth` —no hay razón para mandarla en cada petición al catálogo— y en Development se sirve por HTTP, donde `Secure` no vale.
- **El fallo habría sido de los peores**: el login responde 200, la cookie no se guarda, y el refresh falla siempre sin un solo error en el servidor. Lo que aporta el prefijo —que un subdominio no pueda sobrescribir la cookie— se cubre aquí con `SameSite=Strict` y con que la API no comparta dominio con nada.
- **La ventana de gracia (`ReuseGraceSeconds`) no es un parche.** Sin ella la detección de reuso es inutilizable en la práctica: un móvil o una SPA lanzan varias peticiones a la vez; si dos reciben 401 casi al mismo tiempo, las dos refrescan con el mismo token y la segunda parece un ladrón. El resultado sería cerrar la sesión de usuarios legítimos constantemente.
- **Dentro de la ventana se rechaza igual** (401: ese token ya está gastado) pero **no** se revoca la familia, así que el token nuevo que ya recibió la otra petición sigue valiendo. Fuera de la ventana sí es señal de robo.
- **Es la misma idea que el _leeway_ de Auth0.** El precio es que un ladrón que reutilice el token en los primeros segundos pasa desapercibido — a cambio de que el mecanismo se pueda tener encendido, que es lo que de verdad protege.

### `Features/Accounts/Service/RefreshTokenService.cs`

- **Por qué la rotación hace detectable un robo.** Que un token ya gastado vuelva a aparecer solo tiene dos explicaciones: hay dos copias circulando (robo) o el propio cliente lanzó dos refrescos a la vez. La rotación existe precisamente para que esto sea distinguible.
- **Fuera de la ventana de gracia se revoca la familia entera.** No se sabe cuál de las dos copias es la del dueño, así que cae la sesión completa. Es duro a propósito.
- **Cuenta desaparecida con la sesión abierta -> 401, no 404.** Para quien pregunta, su credencial ya no vale, y decir "ese usuario no existe" sería un oráculo de enumeración.
- **La comprobación de lockout en el refresh es el cinturón.** Renovar no vuelve a pedir credenciales, así que no pasa por el bloqueo de Identity: sin ella una cuenta bloqueada podría seguir renovando su sesión indefinidamente. Los tirantes son que bloquear revoque además las sesiones abiertas (`UserAdminService.LockAsync`); esta comprobación cubre también al usuario que se bloquea solo por fallar el login.
- **Gastar el viejo y emitir el nuevo, atómico.** Si solo se confirmara lo primero, el usuario se quedaría sin sesión por un fallo nuestro.
- **El `UPDATE` condicional es quien arbitra las carreras**: si dos peticiones simultáneas llegan con el mismo token, solo una lo gasta; la otra recibe `false`.
- **Logout: primero la garantía, después la optimización.** Sin familia viva la sesión no se puede extender; matar el `jti` en la denylist solo adelanta la muerte del access token que el cliente ya tiene, y si falla la sesión sigue cortada igual.
- **32 bytes de un generador criptográfico, no un GUID.** Los bits de un GUID no son todos aleatorios y su propósito es ser único, no impredecible.
- **SHA-256 a secas y no un hash de contraseña con sal.** El valor ya son 256 bits aleatorios: no hay nada que adivinar por fuerza bruta y un algoritmo lento solo añadiría latencia a cada refresh. La sal tampoco aporta: no hay dos usuarios que puedan tener "el mismo token".

### `Features/Accounts/Service/AuthService.cs`

- **Aquí no se hashea nada a mano.** `UserManager.CreateAsync(user, password)` aplica PBKDF2 con salt por usuario y el conteo de iteraciones vigente; cualquier `SHA256(password)` casero de tutorial es una vulnerabilidad.
- **Mapa mental desde Spring Security** (nota de aprendizaje del autor): `UserManager` ≈ `UserDetailsService` + `PasswordEncoder`, `SignInManager` ≈ `AuthenticationManager`, `RoleManager` ≈ la gestión de `GrantedAuthority`.
- **Username/email repetido -> 409 y no 400**: el request es válido en sí mismo, choca con el estado de la base.
- **Política de contraseñas de Identity -> 422 y campo a campo**: la forma del DTO ya la filtró DataAnnotations.
- **Se distingue "la actual no es correcta" de "la nueva no cumple la política"** al cambiar contraseña: son dos errores muy distintos para quien está delante, y devolver siempre lo mismo obliga a adivinar qué casilla corregir. No es un oráculo, porque para llegar ahí ya hay que estar autenticado como ese usuario.
- **Proyección a mano en vez de AutoMapper** para `UserDto`: los roles no son una propiedad de `ApplicationUser` sino una consulta aparte, así que un Profile tendría que inyectar el `UserManager` para resolverlos.
- **`ToFieldErrors` agrupa los `IdentityError` por código** en el mismo formato `{ campo: [mensajes] }` que produce `ValidationProblem(ModelState)`, para que el cliente reciba siempre la misma forma de error.

### `Features/Accounts/Service/UserAdminService.cs`

- **`OrderBy` explícito en el listado paginado**: sin él SQL Server no garantiza el mismo orden entre páginas y un usuario puede aparecer dos veces o ninguna al pasar de página.
- **El N+1 de roles está asumido**: una consulta por usuario, aceptable porque la página está acotada a 100 y esto es un panel de administración, no un endpoint caliente. Si algún día molesta, la salida es un JOIN contra `UserRoles`, no subir el `pageSize`.
- **Asignar rol es idempotente**: pedir un rol que ya se tiene no es un error. Devolver 409 obligaría al cliente a consultar antes de cada asignación.
- **Auditoría de concesión/revocación de roles con `LogWarning`**: una promoción a administrador es el cambio de permisos más grande que admite el sistema, y sin rastro no hay forma de responder "¿quién le dio admin a este?" tres meses después.
- **Regla 1 al quitar admin: uno no se lo quita a sí mismo.** Es el clic con el que un admin se deja fuera de su propio panel, y el camino más rápido a dejar el sistema sin nadie que pueda arreglarlo.
- **Regla 2: nunca sin administradores.** Se comprueba *después* de la regla 1 para que el mensaje devuelto sea el que de verdad explica el rechazo.
- **`DateTimeOffset.MaxValue` = bloqueo indefinido.** Identity no tiene "bloqueado para siempre": tiene una fecha de fin, y el infinito se expresa así.
- **Bloquear revoca además las sesiones.** Sin eso la cuenta queda marcada como bloqueada y el usuario sigue dentro: su access token vale hasta que expire y —lo grave— podría seguir renovándolo indefinidamente, porque renovar no vuelve a pedir credenciales y por tanto no pasa por el bloqueo.
- **Desbloquear limpia también los intentos fallidos acumulados**: si no, la cuenta recién desbloqueada se volvería a bloquear al primer error de contraseña.

### `Features/Accounts/Controllers/AuthController.cs`

- **Rate limit `auth`: 10 intentos por minuto y por IP.** Complementa al lockout de Identity, que solo cuenta fallos por usuario.
- **El rol no se acepta como campo del body en el registro.** En el código de referencia del curso el cliente podía mandar `"Role": "Admin"` en un endpoint anónimo y auto-promoverse. Los administradores se crean sembrando o promoviendo desde un endpoint protegido.
- **Registrarse deja la sesión abierta, igual que el login**: si no, el cliente recibiría un access token que no puede renovar y en 15 minutos estaría fuera.
- **`/refresh` no recibe DTO**: el refresh token se lee de la cookie `HttpOnly`, que es justo lo que impide que un XSS se lo lleve.
- **`/logout` es `[AllowAnonymous]` a propósito**: el caso más habitual de cerrar sesión es que el access token ya haya expirado. Exigir uno válido dejaría al usuario sin poder cerrar justo cuando más falta le hace.
- **`/logout` devuelve 204 siempre**, incluso sin cookie o con una ya revocada: cerrar sesión dos veces tiene que ser inofensivo, y un error dejaría al usuario sin saber si salió.
- **Cambiar la contraseña revoca todas las sesiones.** Es la expectativa de cualquiera que la cambia porque sospecha que se la han robado: si las sesiones abiertas siguieran vivas, cambiarla no habría echado a nadie. Se abre una sesión nueva para *este* dispositivo, para no expulsar también a quien acaba de hacerlo bien.
- **`logout-all` solo puede matar en el acto el access token de este dispositivo**: la denylist va por `jti` y no sabemos los de los demás. Sobreviven como mucho lo que les quede de vida, sin poder renovarse.
- **`ClientIp()` es solo para investigar incidentes**, no se usa para decidir nada.

### `Features/Accounts/ConfigureJwtBearerOptions.cs`

- **Antes era un lambda dentro de `AddJwtBearer(...)`** que volvía a leer la sección con `configuration.GetSection("Jwt").Get<JwtOptions>()!`. Funcionaba, pero por coincidencia: el `!` solo era seguro porque `ValidateOnStart` aborta el arranque antes de que ese lambda llegue a ejecutarse. Bindear dos veces la misma sección es además tener dos fuentes de verdad, una validada y otra no.
- **Con `IConfigureNamedOptions<JwtBearerOptions>`** la configuración entra por el constructor, tipada y validada, igual que en cualquier otro servicio.
- **Las cuatro validaciones activas.** El código de referencia del curso apagaba `ValidateIssuer` y `ValidateAudience` porque su token no emitía `iss`/`aud`: eso es parchear el síntoma. Un token firmado con la misma clave por cualquier otro servicio sería aceptado aquí.
- **`ClockSkew` a 30 s.** El default son 5 minutos de gracia: un token expirado seguiría valiendo 5 minutos más.
- **Firma buena y sin expirar no significa "sigue valiendo"**: un logout puede haberlo invalidado antes de tiempo. Es el precio de que un JWT sea autocontenido — no hay estado del lado del servidor a menos que se añada en `OnTokenValidated`.
- **`context.Fail` y no una excepción**: el pipeline lo convierte en un 401 limpio, que es lo que el cliente debe ver.

### `Features/Accounts/Service/JwtTokenService.cs`

- **Singleton**: no toca la base de datos ni guarda estado por request, y la clave de firma se materializa una sola vez en el constructor en vez de en cada login.
- **`IOptions<JwtOptions>` y no `IConfiguration`**: la configuración ya viene validada (`ValidateOnStart`) y tipada, así que no hay ni un `configuration["Jwt:SecretKey"]` que pueda ser `null`.
- **`sub` es el id inmutable**; el username puede cambiar y no sirve como clave.
- **`jti` permite revocar un token concreto** (denylist en Redis) sin invalidar todos los del usuario.
- **Claims duplicados en el formato "clásico" de .NET** (`ClaimTypes.NameIdentifier`, `ClaimTypes.Name`, `ClaimTypes.Role`) porque es el que leen `[Authorize(Roles = ...)]` y `User.Identity.Name` sin más configuración.
- **`notBefore`/`expires` van siempre en UTC**: la RFC 7519 define `exp` como epoch UTC. Es la única excepción al `DateTime.Now` local del resto del proyecto.

### `Features/Accounts/Service/IRefreshTokenService.cs`

- **El access token es corto y no se puede revocar por sí solo**; lo que se revoca es la *sesión*. La rotación —cada uso gasta el token y entrega otro— convierte un robo en algo detectable, porque ladrón y dueño acaban usando el mismo token gastado.
- **El valor en claro se devuelve una sola vez**, para que quien llama lo ponga en la cookie: en la base solo queda su huella y no hay forma de recuperarlo.
- **`LogoutAsync` no lanza si el token no existe o ya estaba revocado.** Un logout que devuelve error deja al usuario sin saber si está dentro o fuera.
- **`RevokeAllSessionsAsync` no puede invalidar los access tokens ya emitidos**: la denylist va por `jti` y no sabemos cuáles son los del usuario. Sobreviven como mucho lo que dure un access token (15 min), y en ese rato el usuario ya no puede renovar. Es consecuencia de que un JWT sea autocontenido, no un descuido.

### `Features/Accounts/Service/IUserAdminService.cs`

- **Se apoya en `UserManager` y no en `BaseRepository<T>` / `CrudService<>`**: `ApplicationUser` no implementa `IEntity` —su clave es un `string`— y su ciclo de vida pertenece a Identity, que es quien sabe de hashes, sellos de seguridad y bloqueos. Forzarlo dentro del CRUD genérico sería meter una entidad en una abstracción que no le sirve.
- **Las reglas de este servicio no son validaciones de formato**: son las que impiden que un clic deje el sistema sin nadie que pueda administrarlo.
- **Se comprueba que el rol exista antes de asignarlo.** `UserManager.AddToRoleAsync` con un rol desconocido falla, pero un `RoleManager` mal usado lo crearía al vuelo: acabaríamos con roles fantasma que no protegen nada porque ningún `[Authorize]` los nombra.

### `Features/Accounts/Service/IAuthService.cs`

- **No hereda de `ICrudService`**: un usuario no es un recurso CRUD más — no se "crea", se registra; no se "actualiza", cambia de contraseña o de rol.
- **El registro devuelve ya el token** para evitar el viaje extra de registrarse y volver a hacer login.
- **`ChangePasswordAsync` exige la contraseña actual aunque el usuario ya esté autenticado**: un access token demuestra que alguien entró hace un rato, no que quien está delante ahora sea el dueño.

### `Features/Accounts/Service/IJwtTokenService.cs`

- **Aislado en su propia interfaz** para que `AuthService` no sepa nada de `System.IdentityModel.Tokens.Jwt`: el día que el token pase a ser opaco/de referencia, cambia esta implementación y nada más.

### `Features/Accounts/Repository/IRefreshTokenRepository.cs`

- **No hereda de `IBaseRepository<T>` a propósito**: de las cinco operaciones CRUD aquí no se usa ninguna tal cual. Un refresh token no se "actualiza" ni se "borra": se **gasta**, y esa operación tiene que ser atómica. Heredar el CRUD solo traería cinco métodos que nadie debe llamar.
- **`TryConsumeAsync` es un `UPDATE … WHERE RevokedAt IS NULL` en una sola sentencia**, no un leer-y-luego-escribir. Entre comprobar "está vivo" y marcarlo cabe otra petición, y entonces las dos rotarían el mismo token y habría dos sesiones válidas donde debía haber una. Es la misma razón por la que el stock se descuenta con un UPDATE condicional y no comprobando antes.
- **Devolver `false` significa "llegaste tarde"**, y es justo la señal que dispara la detección de reuso.
- **`Add` no hace `SaveChanges`**, como `IEventOutbox`: quien confirma es la transacción de negocio, para que revocar el viejo y emitir el nuevo sean una sola cosa.
- **`RevokeAllForUserAsync` es lo que hace que bloquear una cuenta signifique algo**: sin ello el usuario sigue dentro y puede seguir renovando indefinidamente.

### `Features/Accounts/Repository/RefreshTokenRepository.cs`

- **El instante se captura fuera del árbol de expresión**: dentro, `DateTime.Now` se traduce a `GETDATE()`, o sea el reloj del servidor SQL, y el resto del proyecto estampa con el del proceso.
- **La condición `RevokedAt == null` va dentro del `UPDATE`**: es lo que hace que dos peticiones simultáneas con el mismo token no puedan gastarlo las dos.
- **Los borrados van en tandas acotadas**, como el resto de purgas: un DELETE de toda la tabla escala el bloqueo y se lleva por delante a quien esté autenticándose.

### `Features/Accounts/RefreshTokenCleaner.cs`

- **La tabla solo crece**: cada login abre una familia y cada refresco añade un eslabón. Un usuario activo genera decenas al día, y ninguno sirve para nada pasada su fecha —ni siquiera los revocados, porque la comprobación de reuso solo mira dentro de la ventana de gracia—.
- **Vive en el slice, no en `OutboxCleaner`.** Es tentador meter todas las purgas en el recolector que ya existe, pero ese vive en `Shared/Messaging` y nombrar ahí una entidad de `Accounts` invierte la dirección de dependencias que fija `rules.md` §4: lo transversal no conoce los slices.
- **Se registra siempre**, como el resto de purgas: la tabla crece haya o no actividad, y una limpieza que solo corre en algunos entornos es una que nadie recuerda que existe hasta que la tabla estorba.
- **Dos minutos de espera antes de la primera pasada**: al arrancar hay cosas más urgentes (migraciones, seeding).
- **El `catch (Exception)` no lleva filtro que excluya `OperationCanceledException`.** Una OCE que no venga del `stoppingToken` (la cancelación de un `SqlCommand`) se escaparía y, con `BackgroundServiceExceptionBehavior.StopHost` por defecto desde .NET 6, tumbaría la API entera por una tarea de limpieza.
- **El `cutoff` deja un día de margen sobre la caducidad**: dentro de la ventana de gracia todavía se consulta un token recién gastado para distinguir una carrera del cliente de un robo. Borrarlo antes convertiría esa distinción en "no existe" y perdería la señal.

### `Features/Accounts/AccountsExtensions.cs`

- **Equivale al `SecurityFilterChain` + `UserDetailsService` de Spring Security** (nota de aprendizaje del autor).
- **`ApplicationUser` no implementa `IEntity`** y por eso este slice no siguió el patrón `Repository/` genérico del resto.
- **`ValidateOnStart`** hace que un secreto ausente o corto reviente el arranque, no el primer login. Equivale a `@ConfigurationProperties` + `@Validated`.
- **`AddIdentityCore` y no `AddIdentity`**: esta es una API stateless con Bearer token. `AddIdentity` registraría además los esquemas de cookie de Identity, que nadie usa y que se pelean con el esquema JWT por ser el "default".
- **La política de contraseñas se escribe explícita** aunque los defaults de Identity sean razonables: dejarlos implícitos hace que nadie sepa cuál es la política real.
- **`AddSignInManager()`** es necesario para `CheckPasswordSignInAsync` + lockout.
- **Sin `options.Lockout...`**, `CheckPasswordSignInAsync(lockoutOnFailure: true)` cuenta los fallos pero nunca bloquea.
- **`IJwtTokenService` Singleton**, con la contrapartida asumida de que rotar `Jwt:SecretKey` exige reiniciar el proceso. El día que haga falta rotación en caliente, se cambia `IOptions` por `IOptionsMonitor`.
- **`RefreshTokenCookie` es Scoped por uniformidad**, no por necesidad: es un adaptador sin estado y Singleton bastaría, pero no se instancia en caliente.

### `Features/Accounts/JwtOptions.cs`

- **Es el equivalente tipado de `@ConfigurationProperties` de Spring Boot** (nota de aprendizaje del autor).
- **`ValidateDataAnnotations().ValidateOnStart()` es deliberado**: un secreto vacío no rompe en el arranque sino en el primer login, en producción, de noche.
- **`SecretKey` mínimo 32 caracteres** porque HS256 exige una clave de al menos 256 bits; con menos, .NET lanza en runtime.

### `Features/Accounts/Models/ApplicationUser.cs`

- **Hereda de `IdentityUser`**, que aporta gratis hash de contraseña (PBKDF2 + salt), normalización de email/username, bloqueo por intentos fallidos, tokens de confirmación y roles.
- **No implementa `IEntity` a propósito.** Su clave es un `string` (GUID), no un `int`, así que no encaja en `BaseRepository<T>` ni en `CrudService<...>` — y no debe encajar: el ciclo de vida de un usuario (registro, login, roles, bloqueo) lo gobierna `UserManager`. Forzarlo dentro del genérico sería el clásico error de meter una entidad en una abstracción que no le sirve.
- **Equivale a la entidad `User` + `UserDetailsService` de Spring Security** (nota de aprendizaje del autor).
- **`CreatedAt` se añade a mano** porque Identity no trae auditoría.

### `Features/Accounts/Models/RefreshToken.cs`

- **Vive en la base de datos y no en una cache** porque es la garantía de que una sesión se puede revocar: revocarla y emitir el siguiente ocurren en la misma transacción. La denylist de `jti` en Redis es otra cosa —una optimización para que el access token que el cliente ya tiene muera en el acto— y puede faltar sin que la sesión deje de cortarse.
- **`TokenHash` sigue la misma lógica que una contraseña**: si la base se filtra, lo que hay dentro no sirve para autenticarse. Como el índice es único, además hace la búsqueda barata sin descifrar nada. Con `MaxLength(64)` y no `nvarchar(max)`, que no es indexable en SQL Server.
- **`FamilyId` permite revocar de golpe toda la descendencia** al detectar un reuso. La alternativa —seguir el rastro de `ReplacedBy` token a token— exige recorrer la cadena entera con una consulta por eslabón, y basta con que falte uno para dejar media sesión viva.
- **Una familia por sesión**: cerrar sesión en el móvil no cierra la del portátil, que es lo que uno espera.
- **`CreatedByIp` no se usa para decidir nada**: detrás de un proxy o de una red móvil la IP cambia sola, y atar la sesión a ella corta a usuarios legítimos constantemente.

### `Features/Accounts/Controllers/UserController.cs`

- **Existe porque hasta entonces el único camino para tener un administrador era el `DataSeeder`**: no había forma de promover a nadie, ni de bloquear una cuenta, sin tocar la base a mano.
- **El `[Authorize(Roles = Admin)]` va en la clase** porque aquí sí es el mismo para todas las acciones. Ojo con la semántica: varios `[Authorize]` se combinan (AND), así que una acción no podría relajarlo — solo `[AllowAnonymous]` gana, y aquí no lo lleva ninguna.

### `Features/Accounts/Dtos/UserDto.cs`

- **Nunca lleva contraseña ni hash**, ni siquiera como campo opcional: un DTO que puede transportar una credencial acaba transportándola.

### `Features/Accounts/Dtos/ChangePasswordDto.cs`

- **`CurrentPassword` se exige siempre**, aunque el usuario ya esté autenticado: sin ella, quien se siente un minuto delante de una sesión abierta puede cambiar la contraseña y quedarse con la cuenta. Un access token demuestra que *alguien* entró hace un rato, no que quien está ahora sea el dueño.

### `Features/Accounts/Dtos/RegisterUserDto.cs`

- **La política de fuerza real (mayúscula, dígito, longitud) la aplica Identity** en `AddIdentity(options.Password...)`. Las DataAnnotations del DTO solo cortan lo obviamente inválido antes de llegar al servicio.

### `Features/Accounts/Dtos/AuthResponseDto.cs`

- **`ExpiresAt` va en hora local**, coherente con el `DateTime.Now` del resto del proyecto (el `exp` interno del JWT sí es UTC).

---

## Features/Ordering

### `Features/Ordering/Models/Order.cs`

- **`Ordering` es un contexto acotado propio y no una entidad más de `Catalog`.** Tiene su propio lenguaje —orden, línea, comprobante, envío— y sus propias invariantes. Que hoy solo compre productos del catálogo no lo convierte en parte de él.
- **Los datos del producto y del cliente se copian, no se referencian.** Precio, nombre y SKU quedan congelados en el momento de comprar: si mañana sube el precio o se renombra el producto, el comprobante de ayer tiene que seguir diciendo lo que se cobró. Un documento que cambia cuando cambia el catálogo no sirve como comprobante de nada.
- **`ReceiptStatus` existe porque la generación del PDF es asíncrona.** Entre que la orden se crea y el documento está disponible pasa un rato. Sin el campo, "no hay comprobante" y "el comprobante falló" serían indistinguibles para el cliente.
- **`Number` es columna real con índice único, no una propiedad calculada.** La gente busca por ese número, y calcularlo en memoria obligaría a traer la tabla entera para encontrar uno.
- **Los totales se guardan calculados y no se recalculan al leer.** El desglose es parte del documento y tiene que poder reproducirse aunque cambien los impuestos o las tarifas de envío.
- **Los datos del cliente también van congelados.** Si el usuario cambia su email mañana, el comprobante emitido hoy debe seguir mostrando aquel al que se le envió.
- **`ReceiptDocumentKey` guarda una clave opaca, nunca una ruta de disco.** Guardar `/app/documents/2026/09/x.pdf` ataría la base a la infraestructura de hoy: el día que los comprobantes vivan en S3 habría que reescribir todas las filas. La clave solo la entiende `IDocumentStore`.
- **`OrderItem.ProductId` no lleva clave foránea a propósito.** Si un producto se borra, la orden y su comprobante tienen que sobrevivir. Una FK obligaría a elegir entre impedir el borrado o borrar la historia de compras, y las dos son peores.
- **`LineTotal` es redundante a propósito: es lo que se imprimió.** Recalcularlo al leer parece más limpio hasta el día que cambia la forma de redondear y todos los comprobantes antiguos empiezan a cuadrar mal por un céntimo.


### `Features/Ordering/Dtos/OrderDtos.cs`

- **`PlaceOrderDto` no lleva precios ni totales.** Los pone el servidor a partir del catálogo: un precio que viaja en el cuerpo es un precio que el cliente elige. El email tampoco viaja: sale del claim del token.
- **`CustomerName` SÍ lo elige el cliente, y es una decisión, no un descuido.** Se imprime tal cual en el comprobante, así que cualquiera puede emitirse uno a nombre de otra persona. No hay impacto cruzado —solo lo descarga quien compró— pero significa que **este documento no vale como prueba de identidad de nadie**: es un recibo de compra, no una factura. Es un dato de envío («¿a nombre de quién va el paquete?»), legítimamente del comprador. El día que esto emita facturas fiscales, el nombre tiene que salir del perfil verificado del usuario y no del cuerpo.
- **`OrderDto` expone el ESTADO del comprobante y no la clave del documento.** La clave es un detalle del almacén: publicarla ataría el contrato de la API a la infraestructura de hoy, y el día que sea S3 el cliente estaría leyendo una clave que ya no significa nada. El cliente mira el estado para saber si ya puede descargar.


### `Features/Ordering/Events/OrderPlaced.cs`

- **Se emite por el outbox y no llamando al generador.** Escribir el evento es parte de la transacción de la compra, así que o hay orden y evento, o no hay ninguno de los dos. Llamar al generador aquí ataría la compra a que el generador esté vivo — y generar un PDF tarda lo suyo.
- **Lleva solo el id y el número, no la orden entera.** Excepción consciente a la regla de "un evento debe traer lo que el consumidor necesita": el consumidor vive en el mismo proceso y necesita el *estado confirmado* de la orden con sus líneas. Meter el detalle en el mensaje duplicaría la fuente de verdad —el documento se generaría a partir de una copia que puede haber quedado obsoleta— y engordaría el payload sin ganar nada. El día que el generador sea otro servicio, lo que hay que añadir es el detalle, no cambiar el mecanismo.


### `Features/Ordering/Service/IOrderService.cs`

- **`PlaceAsync` no genera el comprobante.** Emite `OrderPlaced` por el outbox y devuelve; el PDF lo hace un consumidor aparte. Generarlo aquí ataría la compra a que el generador esté vivo y a que tarde poco, y ninguna de las dos cosas es cierta.
- **`intent` no tiene valor por defecto, igual que en `ProductService.BuyAsync`.** La garantía de no comprar dos veces es una invariante de la operación, no algo que dependa de que el controller lleve un atributo.
- **`GetReceiptAsync` devuelve el contenido y no la clave.** La clave es un detalle del almacén; sacarla del servicio ataría el contrato al proveedor de hoy. Quien llama recibe un `Stream` que debe liberar — de eso se encarga MVC al escribir la respuesta.
- **La comprobación de propiedad va en el servicio y no en el controller.** Es una regla de negocio («un comprobante es de quien compró»): dejarla arriba significaría que el día que lo llame un job o un endpoint nuevo se pierde en silencio. Es la misma razón por la que la idempotencia bajó al servicio.


### `Features/Ordering/Service/OrderService.cs`

- **La idempotencia se resuelve DENTRO de la transacción.** La marca y el efecto se confirman juntos o no se confirma ninguno; es la garantía de idempotencia del proyecto, reutilizada tal cual.
- **Se guarda antes de emitir el evento porque el evento necesita el `Id` que asigna la base.** Sigue siendo atómico: los dos `SaveChanges` van dentro de la misma transacción del runner.
- **En el `catch` de intento duplicado, otra réplica ganó la carrera.** Nuestra transacción entera se deshizo —stock incluido—, así que basta con devolver lo que hizo el ganador.
- **404 y no 403 para la orden de otro.** Decir "existe pero no es tuya" ya filtra que existe, y con ids correlativos eso permite contar las órdenes de la tienda desde fuera.
- **Dos códigos distintos para el comprobante ausente, y esa es toda la razón de que `ReceiptStatus` sea columna y no un booleano derivado.** `receipt_not_ready` significa «vuelve en un momento» y un cliente lo reintenta; devolverlo para un comprobante que murió en la DLQ lo deja haciendo polling eterno sobre algo que no va a existir. Por eso existe `receipt_failed`.
- **Los dos son 409 y no 404 porque la ORDEN existe.** Y se usa `CustomAppException` y no `ConflictAppException` porque el `code` es parte del contrato —es por lo que el cliente los distingue— y `ConflictAppException` fija el suyo en `"conflict"`.
- **El almacén devuelve `null` —no lanza— cuando la clave ya no está.** Es una condición tratable (un borrado, una migración de infraestructura a medias), no un fallo del sistema. Ahí sí es un 404: la orden existe pero su documento se ha perdido, y decirlo es más honesto que un 500.
- **El nombre de la descarga lo pone el dominio, no el almacén.** Visto ejecutando: el fichero llegaba como `cdfdcf87c326aadb22845f2f46c8c691.pdf` —la clave opaca— y con eso en la carpeta de descargas nadie sabe de qué compra era. Peor: filtra la forma de las claves.
- **Se agrupan las líneas repetidas ANTES de apartar stock.** Sin esto, un carrito con el mismo SKU dos veces produce dos líneas idénticas en el comprobante y dos descuentos separados: cuadra en total, pero el documento queda raro y el cliente llama preguntando.
- **Las líneas se ORDENAN por SKU, y no es cosmética.** Cada descuento toma un lock exclusivo de la fila del producto y lo mantiene hasta el commit, que aquí está lejos (secuencia, INSERT de la orden, outbox, marca del comando). Recorrerlas en el orden que mandó el cliente es pedir un deadlock: A compra `[1,2]` y B compra `[2,1]` a la vez, cada uno bloquea el primero y espera el del otro. Con un orden total y global de adquisición, el deadlock deja de ser posible por construcción. Se sobreviviría —el error 1205 es transitorio y EF reintenta— pero rehaciendo la compra entera, y con contención alta se agotan los reintentos y sale un 500.
- **Un solo mensaje para "no existe" y "no hay bastante".** Distinguirlos convierte el checkout en un inventario consultable desde fuera.
- **Descuento, impuestos y envío quedan a cero.** No hay reglas de negocio que los calculen todavía; están en el modelo y en el comprobante porque el desglose es parte del documento, y añadirlos después obligaría a migrar datos.


### `Features/Ordering/Service/IReceiptRenderer.cs`

- **Es un puerto para que la librería de PDF sea sustituible.** Ni el consumidor que lo dispara, ni el controller que lo sirve, ni la orden saben con qué se dibuja: cambiar de QuestPDF a otra cosa —o generar HTML, o una factura electrónica firmada— es escribir otra implementación y cambiar una línea de registro.
- **Devuelve `Stream` y no `byte[]`.** Quien lo recibe lo copia directamente al almacén sin materializar el documento entero en memoria, y esa decisión deja de ser gratis en cuanto se generen miles.
- **Todo lo que se imprime sale de la orden** —precios, nombres, datos del cliente—, que es lo que hace que el documento sea reproducible aunque el catálogo haya cambiado desde entonces.


### `Features/Ordering/Documents/IOrphanReceiptCollector.cs`

- **El efecto está fuera del `BackgroundService` porque lo que vive dentro de uno no se puede probar.** Aquí importa más que en ningún otro sitio porque este componente **borra ficheros**: los tests que de verdad hacen falta son «no borra el referenciado» y «no borra el recién escrito», y ninguno se puede escribir contra un job con un temporizador de horas dentro.
- **Existe porque el comprobante se escribe dentro de la transacción del inbox y un fichero no se deshace con ella.** Si el commit falla, queda un PDF que nadie apunta. Se aceptó a sabiendas y en esa dirección —un huérfano es basura recolectable, un comprobante perdido es un cliente sin su documento—, pero alguien tiene que recogerla.


### `Features/Ordering/Documents/OrphanReceiptCollector.cs`

- **Vive en `Ordering` y no en `Shared/Documents`, aunque hable de un almacén transversal.** La pregunta «¿quién referencia esta clave?» solo la sabe responder quien tiene la tabla, y `Shared/` no puede nombrar tipos de `Features/` (`rules.md` §4). Hoy `Ordering` es el único que escribe documentos; el día que haya un segundo, esto se invierte con un puerto `IDocumentReferences` y sube a `Shared`.
- **`BatchSize` es un lote acotado a propósito.** Ni una consulta por fichero —una tormenta contra la base— ni el almacén entero en una sola consulta —un `IN` de cien mil elementos que SQL Server no traga—.
- **El periodo de gracia es la única línea que no se puede equivocar.** El fichero existe ANTES que la fila que lo apunta —se escribe dentro de la transacción—, así que sin ese corte se borrarían comprobantes buenos a mitad de vuelo. Y un comprobante borrado no vuelve. (Sobrevive en el código, en una línea.)
- **Ante la duda, no se borra.** Si la consulta de referencias falla se salta el lote entero: borrar de más pierde el documento de un cliente, borrar de menos deja basura una vuelta más. La asimetría del coste decide sola.


### `Features/Ordering/Documents/ReceiptCleaner.cs`

- **Esta clase solo aporta el reloj y la disciplina de no tumbar el proceso.** Todo lo que decide qué se borra vive en `IOrphanReceiptCollector`, fuera de aquí, para que se pueda probar sin esperar horas.
- **Un respiro de 5 minutos antes de la primera pasada.** El arranque ya tiene bastante —migraciones, seeding, la primera conexión al broker— sin que además alguien recorra el disco.
- **Scope propio por pasada.** El job es un singleton y el recolector es Scoped (depende del repositorio, que depende del `DbContext`).
- **El `catch` general NO lleva filtro que excluya `OperationCanceledException`.** Una OCE que no venga del `stoppingToken` escaparía de `ExecuteAsync`, y desde .NET 6 el default es `BackgroundServiceExceptionBehavior.StopHost` — un fallo recogiendo basura tumbaría la API entera.


### `Features/Ordering/Documents/QuestPdfReceiptRenderer.cs`

- **Por qué QuestPDF, comprobado antes de meterlo (2026-09-06):** licencia Community gratuita —también para uso comercial— con ingresos brutos anuales por debajo de 1.000.000 USD y 90 días de transición si se superan; API de composición en C# y no HTML→PDF (sin navegador headless, sin proceso externo colgado, maquetado comprobado en compilación); pensado para alta transaccionalidad, sin estado compartido entre generaciones.
- **La licencia se miró primero por lo que pasó con AutoMapper 15**, que empezó a exigir licencia comercial con el proyecto ya montado. Es un umbral, no un "gratis para siempre": está anotado como decisión del owner el día que aplique.
- **Trampa 1: `QuestPDF.Settings.License` lanza al GENERAR, no al arrancar.** Sin cuidarlo, la API arrancaría sana y los comprobantes fallarían uno a uno dentro del consumidor. Se declara en el arranque (`AddReceiptRendering`).
- **Trampa 2: en Linux dibuja con SkiaSharp, que necesita `libfontconfig1` y alguna fuente instalada.** La imagen `mcr.microsoft.com/dotnet/aspnet` no las trae: sin añadirlas al `Dockerfile` esto revienta solo dentro del contenedor —en local funciona—, que es la peor forma de descubrirlo. Ya están puestas ahí.
- **Cultura invariante y no la del servidor.** Con la cultura ambiente, el mismo comprobante saldría con coma o con punto decimal según la máquina que lo generara, y dos réplicas producirían documentos distintos para la misma orden.
- **Sin `FontFamily` explícita.** QuestPDF embebe su fuente por defecto (Lato) en el paquete, así que el documento sale idéntico en local y dentro de la imagen. Pedir Calibri —que no existe en Linux— deja el resultado a merced de la sustitución de fuentes de cada máquina: dos réplicas, dos comprobantes distintos, y ningún error que lo avise.
- **El pie va en todas las páginas.** Un comprobante de varias hojas sin número de página es imposible de comprobar que está completo.
- **`stream.Position = 0` antes de devolverlo.** Quien lo recibe lo copia al almacén, y un stream en la última posición guardaría un fichero de cero bytes sin ningún error.
- **`table.Header` y no una primera fila cualquiera.** QuestPDF la repite en cada página; con un pedido largo, las páginas siguientes serían columnas de números sin saber a qué corresponden.
- **Descuento, impuestos y envío solo se imprimen si existen.** Una fila "Descuento 0,00" en cada comprobante es ruido que además invita a preguntar por qué no hay descuento.


### `Features/Ordering/Messaging/IReceiptGenerator.cs`

- **Es el gemelo de `IProductPurchasedHandler` y existe por la misma lección:** lo que vive dentro de un `BackgroundService` atado a AMQP no se puede probar. Con el efecto fuera, el test que de verdad importa —el que falla a mitad y comprueba que el mensaje se reintenta— se escribe sin broker delante.
- **Deja al consumidor siendo lo que debe ser:** fontanería AMQP (ack, reintentos, DLQ) que no sabe qué significa el mensaje que transporta.
- **`HandleAsync` corre dentro de la transacción del inbox**, así que si lanza, la marca de «procesado» se deshace con él y el mensaje se reintenta.


### `Features/Ordering/Messaging/ReceiptGenerator.cs`

- **El estado se relee de la base y no se toma del mensaje.** El evento trae lo justo para identificar la orden a propósito: generar el documento a partir de una copia viajada sería tener dos fuentes de verdad para lo que se imprime.
- **Si la orden no existe no se lanza**, porque reintentar no la va a hacer aparecer. Solo puede pasar si alguien borró la orden a mano o si se está reproduciendo un evento antiguo desde la DLQ.
- **La comprobación de `ReceiptDocumentKey` es una segunda red bajo la del inbox, y no es redundante.** El inbox deduplica por `MessageId`, así que un replay manual desde la DLQ —que llega con otro id— volvería a generar el PDF y dejaría el anterior huérfano en el almacén. Aquí la pregunta es sobre el estado, no sobre el mensaje.
- **Se escribe un fichero dentro de una transacción de base de datos y eso se acepta a sabiendas, en ESTA dirección.** Un huérfano es basura recolectable (nadie lo apunta y no se alcanza sin su clave); un comprobante perdido es un cliente sin su documento. La alternativa —escribir después de confirmar— mueve el problema al otro lado y ahí sí se pierden documentos: morir entremedias deja la orden diciendo que su comprobante existe.


### `Features/Ordering/Messaging/OrderPlacedConsumer.cs`

- **Esta clase es la razón de que el PDF sea asíncrono.** La compra emite el evento dentro de su transacción y responde; el documento se dibuja aquí, en otro hilo y con su propio presupuesto de reintentos. Si el generador falla —o si nadie lo está ejecutando— la compra sigue siendo válida: lo único que pasa es que el comprobante tarda.
- **El nombre de la cola lo declara el slice, no la configuración compartida:** dice a qué reacciona `Ordering`, así que es suyo. `EventSubscription.For` le da una DLX propia — con la `fanout` heredada del catálogo, un comprobante que muriera aparecería también en la DLQ de las compras.
- **Sin `OnExhaustedAsync`, `ReceiptStatus.Failed` era inalcanzable** (lo destapó una revisión). Una orden cuyo PDF muriera en la DLQ se quedaba en `pending` para siempre, así que `GET /{id}/receipt` devolvía 409 `receipt_not_ready` indefinidamente; ese código significa «vuelve en un momento», o sea polling eterno sobre un documento que no va a existir.
- **Marcarlo desde el `catch` de cada intento habría sido mentir mientras aún quedan reintentos.** `OnExhaustedAsync` es el único punto en que «ya no habrá más» es cierto.


### `Features/Ordering/Ports/ICatalogGateway.cs`

- **Es un puerto de este slice, no una referencia a `Catalog`.** Así el servicio de órdenes habla de "apartar una unidad de este SKU" y no conoce ni `Product`, ni `IProductRepository`, ni cómo se descuenta el stock. Toda la dependencia hacia el otro contexto queda confinada a una sola clase —el adaptador— y se ve de un vistazo en el composition root.
- **Sin esto, un slice acaba importando tipos del otro por comodidad** y en seis meses no hay forma de mover ninguno de los dos: es exactamente la dispersión que el vertical slicing venía a evitar, solo que con carpetas bonitas.
- **Apartar y consultar son la misma operación a propósito.** Separarlas sería read-then-write: entre "¿hay stock?" y "descuéntalo" cabe otra compra, y se vendería dos veces la última unidad.


### `Features/Ordering/Ports/CatalogGateway.cs`

- **Si mañana el catálogo es otro servicio, lo que cambia es esta clase** —pasaría a hacer una llamada HTTP o a publicar un comando— y ni el servicio de órdenes ni el controller se enteran. Esa es toda la razón de que exista el puerto.
- **El descuento y la comprobación ocurren en la misma sentencia SQL.** Entre comprobar y descontar cabe otra compra, y se vendería dos veces la última unidad.
- **Se copia lo que la orden tiene que congelar.** A partir de ahí, que el producto cambie de precio o de nombre no altera esa compra.


### `Features/Ordering/Repository/IOrderRepository.cs`

- **No hereda de `IBaseRepository<T>`.** Una orden no se actualiza ni se borra —se coloca, y a partir de ahí solo cambia de estado—, así que de las cinco operaciones del CRUD genérico aquí no vale ninguna tal cual.
- **El número sale de una secuencia de SQL Server, no de `MAX(Number)+1`.** Eso último es un leer-y-escribir: dos compras simultáneas se llevarían el mismo número contra un índice único y una de las dos reventaría. La secuencia la sirve el motor, sin bloquear.
- **`Add` no hace `SaveChanges`:** manda la transacción de negocio.
- **El filtro por comprador va en la consulta y no en un `if` posterior.** Es la diferencia entre "no existe para ti" y "existe y te digo que no puedes verla"; lo segundo ya filtra que existe.
- **`FindReferencedKeysAsync` pregunta por lotes y no fichero a fichero.** El recolector recorre el almacén entero, y una consulta por documento convierte una tarea de fondo en una tormenta contra la base.


### `Features/Ordering/Repository/OrderRepository.cs`

- **`SELECT NEXT VALUE FOR` es atómico y no bloquea:** el motor reparte números sin que dos transacciones puedan llevarse el mismo. Un `MAX()+1` sí se los llevaría.
- **La secuencia NO se reinicia por año.** El año del número sale de la fecha, así que en 2027 los números seguirán subiendo (`ORD-2027-000431`). Reiniciarla obligaría a un trabajo anual que alguien olvidaría, y a que el índice único dejara de bastar.
- **Los números pueden tener huecos.** Una secuencia no se deshace con el rollback —es su forma de no bloquear—, así que una compra que falla, o un reintento de la estrategia de EF, se lleva un número que ya no usará nadie. Para un comprobante interno da igual; para una FACTURA no: varias legislaciones exigen numeración correlativa sin huecos, y eso pide una tabla de contadores por serie, bloqueada dentro de la misma transacción y pagando la serialización. El día que esto emita facturas de verdad, esa es la decisión.
- **`ThenByDescending(o => o.Id)` es un desempate estable.** Sin él, dos órdenes del mismo instante pueden salir dos veces o ninguna al paginar.
- **El instante se captura fuera del árbol de expresión.** Dentro, `DateTime.Now` se traduce a `GETDATE()` y lo evaluaría el reloj del servidor SQL.
- **`SetReceiptFailedAsync` solo actúa si la orden NO tiene comprobante.** El aviso de "agotado" llega desde el consumidor y podría cruzarse con una generación que sí funcionó (un replay desde la DLQ que termina bien mientras el mensaje original agota sus intentos): sin esa condición marcaría como fallido un comprobante que ya está en el almacén y descargándose.
- **Comparación `Ordinal` para las claves.** Las generó el almacén y se comparan byte a byte; una comparación sensible a la cultura aquí decidiría si se borra el fichero de un cliente.


### `Features/Ordering/Controllers/OrderController.cs`

- **Ninguna acción es pública: una orden es de alguien.** El `[Authorize]` de clase lleva el requisito débil (estar autenticado) porque varios `[Authorize]` se combinan (AND) y solo `[AllowAnonymous]` gana — poner aquí un rol lo exigiría también en las acciones.
- **`[RequestSizeLimit(64 KB)]` corta la petición antes de leerla entera.** Sin eso, el único tope es el de Kestrel (30 MB): un carrito de 300.000 líneas se enlaza y se aloja completo en memoria para acabar en un 400 por el `[MaxLength(50)]`. Validar después de haber recibido no protege.
- **`[Idempotent]` es un atajo, no la garantía.** Responde un reintento sin tocar la base; quien impide de verdad la doble compra es `OrderService`, que escribe la marca del comando en la misma transacción que la orden y el descuento de stock.
- **El controller traduce protocolo a dominio.** La cabecera del cliente se convierte aquí en una intención, y el servicio ya no sabe que existe HTTP.
- **201 CON cuerpo, a diferencia de `POST /category`.** El cliente necesita el número de orden y los totales para pintar la confirmación, y obligarle a un GET más después de pagar es la peor petición extra posible.
- **El PDF se sirve por la acción y no como estático, y hacen falta las tres barreras:** el fichero vive fuera de `wwwroot/` (no hay URL pública que adivinar), su clave es aleatoria (no se deduce de la orden ni del usuario) y la acción comprueba de quién es la orden. Con solo las dos primeras, cualquier filtración de una clave sería una descarga; con solo la tercera, `UseStaticFiles` lo serviría sin pasar por autenticación.
- **Devuelve `Stream` y no `byte[]`:** MVC lo copia a la respuesta a trozos y libera el fichero al terminar, así que una descarga no se lleva el documento entero a memoria.
- **409 y no 404 mientras se genera:** el comprobante existirá, solo que todavía no. Un 404 le diría al cliente que deje de pedirlo.
- **`Cache-Control: no-store…` porque el documento lleva nombre, dirección e importe.** Las caches compartidas ya quedan fuera por la cabecera `Authorization` (RFC 9111 §3.5), pero el disco del navegador no: sin esto el PDF se queda cacheado en un equipo que puede ser compartido, y sigue ahí después de cerrar sesión.
- **`enableRangeProcessing` no se activa.** Son unas decenas de KB, y las peticiones por rango obligarían al almacén a soportar lecturas parciales, que es justo la clase de detalle que un S3 y un disco resuelven distinto.


### `Features/Ordering/OrderingExtensions.cs`

- **Es un contexto acotado propio y no una entidad más de `Catalog`:** tiene su lenguaje —orden, línea, comprobante, envío— y sus invariantes; toda la dependencia hacia el catálogo cabe en una clase (`CatalogGateway`).
- **El almacén de documentos NO se registra aquí.** Es transversal (`Shared/Documents`), y quien decide si escribe en disco o en S3 es el despliegue, no este slice.
- **`IReceiptGenerator` se registra siempre, también sin broker.** Es lógica del slice y así se puede probar sin AMQP delante.
- **El recolector de huérfanos también se registra siempre.** El huérfano lo produce un commit fallido, no el transporte, así que una réplica sin mensajería acumula basura igual. Se apaga con `Documents:CleanupIntervalHours = 0`.
- **La licencia de QuestPDF se declara al arrancar o lanza al GENERAR.** Sin esto la API arrancaría sana y los comprobantes fallarían uno a uno dentro del consumidor: cinco reintentos y a la DLQ, cada uno. Es una propiedad estática del proceso, así que su sitio es el composition root, una sola vez.
- **Community es gratuita, también para uso comercial, por debajo de 1.000.000 USD de ingresos brutos anuales.** Es un umbral, no un «gratis para siempre»: superarlo exige licencia (con 90 días de transición) y es una decisión del owner. Se comprobó antes de meterlo por lo que pasó con AutoMapper 15, que empezó a exigir licencia comercial con el proyecto ya montado.
- **Singleton para el renderizador:** no guarda estado entre documentos y crear uno por comprobante no aporta nada.

---

## Arranque, datos, excepciones y tests

### `Program.cs`

- **La plantilla de consola de Serilog no es cosmética.** La de fábrica (`[{Timestamp} {Level}] {Message}{NewLine}{Exception}`) **no renderiza las propiedades del LogContext**: el `CorrelationId` y el `TraceId` se empujaban correctamente y no aparecían en ninguna línea, o sea que la única razón de existir del middleware no se cumplía. Fallo silencioso: el mecanismo funciona, el sink no lo enseña.
- **El sink se declara en código y NO también en `Serilog:WriteTo` de appsettings.** Declararlo en los dos sitios no sustituye: Serilog los suma y cada línea sale duplicada.
- **`ForwardedHeaders` es lo que hace correcto todo lo que viene después.** Detrás de un proxy, sin esto: el rate limiter particiona por la IP DEL PROXY (un solo cubo de 100 req/min para todo internet), los logs registran esa misma IP para todo el mundo, y `UseHttpsRedirection` no sabe si la petición original era HTTPS (avisa con "Failed to determine the https port" y es un no-op).
- **Vaciar `KnownNetworks`/`KnownProxies` acepta las cabeceras de CUALQUIER origen.** Solo es admisible mientras la API no sea alcanzable directamente desde fuera del proxy; si lo fuera, cualquiera podría falsear su IP y saltarse el rate limit. Declarar la red del proxy en cuanto se conozca.
- **`MigrateAsync` en el arranque.** Sin esto, la imagen de runtime (que no lleva SDK ni `dotnet-ef`) arranca contra una base vacía: `/health` responde 200, `/health/ready` responde Healthy —porque `AddDbContextCheck` solo comprueba que se puede CONECTAR, no el esquema— y todos los endpoints devuelven 500 "Invalid object name 'Categories'". Además `MigrateAsync` reintenta gracias a `EnableRetryOnFailure`, que es justo lo que hace falta cuando SQL Server todavía está arrancando.
- **`ClientAbortMiddleware` va justo por DEBAJO de `UseExceptionHandler`.** El middleware de diagnóstico del framework escribe su "unhandled exception" a nivel Error ANTES de llamar a ningún `IExceptionHandler`, así que decidirlo en `GlobalExceptionHandler` llega tarde: la línea de Error ya está escrita. Hay que interceptar antes de que le llegue.
- **`CorrelationIdMiddleware` justo tras resolver la IP real y antes de todo lo demás.** Ponerlo más abajo dejaría sin identificar los fallos tempranos, que son los peores de diagnosticar.
- **HSTS fuera de Development a propósito.** En local se sirve HTTP y una cabecera HSTS queda cacheada en el navegador para `localhost`, rompiendo cualquier otro proyecto que use ese host en claro — y cuesta de diagnosticar porque el fallo aparece en otra app. HSTS existe porque `UseHttpsRedirection` por sí solo no evita que la PRIMERA petición en claro viaje con la cookie o el token dentro.
- **`UseStaticFiles` es TERMINAL para los archivos que sirve.** Puesto más arriba, las imágenes no pasaban por el limitador (descarga en bucle sin cuota) ni recibían cabeceras CORS (un `<img>` no las necesita, pero un `fetch()` del front sí).
- **El `Predicate` del health check de readiness.** Hoy todos los checks llevan el tag `ready`, pero sin el predicado los tags eran decorativos y un check futuro sin tag entraría en readiness sin querer.
- **`/health` (liveness) no toca ninguna dependencia externa a propósito.** Si la sonda de vida depende de la base, una caída de la base provoca que el orquestador reinicie procesos que están perfectamente sanos.
- **`public partial class Program`.** La clase generada por las instrucciones de nivel superior nace `internal`, así que `WebApplicationFactory<Program>` no la ve y el proyecto de tests no compila. Declararla `public partial` es la forma oficial de abrirla sin tocar nada más.

### `Data/AppDbContext.cs`

- **Hereda de `IdentityDbContext<TUser>` y no de `DbContext`.** Eso añade las 7 tablas `AspNet*` al mismo contexto y a la misma transacción que el dominio.
- **Solo existe `ApplicationUser`.** El contexto de referencia del curso mantenía **dos** tablas de usuarios (una legacy `Users` y las de Identity) y declaraba un `DbSet<User> Users` que **ocultaba** el `Users` de `IdentityDbContext`. Resultado: las comprobaciones de unicidad consultaban una tabla vacía y siempre devolvían "libre".
- **`base.OnModelCreating` es obligatorio.** Es quien mapea las tablas de Identity y sus índices únicos. Omitirlo compila y luego falla en la migración.
- **`Attempts` va en el INCLUDE del índice `IX_OutboxMessages_Pending`.** El publicador filtra por `Attempts < MaxPublishAttempts`, que no estaba en el índice: el plan real hacía un key lookup a la tabla por CADA fila recorrida para evaluarlo. Y los mensajes agotados siguen con `ProcessedAt IS NULL` (el recolector no los borra, a propósito), así que se quedan en el índice PARA SIEMPRE, a la cabeza del escaneo, y cada vuelta —cada 5 s, eternamente— paga un lookup por cada uno antes de llegar a los frescos. Basta un despliegue con la routing key mal para llenarlo de zombis: medido, dos mensajes sin cola destino pasaron de 0 a 5 intentos en ~14 s.
- **El índice del publicador va por `Sequence` y no por `OccurredAt`.** `Sequence` es el orden real de drenaje; indexar `OccurredAt` ordenaba por un valor que ya no manda.
- **La precisión de los importes se fija explícitamente.** `decimal` sin precisión cae a `decimal(18,2)` por convención de EF, que aquí vale, pero dejarlo implícito hace que un cambio de convención mueva dinero sin que nadie lo note.
- **La auditoría automática equivale a `@EnableJpaAuditing` + `@CreatedDate`/`@LastModifiedDate` de Spring.** Antes cada repositorio asignaba `UpdatedAt` a mano (`ProductRepository.BuyProduct`); ahora es imposible olvidarlo.
- **`DateTime.Now` (hora local) es la decisión ya tomada en el proyecto**, documentada en `AGENTS/docs/02-repository.md`.

### `Data/DataSeeder.cs`

- **Se ejecuta desde `Program.cs` dentro de un scope propio.** El contenedor raíz no puede resolver servicios `Scoped` como `AppDbContext` o `UserManager`, y hacerlo lanza en el arranque.
- **Roles y usuarios se crean con `RoleManager`/`UserManager` y nunca con `INSERT` directo.** Un insert a mano se salta el `SecurityStamp` y el `ConcurrencyStamp`, y sin `SecurityStamp` el lockout y la invalidación de credenciales de Identity dejan de funcionar — un fallo que no se ve hasta que hace falta.
- **`SaveChanges` antes de crear los productos.** Es lo que asigna los `Id` reales; el seeder de referencia hacía `Categories.Find(1)` sobre categorías todavía no persistidas y solo funcionaba por accidente, si el IDENTITY empezaba en 1.

### `Exceptions/IdempotencyConflictAppException.cs`

- **422 y no 409.** La petición está bien formada y no choca con el estado de la base: no se puede procesar porque contradice a otra que el cliente mandó con la misma clave. Es lo que fija el borrador de idempotencia de la IETF (`draft-ietf-httpapi-idempotency-key-header`, un SHOULD desde la versión -07) y lo que hace Stripe con su `idempotency_error`. **La industria no está unificada**: Square devuelve 400 con `IDEMPOTENCY_KEY_REUSED`. Se elige el del estándar y se documenta.
- **Sin esta excepción, reutilizar una clave con otro cuerpo reproducía la respuesta de la primera en silencio**: el cliente pedía comprar 5 unidades, recibía un 200 con el resultado de haber comprado 1, y nada indicaba que su segunda petición no se había ejecutado. Un fallo silencioso en el mecanismo que existe precisamente para no cobrar de más.
- **Es una excepción de dominio y no un resultado del filtro HTTP a propósito.** La comprobación vive donde vive la garantía, dentro de la transacción de negocio, y por tanto tiene que poder expresarse sin conocer códigos de estado. Quien traduce sigue siendo `GlobalExceptionHandler`.

### `Exceptions/PreconditionFailedAppException.cs`

- **412 y no 409, aunque las dos hablen de conflicto.** El 409 dice "tu petición choca con el estado actual"; el 412 dice "la *precondición* que TÚ pusiste no se cumple". La diferencia es accionable: ante un 412 el cliente sabe que debe releer el recurso, quedarse con el `ETag` nuevo y decidir si su cambio sigue teniendo sentido — que es exactamente lo que hay que hacer ante un *lost update*.

### `Exceptions/ValidationAppException.cs`

- **422 para validación de negocio, no sustituye a las DataAnnotations del DTO** (esas producen un 400 vía `ValidationProblem(ModelState)`). Es para reglas que solo se pueden evaluar contra la base y que conviene devolver agrupadas por campo, con el mismo formato que `ProblemDetails.errors`.

### `tests/ApiEcommerce.Tests/Integration/ApiFactory.cs`

- **Por qué no Testcontainers**, que es lo que decía el plan: el entorno de trabajo es un dev container **sin Docker dentro**, así que no puede arrancar contenedores. Se usa la infraestructura ya levantada en el host, con base separada (`ApiEcommerceNET8_Tests`) y un prefijo propio de Redis. Testcontainers entra en la fase 6 (CI), donde el runner sí tiene Docker.
- **La base se borra al EMPEZAR y no al terminar**: si una corrida falla, el estado queda ahí para poder mirarlo. Las migraciones y el seeding los aplica el propio `Program` al arrancar el host, igual que en producción, así que el test también cubre ese camino.
- **`Env(...)` por variable de entorno con valor local por defecto.** En el dev container la infraestructura vive en `172.17.0.1` (gateway del bridge de Docker); en CI los `services` del runner escuchan en `localhost`. La contraseña por defecto es la del SQL Server local del autor, no un secreto de producción: sirve para que `dotnet test` funcione recién clonado. CI la pasa por `TEST_SQL_PASSWORD`.
- **`UseSetting` y NO `ConfigureAppConfiguration`.** Los callbacks de `ConfigureAppConfiguration` se aplican DESPUÉS de que `Program` haya ejecutado sus registros, y varias piezas (`AddDistributedCaching`, `AddMessaging`, `AddHealthProbes`) leen la configuración de forma EAGER para decidir QUÉ implementación registran. Con `ConfigureAppConfiguration` el host arrancaba con `NoIdempotencyStore` y `NoCacheService` pese a que la configuración final sí traía Redis: los tests de idempotencia pasaban en verde sin probar nada.
- **Sin `MultipleActiveResultSets` en la cadena de conexión.** MARS hace que EF Core DESACTIVE los savepoints y suelte un warning en cada transacción ("If 'SaveChanges' fails, the transaction cannot be automatically rolled back"). EF Core no lo necesita —es herencia de EF6— y con él los tests corrían en un modo transaccional distinto del real.
- **`DocumentsRoot` temporal y por corrida.** `Documents:RootPath` es relativo al content root, o sea el directorio del proyecto: cada corrida dejaría PDFs dentro del repo. Y siendo una carpeta por corrida, dos suites en paralelo tampoco se pisan.
- **Rate limit subido a 1.000.000 en la suite.** El limitador particiona por IP y en `WebApplicationFactory` todas las peticiones comparten la misma, así que a las 100 peticiones toda la suite empieza a recibir 429 por un motivo que no tiene nada que ver con lo que se prueba. La política se prueba aparte, bajando el límite.
- **El recolector de huérfanos, APAGADO en la suite.** Hoy no llegaría a correr —espera 5 minutos antes de la primera pasada y la suite dura ~80 s— pero eso es una coincidencia, no una garantía: un job que BORRA FICHEROS no puede depender de llegar tarde. Se prueba invocándolo a mano, que además es determinista.
- **Sin broker (`RabbitMq:ConnectionString` vacío).** Los eventos se quedan en el outbox, que es el comportamiento diseñado y no un fallo.
- **El proveedor de documentos va explícito** para que el host de tests pruebe el MISMO camino que producción y no una rama distinta.
- **Un usuario nuevo por test y no uno compartido.** Cinco fallos de login bloquean la cuenta cinco minutos, y una cuenta compartida convierte ese bloqueo en fallos intermitentes por toda la suite.
- **El host arranca en `InitializeAsync` y no dentro del primer test**, para que un fallo de migración o de seeding se lea como lo que es.
- **Al terminar se borran los ficheros pero no la base**: los ficheros son basura del sistema de ficheros del host, no evidencia.

### `tests/ApiEcommerce.Tests/Integration/IntegrationCollection.cs`

- **Sin paralelismo a propósito, y no basta con las clases que usan el fixture.** Las que levantan su PROPIO host (degradación, arranque) también van en la colección, porque comparten la misma base y en paralelo dos `MigrateAsync` chocan con `Database 'ApiEcommerceNET8_Tests' already exists`. Pasaban en aislado y fallaban en la suite completa, que es el peor modo de fallo.

### `tests/ApiEcommerce.Tests/Integration/TestHostGuardTests.cs`

- **No es paranoia, es una cicatriz.** Con la configuración inyectada por `ConfigureAppConfiguration` el host arrancaba con `NoIdempotencyStore` y `NoCacheService`: todos los tests de idempotencia pasaban en verde sin probar absolutamente nada, porque un no-op no rompe una aserción de "dos peticiones distintas dan dos resultados". Solo cayeron los dos que exigían un replay de verdad.

### `tests/ApiEcommerce.Tests/Integration/StartupTests.cs`

- **El P0 que cubre el primer test.** Un `[Required]` sobre `SeedOptions.AdminPassword` se validaba siempre que alguien leyera `.Value`, aunque el seeding estuviera apagado. Un despliegue en producción sin esa variable moría con `OptionsValidationException` y, con `restart: unless-stopped`, entraba en crash-loop. La lección: una regla CONDICIONAL no se expresa con un atributo, va en `.Validate(...)`.

### `tests/ApiEcommerce.Tests/Integration/OrderingTests.cs`

- **El host de tests corre sin broker**, así que `OrderPlacedConsumer` no está registrado y nadie drena el outbox. Eso permite probar las dos mitades por separado y de forma determinista; que el evento llegue por AMQP lo cubre `MessageInboxTests` y la verificación manual contra un RabbitMQ real.
- **`AFailingReceiptLeavesNoMarkAndNoKey` monta el camino real** (`IMessageInbox.ProcessOnceAsync`) con un efecto que revienta. Los demás tests llaman al generador directamente, así que probaban el efecto pero no que el efecto y la marca de «mensaje procesado» se confirmen JUNTOS. Se puede escribir sin broker porque el efecto vive fuera del `BackgroundService`.
- **El desbordamiento de `(Page - 1) * PageSize`** ya produjo un 500 real desde un query string.

### `tests/ApiEcommerce.Tests/Integration/IdempotencyTests.cs`

- **`TheReplayReturnsTheSameBody` compara BYTES y no JSON parseado.** Durante mucho tiempo no se pudo exigir: el filtro memorizaba el cuerpo ya serializado y lo devolvía tal cual, y `System.Text.Json` escapa `+` como `\u002B` mientras que la respuesta viva de MVC lo emite crudo — dos cuerpos equivalentes que no eran idénticos, y sólo cuando el base64 del `rowVersion` llevaba un `+`, o sea de forma intermitente. Al bajar la garantía a la transacción, lo que se memoriza es el DTO y no la respuesta HTTP, así que el replay vuelve a pasar por el MISMO formateador de MVC; la deuda se cerró como efecto secundario.
- **Solo se memoriza el ÉXITO.** Memorizar un 409 convertiría un fallo transitorio —comprar más de lo que hay, y que luego entre stock— en un fallo PERMANENTE durante 24 h para esa clave, sin forma de que el cliente salga del bucle.
- **La cabecera de replay no la afirmaba ningún test** pese a estar en el `.feature`: se podía haber dejado de emitir sin que nada se pusiera rojo.
- **El límite de longitud de la clave.** Sin él se aceptaban claves de 7000 caracteres (medido contra la API corriendo) que acaban enteras dentro de una clave de Redis que vive 24 h, en una instancia compartida con otros proyectos.
- **`EachUserGetsItsOwnResponse...` compara CUERPOS y no stock.** El test que ya había miraba el stock, que sube igual tanto si cada uno ejecutó lo suyo como si algo raro pasó por medio; la fuga entre cuentas sólo se ve comparando los cuerpos.
- **En el test concurrente se afirma "exactamente UNA no reproducida"** en vez de "exactamente un 200": los que llegan después de que la primera termine reciben 200 CON la cabecera de replay, y eso es correcto.

### `tests/ApiEcommerce.Tests/Integration/DegradationTests.cs`

- **La decisión de degradar tiene que ser la misma en todas las implementaciones.** Que `RedisCacheService` fallara en abierto y `RedisIdempotencyStore` en cerrado hacía que un corte de Redis devolviera **500 por una compra ya cobrada**.
- **Con Redis caído se pierde el ATAJO, no la garantía.** La marca de que un comando ya se ejecutó vive en `ExecutedCommands`, en la misma transacción que el efecto. Estos tests eran imposibles de escribir antes, porque el almacén ERA Redis: "el almacén no está" y "la compra no puede ocurrir" son ahora el mismo evento y la pregunta desaparece.
- **En el test concurrente sin Redis no se admite ningún 409.** Sin el atajo no hay ni reserva ni conflicto de puerta: arbitra la clave primaria de `ExecutedCommands`, el perdedor se bloquea en la clave hasta que el otro confirma, choca, su transacción entera se deshace —incluido el stock— y devuelve el resultado del ganador.
- **La cabecera de replay sale de un hecho que reporta el servicio**, no del filtro, y por eso sigue saliendo sin Redis.

### `tests/ApiEcommerce.Tests/Integration/MessageInboxTests.cs`

- **Son los tests del P0 de la revisión del 2026-09-06** y llevaban desde entonces sin poder escribirse. El bug: la marca se confirmaba *antes* del efecto, así que si el efecto fallaba la reentrega se reconocía como duplicado, se hacía ack y el mensaje desaparecía sin procesarse. Se arregló y se verificó a mano el camino feliz, pero el caso que importa —el efecto que revienta— no tenía red.
- **Se pueden escribir ahora porque la unidad transaccional salió del `BackgroundService` a `IMessageInbox`**: no hace falta broker, solo un efecto que lance. Mientras vivió dentro del consumidor, probar esto exigía RabbitMQ en la CI.
- **El test concurrente afirma que la perdedora LANZA y que `IsConcurrentDuplicate` lo reconoce.** De eso depende el consumidor para hacer ack en vez de gastar un reintento; si dejara de reconocerlo, el mensaje daría vueltas hasta la DLQ sin que nada fallara a la vista.

### `tests/ApiEcommerce.Tests/Integration/OrphanReceiptCollectorTests.cs`

- **Este componente BORRA FICHEROS**, así que los tests que de verdad importan no son los del camino feliz sino los dos que comprueban que **no** borra. Se pueden escribir porque el efecto vive fuera del `BackgroundService`; contra un job con un temporizador de horas dentro no habría forma.
- **Usa el almacén y la base reales**: su trabajo entero es la interacción entre los dos, y con ambos falsos no probaría nada.
- **El periodo de gracia es la variable del componente.** El PDF se escribe DENTRO de la transacción, así que existe un rato antes que la fila que lo apunta: sin gracia, el recolector borraría comprobantes buenos a mitad de vuelo, y eso no se recupera.

### `tests/ApiEcommerce.Tests/Integration/AuthorizationTests.cs`

- **Es el bloque más valioso del proyecto y el que no se puede escribir con mocks**: quien decide 401 frente a 403 es el pipeline de ASP.NET Core, no el controller.
- **Varios `[Authorize]` se combinan (AND), no se sobreescriben.** Con `[Authorize(Roles = "admin")]` en la clase, la compra devolvía 403 a un cliente — que es lo que hacía el código del curso y no tiene sentido en una tienda.

### `tests/ApiEcommerce.Tests/Integration/RefreshTokenTests.cs`

- **Lo que se prueba no es "el endpoint responde 200", es que una sesión se pueda cortar.** Antes, un access token robado valía 60 minutos y no había forma de invalidarlo, ni siquiera cambiando la contraseña.
- **Contra base y Redis reales**: la garantía es una fila con índice único y la denylist es una clave con TTL; con dobles en memoria no se probaría ninguna de las dos.
- **La ventana de gracia es configuración y no constante.** Los 15 s por defecto son lo correcto en producción —un cliente legítimo lanza varios refrescos a la vez y no puede parecer un ladrón por eso— pero harían que el test de detección de robo tardara 15 s; con una constante ese test no existiría.
- **La comparación de `Set-Cookie` va sin distinguir mayúsculas.** Kestrel las emite en minúscula (`httponly`) y una comprobación sensible a mayúsculas da un falso negativo (ya pasó al verificarlo).
- **El access token va en el cuerpo y el refresh en la cookie.** El access dura 15 minutos y no se renueva solo; el refresh es el que hay que proteger de un XSS. Si además viajara en el cuerpo, la cookie `HttpOnly` no serviría de nada.
- **Matar el access token en el logout es la OPTIMIZACIÓN, no la garantía.** El `jti` entra en una denylist con TTL igual a lo que le quedaba de vida; sin Redis el token sobrevive hasta expirar (como mucho 15 minutos) pero la sesión se corta igual.

### `tests/ApiEcommerce.Tests/Integration/UserAdminTests.cs`

- **Las reglas duras son lo que un refactor descuidado se lleva por delante sin que nada más falle**: no poder quitarse el rol admin a uno mismo, no poder degradar al último administrador, no poder bloquearse a uno mismo.
- **`TheLastAdministratorCannotBeDemoted` se prueba con un admin recién creado**: como el sembrado sigue siendo admin, este no es el último y la operación debe pasar. Lo que fija el test es que la regla cuenta admins de verdad y no rechaza siempre.
- **Bloquear tiene que cortar las sesiones abiertas.** Renovar no vuelve a pedir credenciales, así que sin eso el usuario podría seguir renovando indefinidamente y el bloqueo no serviría de nada.

### `tests/ApiEcommerce.Tests/Integration/ConcurrencyTests.cs`

- **Con `Task.WhenAll` de peticiones reales, nunca en secuencia.** En secuencia estos mismos escenarios pasaban también con la implementación defectuosa, y por eso el bug del stock sobrevivió tanto tiempo.
- **El stock se descuenta con un UPDATE condicional atómico y no con concurrencia optimista.** Con `RowVersion` + reintentos se MIDIÓ que no servía: no sobrevendía, pero rechazaba compras válidas (5×200 + 5×409 sobre stock 10) al agotar los reintentos. La concurrencia optimista sirve para EDITAR una entidad, no para un contador con contención. Medición actual: 10×200, 5×409 y stock 0.

### `tests/ApiEcommerce.Tests/Integration/PaginationTests.cs`

- **Una página fuera de rango es 200 con lista vacía y no 404.** El código del curso devolvía 404 con la tabla vacía, que es semánticamente falso y obliga al cliente a tratar "no hay nada" como un error.
- **El desempate estable del orden por defecto.** `CreatedAt` no es único —lo estampa `DateTime.Now`— así que sin un `ThenBy` por clave primaria el orden no es total: SQL Server puede devolver las empatadas en distinto orden en cada consulta y, como cada página es un OFFSET/FETCH independiente, una fila sale en DOS páginas y otra en NINGUNA. La regla estaba escrita a mano en los repositorios que paginan de verdad y faltaba justo en el camino genérico, que es el que sirve `GET /api/v1/category/paged`.

### `tests/ApiEcommerce.Tests/Integration/ConcurrencyControlTests.cs`

- **La carrera que `Product.RowVersion` por sí solo NO cierra.** A lee, B edita, A guarda: el PATCH de A relee la fila, así que EF compara contra el rowversion de B y todo cuadra — A pisa el cambio de B sin que nadie se entere. Lo único que rompe el empate es el token que A leyó en su GET, y ese solo puede llegar del cliente.

### `tests/ApiEcommerce.Tests/Integration/IdempotencyStoreTests.cs`

- **Que el marcador caduque antes de tiempo ya NO permite una doble ejecución**: la duplicada pasa la puerta y choca contra la clave primaria de `ExecutedCommands`. Cuando este plazo gobernaba la GARANTÍA, era un lease sin renovación y sí abría esa ventana — es la deuda que el rediseño cerró.
- **El `release` comprueba el dueño (`fence`)**, que es el canónico de un lock distribuido: sin él, una petición cuyo marcador ya caducó borraría el marcador vivo de otra.

### `tests/ApiEcommerce.Tests/Integration/DeadLetterAdminTests.cs`

- **El ciclo completo —fallar, morir en la DLQ, reemitir y ver la orden recuperarse— se verificó ejecutando contra un RabbitMQ real**, no en la suite: aquí solo se prueban las dos cosas que no dependen del broker (quién puede llamarlo y qué contesta sin mensajería).

### `tests/ApiEcommerce.Tests/Shared/Http/GlobalExceptionHandlerTests.cs`

- **El `SqlException` DESNUDO es el caso real de `ExecuteUpdateAsync`**, que no pasa por `SaveChangesAsync`: justamente `TryDecrementStockAsync`, la sentencia con más contención del sistema. La versión anterior del handler hacía pattern matching sobre `DbUpdateException { InnerException: SqlException }` y ese caso salía 500.
- **Se RECORRE la cadena de `InnerException`** porque el anidamiento no es estable: con `EnableRetryOnFailure` agotado llega envuelto dos veces.
- **`InvalidOperationException` deliberadamente NO se mapea.** EF Core la usa para errores de PROGRAMACIÓN ("the instance of entity type X cannot be tracked because..."). Mapearla a 409 daba el código equivocado Y filtraba mensajes internos del ORM al cliente, porque `Detail` solo se censura a partir de 500.
- **El `correlationId` del cuerpo tiene que ser el mismo que viaja en `X-Correlation-Id`.** Antes era `TraceIdentifier`, así que el cuerpo y la cabecera llevaban dos ids distintos para la misma petición.
- **Los dos tests de logging son la red de una decisión de CONFIGURACIÓN.** Desde 2026-09-06 el logger del `ExceptionHandlerMiddleware` del framework está silenciado en `appsettings.json` (escribía "An unhandled exception has occurred" a nivel Error TAMBIÉN para los 4xx: medido, 10 rechazos por falta de stock = 10 incidentes falsos con traza completa). A partir de ahí este handler es el ÚNICO que registra las excepciones, y que deje de hacerlo no rompería ningún test salvo esos dos.

### `tests/ApiEcommerce.Tests/Shared/Http/ClientAbortMiddlewareTests.cs`

- **El caso real no es una `OperationCanceledException`.** Cuando el cliente corta, EF cancela el `SqlCommand` y SqlClient lanza un `SqlException`, que llegaba al final del pipeline como una excepción cualquiera: 500, nivel Error y traza completa. Medido en las pruebas de carga: 27 «errores» que no eran errores.
- **Los tests usan una excepción cualquiera a propósito**: lo que se fija es que la decisión se toma por el ESTADO de la petición y no por el tipo de la excepción. `SqlException` no se puede construir en un test —no tiene constructor público—, que es justo por lo que perseguir tipos concretos era mal diseño.
- **`IfTheResponseAlreadyStarted...` necesita sustituir la feature de respuesta**: la de `DefaultHttpContext` devuelve `HasStarted` false SIEMPRE, así que un test escrito con ella pasa sin probar nada.
- **499 (Client Closed Request) no es del RFC** pero es la convención de facto (la de nginx).

### `tests/ApiEcommerce.Tests/Shared/Http/SqlExceptionFactory.cs`

- **La reflexión es frágil ante un cambio de versión de `Microsoft.Data.SqlClient`**, pero la alternativa es no poder probar el mapeo 2601/2627/1205/547 sin levantar SQL Server, y ese mapeo ya se rompió una vez. Si algún día falla al actualizar el paquete, el fallo es DEL HELPER y no del handler: se arregla aquí y los tests siguen valiendo.

### `tests/ApiEcommerce.Tests/Shared/Documents/LocalDocumentStoreTests.cs`

- **La razón de que este almacén exista aparte de `IFileStorage`**: si el fichero acabara bajo `wwwroot/`, `UseStaticFiles` lo serviría a quien adivinara la ruta y las tres barreras de acceso se quedarían en dos.
- **`Path.GetFullPath` normaliza `.` y `..` pero NO sigue los enlaces.** Sin resolverlos, un enlace dentro del almacén (`2026 -> ../secretos`) produce una ruta que EMPIEZA por la raíz, pasa el filtro y lee un fichero de fuera. Lo destapó la revisión de seguridad reproduciéndolo, no leyéndolo.
- **`Path.GetFullPath` usa el DIRECTORIO DE TRABAJO.** Con eso, dónde acaban los comprobantes dependería de desde dónde se lanzó el proceso (un `dotnet /app/ApiEcommerce.dll` desde otra carpeta escribiría en otro sitio) y todas las claves ya guardadas darían 404.
- **`GetFullPath` CONSERVA la barra final y la comprobación concatena una.** Sin recortarla, la raíz quedaba como `.../docs//` y NINGUNA clave pasaba el filtro: guardar lanzaría y abrir devolvería null para todo — todos los comprobantes en 404 permanente mientras la orden dice "available". Un carácter, y en silencio.
- **El separador final del chequeo** es lo que impide que `/tmp/docs-otro` pase por estar "dentro" de `/tmp/docs`.
- **La clave viene de la base, pero basta una fila manipulada, una migración descuidada o un endpoint futuro que la acepte del cliente**; por eso se compara la ruta ya canonicalizada y no se filtra por la cadena `..`, que no cubre rutas absolutas.

### `tests/ApiEcommerce.Tests/Shared/Storage/LocalFileStorageTests.cs`

- **Allowlist y no denylist**: una denylist siempre se olvida de algo. El SVG está fuera porque lleva JavaScript y se sirve desde el MISMO origen que la API: XSS almacenado.
- **Persistir `{Request.Scheme}://{Request.Host}/...` (lo que hacía el curso) guarda una cabecera que controla el cliente**, y queda rota al cambiar de dominio o al meter un proxy delante.
- **Del nombre del cliente solo se lee la extensión**, ni siquiera "solo la parte del nombre": es la puerta de entrada al path traversal.

### `tests/ApiEcommerce.Tests/Shared/Paging/PagedResultTests.cs`

- **`(Page - 1) * PageSize` en `int` DESBORDA con `?page=2147483647`**: el resultado es negativo y SQL Server responde «The offset specified in a OFFSET clause may not be negative» — un 500 con traza a partir de un query string, en TODOS los endpoints paginados. Lo destapó una revisión de seguridad probándolo.
- **Sin resultados son 0 páginas y no 1**: un paginador con "página 1 de 1" sobre una lista vacía miente.

### `tests/ApiEcommerce.Tests/Shared/Crud/CrudServiceTests.cs`

- **Son las invariantes que `CrudService` es `sealed` para proteger**, así que son exactamente las que hay que fijar con tests.
- **El orden reglas → mapeo → repositorio es la razón de ser de la firma.** La regla recibe `existing` y debe verlo con el estado PREVIO; si el mapeo corriera antes, una regla del tipo "no se puede bajar el precio más de un 50%" compararía el valor nuevo consigo mismo.

### `tests/ApiEcommerce.Tests/Shared/RetryAttemptsTests.cs`

- **El bug que ya se cometió con `x-death`**: los valores de texto viajan como `byte[]` en el cliente AMQP y compararlos sin convertir devuelve false EN SILENCIO. Un operador que ponga la cabecera a mano desde la UI del broker la manda como texto.
- **Borrar la cabecera resetea el presupuesto**, que es el procedimiento de replay desde la DLQ. Con `x-death` era imposible —sobrevive al paso por la DLQ— y un mensaje reencolado por un operador moría en la primera entrega.
- **Es una función pura y por eso se sacó del consumidor**: mientras vivió dentro de un `BackgroundService` atado a AMQP, probar esto exigía levantar RabbitMQ.

### `tests/ApiEcommerce.Tests/Features/Accounts/AuthServiceTests.cs`

- **`LoginAsync` devuelve el MISMO literal para usuario desconocido y contraseña incorrecta.** Si los mensajes difirieran, el login se convertiría en un ORÁCULO para enumerar usuarios. Por eso los dos tests afirman el mismo string.
- **`lockoutOnFailure: true`.** Sin ese flag Identity NUNCA cuenta fallos y el bloqueo configurado en `AddIdentity` no se dispara jamás: la protección existe en la configuración y no en la práctica.
- **`UserManager` y `SignInManager` son clases concretas con constructores enormes.** Moq las puede simular porque sus métodos son `virtual`, pero hay que pasarles los argumentos posicionales. Es feo y es el precio de que Identity no exponga interfaces.

### `tests/ApiEcommerce.Tests/Features/Catalog/CategoryRulesTests.cs`

- **Son la prueba de que la composición abarató el testeo**: las reglas viven fuera del CRUD, así que se instancian con un repositorio falso en una línea, sin base de datos, sin `IMapper` y sin `HttpContext`. Con los hooks `virtual` de una clase base habría que levantar el servicio entero.

### `tests/ApiEcommerce.Tests/Features/Catalog/MappingProfilesTests.cs`

- **El bug que costó un 500.** Con `.ForAllMembers(o => o.Condition(...))`, la `Condition` recibe el valor YA CONVERTIDO al tipo del destino: un `int?` nulo llegaba como 0, no se saltaba y machacaba el campo. Con `CategoryId = 0` se rompía la clave foránea y el cliente recibía un 500 sin relación aparente. Por eso el PATCH parcial se expresa campo a campo.

### `tests/ApiEcommerce.Tests/Features/Catalog/CachedCategoryServiceTests.cs`

- **`GetPagedAsync` no toca la cache, y es decisión explícita**: cada combinación page/pageSize sería una clave que ninguna invalidación conoce. Si alguien "mejora" esto cacheando páginas, el test cae.

### `tests/ApiEcommerce.Tests/Features/Ordering/ReceiptGeneratorTests.cs`

- **Estos tests existen porque el efecto vive FUERA del consumidor.** Mientras la generación viviera dentro de un `BackgroundService` atado a AMQP, «¿qué pasa si el almacén falla?» no se podía preguntar sin un broker delante.
- **La segunda red sobre la del inbox no es redundante.** El inbox deduplica por `MessageId`, así que un replay manual desde la DLQ —que llega con otro id— regeneraría el PDF y dejaría el anterior huérfano en el almacén.

### `tests/ApiEcommerce.Tests/Features/Ordering/QuestPdfReceiptRendererTests.cs`

- **Es el test que cubre la dependencia nativa.** En Linux QuestPDF dibuja con SkiaSharp, que necesita `libfontconfig1`: sin ella esto revienta, y ese fallo de otra forma solo aparecería *dentro del contenedor* y mensaje a mensaje, con cinco reintentos y una DLQ por comprobante.
- **La licencia se declara en el `static` constructor igual que en el arranque real**: QuestPDF lanza al GENERAR, no al registrar, así que sin esa línea el test fallaría por un motivo que no tiene nada que ver con lo que prueba.
- **Un stream en la última posición se copia al almacén como un fichero de CERO bytes**, sin un solo error: la orden diría "comprobante disponible" y la descarga daría un PDF vacío.
- **Se embebe Lato en vez de pedir una fuente del sistema** para que el documento salga igual en local y en la imagen; con una del sistema, dos réplicas podrían producir comprobantes distintos para la misma orden.

