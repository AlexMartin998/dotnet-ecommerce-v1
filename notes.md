# Init

- --- Crear proyecto de C#
  - -- Crear in simple proyeto de c#
```sh
  dotnet new console --name CsBases
```
  - -- Correr ese programa simple
```sh
dotnet run
```

  - -- En `CsBases.csproj` podemos definir importaciones estaticas (globales)
    - asi en las clases solo uso el methdo y no la clase como en el console log
```xml
  <!-- custom: -->
  <ItemGroup Label="Simplificando el uso de la consola">
    <!-- importacion estatica -->
    <Using Include="System.Console" Static="true" />
  </ItemGroup>
```
    - Asi en el Program.cs puedo usar solo:
```cs
class Program
{
    static void Main(string[] args)
    {
        WriteLine("Hello, World!");
    }
}
```

    - s



--- Para crear clases como si fuera un IDE posi es usar el `SOLUTION EXPLORER` de vscode
  -- + Dir > Fundamentals
    -- + File > Seleccionamos tipo de archivo (class) y damos nombre, vscode crea esa clase :D como en JETBRAINS con Java
      - Ya crea la clase
        - El namespace tb















# Patron Adaptador
- --- Patron de diseño estructural q permite q 2 Class con interfaces incompatibles W juntas (`DTO`)
  - -- Entidad (DB)
  - -- Adaptador - es un Mapper (en Java ModelMapper)
  - -- Dto


## DI
- --- Creamos el Interface del SERVICE
  - -- Creamos su Implementacion
  - -- Creamos el `Manager`
    - Inyeccion de Dependencias `ProductManager.cs` x constructor





# Async
- --- Como en JS con Async/Await, solo que aca en lugar de Promise se trabaja con `Task`




# Atributos o Decoradores
- --- En `.Net` ya se tiene DataNotations, pero aca crearemos uno personalizado :v
  - -- De .Nets














# Que es `.NET`
- --- Que es .NET
- -- Es un framework open source multiplatform creado por Microsoft para el desarrollo de aplicaciones `Web` modernas
  - Eficiente y escalabel
  - Es modular

- --- ARQUITECTURA .NET
- -- Middleware
  - Como en todo framework de backend, intercepta la req
- -- DI
  - Permite tener apps modulares, mantenibles, escalables q permiten reutilizar el codigo
- -- Logging
  - Configuracion de logs utiles


- --- API
  - -- Nos permiten tener versionado


- --- DB
  - -- SQL Server
    - Lo mas usado para .NET





### Init project
- --- Crear un proyecto API desde cero
  - -- Con VSCode podemos escoger una plantilla para crear el proyecto 
    - Ctrl + Shift + P >>> `.NET new Project`
    - `ASP .NET Core Web API`  es la que debemos seleccionar
    - Damos nombre y enter
      - ApiEcommerce
    - Ver opciones de plantilla
      - Utilizar Controladores en TRUE



### Structura del project
- --- Tenemos una estructura x default
  - -- bin/
    - Los compilados del proyecto final
  - -- Obj/
    - generados x .NET q no tocamos
  - -- Properties/
    - `launchSettings.json`
      - Configura la App como tal
  - -- `ApiEcommerce.csproj`
    - Config del project, version de .net, las dependencias que se estan usando
    - Como el pom.xml de Spring Boot
  - -- `ApiEcommerce.http`
    - Probar algun endpoint directo desde aca
  - -- `appsettings.json`
    - Permite establecer cnofigs de JWT
    - Conexion a la BD
    - Assets
  - -- `Program.cs`
    - Como el main/index de todo framework, donde se deberan configurar lo que necesitemos
    - Services, middlewares, rutas ,etc
  - -- s
    - 


- --- `Program.cs`
  - -- Con este template de API es quien se encarga de configurarla com tal
  - -- Por defecto viene asi:
    - con su builder de confi de la app q puede recibir `args` por consola al iniciar la app si asi lo necesitamos
  - -- Servicios
    - Los services de la app como sus controllers, Swagger, etc.
    - 
```cs
var builder = WebApplication.CreateBuilder(args);

// Add services to the container. -----------

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// con esta podemos config los middlewares ---
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
```










































### Init Api
- --- Levantamos con Docker el SQLServer y nos conectamos con VSCode 
  - -- Conexion a la db
    - Se hace con `ConnectionStrings` con el `ConexionSql` en el `appsettings.json`



- --- Librerias para conexion a la BD
  - -- Podemos usar VSCode para gestionar paquetes con `NuGet Package`
    - Con Ctrl + Shift + P >>> Add NuGet Package >>> Seleccionamos los diferentes packages
      - `Microsoft.EntityFrameworkCore SqlServer`     <- Conexion a db SqlServer
      - `Microsoft.EntityFrameworkCore Tools`         <- Migraciones
  - -- Verificamos en `ApiEcommerce.csproj` q se hayan instalado
      - https://code.visualstudio.com/docs/csharp/package-management




- --- Archivo de Contexto (clave para cuando se usa EntityFrameworkCore)
  - -- Mapea las clases a tablas de db (como un ORM)
  - -- Creamos el `AppDbContext` para mapear esta conexion
    - Aca registramos todos los modelos a mapear en la DB


- --- Registramos este  `AppDbContext`  en el  `Program.cs`
  - -- Se hace en base al AppDbContext q se creo, y se registra la STRING definida en ConnectionStrings del `appsettings.json`
    - En este caso es  ConexionSql




- --- MIGRACIONES
  - -- Instalamos de manera GLOBAL las herramientas de dotnet-ef
```sh
dotnet tool install --global dotnet-ef
```
    - Verificar su version isntalada
```sh
➜  ApiEcommerce git:(main) ✗ dotnet ef --version                   
Entity Framework Core .NET Command-line Tools
9.0.9
```
  - -- Instalar package desde el CLI `dotnet add package` en lugar de NuGet
    - En ese taso instalamos `Design` para tener las herramientas del `ef` y crear migraiones, etc.
    - Verificamos en `ApiEcommerce.csproj` pa saber si si se instalo (como el pom.xml de spring boot)

```sh
dotnet add package Microsoft.EntityFrameworkCore.Design
```

  - -- Crear la Migracion como tal
    - Ahora que tenemos Design y el ef podemos crear y correr migraciones:
```sh
# crear migracion (no afecta a la db, solo crea el archivo por decir (makemigration drf))
dotnet ef migrations add InitialMigration
# afecta o corre la migracion (migrate drf)
dotnet ef database update
```
    - CREAR migraciones:  `add`     <--   Migrations/ se crea con los archivos
      - `20250921163032_InitialMigration.cs` tiene el 
        - up:     crea la tabla en base al modelo
        - down:   revierte la migracion si nos equivocamos
    - CORRER migraciones: `db upd`  <--   Afecta a DB
      - Done. <- todo fue bien :D














## Patron Repository
- --- Con interface y su Impl como en Spring Boot, nada especial
  - -- Salvo el `.Save()` method q es el que commitea basicamente en la db para cada operacion idempotente en la db




## AutoMapper - `Mapster`  <- (Model Mapper in spring boot)
- --- AutoMapper sera de PAGO, pero algo medio parecido y gratis es Mapster
  - -- Esto es basicamente el Simil de MODEL MAPPER de Spring Boot

  - -- Install AutoMapper con NuGet
    - `AutoMapper` tal cual, la ultima version
    - Lo verificamos en el `ApiEcommerce.csproj` q se haya instalado

    - Si se instalo otra version, simplemente se puede cambiar en el .csproj y luego correr en la terminal: `dotnet restore`

  - -- Creamos los Mappers, aunque ya usamos AutoMapper, como a futuro va a ser de pago ,o si cambia algo, pues ya todo queda aislado en su propio Wrapper, para este caso, cada mapper
    - Creamos el `CategoryProfile`
      - Q es el que definira las methdos para esos mappers con AutoMapper


    - https://medium.com/@rictorres.uyu/automapper-vs-mapster-a-comparative-analysis-for-net-developers-77d8dba4942f






### Controllers
- --- Como en Spring Boot se maneja por Anotaciones e igual hay para MVC y para APIs
  - -- API:   El que es especifico para REST
  - -- MVC:   El que retorna Views html y demas 


  - -- Swagger 
    - Como lo hice con vscode no vino instalado y tiene que instalarse, lo que NO es necesario desde el CLI propio de dotnet
      - `dotnet add package Swashbuckle.AspNetCore`
    - Configurar el Swagger en el `Program.cs`
      - http://localhost:8021/swagger/index.html



  - -- Levantar el API:
    - `dotnet run --urls "http://0.0.0.0:8021"`         // solo una vez el build no para dev
    - `dotnet watch run --urls "http://0.0.0.0:8021"`   // para dev - watcher




  - https://learn.microsoft.com/es-mx/aspnet/core/web-api/?view=aspnetcore-8.0#return-values

























## Product
- --- Relaciones DB
  - -- Creamos el modelo `Product.cs`
  - -- Lo registramos en el `AppDbContext.cs`
  - -- Creamos migracion
    - `dotnet ef migrations add CreateTableProduct`
      - Price en SQL Server req una anotacion/property mas para la precision
        - `[Column(TypeName = "decimal(18,2)")]` // Precision and scale for SQL Server
      - `dotnet ef migrations remove` si algo salio mal y quiero Elminar la migracion (archivo)
  - -- Aplicar la migracion para afectar la DB
    - `dotnet ef database update`

  - https://learn.microsoft.com/es-mx/ef/core/modeling/relationships



### Product DTO
- --- Creamos los DTO para response, create, upd, etc.

- --- Creamos el Profile para el Mapping con AutoMapper


- --- Repository
  - -- S



































































# ==========================================================
# SECCIONES 8-15  (traidas del curso a esta arquitectura)
# ==========================================================
- --- Lo que se trajo del repo de referencia (`AGENTS/__ref__/01/code`, ramas `fin-seccion-*`):
  - -- sec 8+10+13 -> Auth (User -> Identity -> JWT)
  - -- sec 9       -> CORS
  - -- sec 11      -> Cache          (aqui: Redis, no ResponseCaching)
  - -- sec 12      -> API Versioning
  - -- sec 14      -> Subida imagenes
  - -- sec 15      -> Seeding + Paginacion (Mapster NO: se queda AutoMapper)

- --- Regla: se trajo la FEATURE, no el codigo. Cada capitulo dice que hacia mal el
      curso y por que aqui se hace distinto.

```sh
# infra que tiene que estar arriba (esta fuera de este repo)
docker compose ps -a          # sqlserver_ecommerce :1434  +  redis_generic :6999
```










## 0. Fix previo: el build estaba roto
- --- `AGENTS/__ref__/01/code/*.cs` esta DENTRO de la carpeta del proyecto
  - -- MSBuild compila por convencion TODO `.cs` bajo el csproj -> tipos duplicados -> 52 errores
  - -- Se excluye en el `.csproj`:
```xml
<ItemGroup>
  <Compile Remove="AGENTS/**" />
  <Content Remove="AGENTS/**" />
  <EmbeddedResource Remove="AGENTS/**" />
  <None Remove="AGENTS/**" />
</ItemGroup>
```
  - -- Leccion: en .NET no hay "lista de archivos" como en un pom/webpack. Es glob implicito.










## 1. Auth: Identity + JWT
- --- Paquetes
```sh
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore --version 9.0.9
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer     --version 9.0.9
```

- --- `ApplicationUser : IdentityUser`   (`Models/ApplicationUser.cs`)
  - -- Identity regala: hash PBKDF2+salt, normalizacion, lockout, roles, tokens
  - -- OJO: su PK es `string` (GUID), NO `int`
    - por eso NO implementa `IEntity` y NO entra en `BaseRepository<T>` / `CrudService<>`
    - y esta bien: un usuario no es un CRUD, es registro/login/roles -> lo gobierna `UserManager`

- --- `AppDbContext : IdentityDbContext<ApplicationUser>`  (antes `: DbContext`)
  - -- `base.OnModelCreating(modelBuilder)` es OBLIGATORIO -> es quien mapea las 7 tablas `AspNet*`
  - -- El curso dejaba ADEMAS una tabla `Users` legacy + `public DbSet<User> Users`
    - eso OCULTA (`CS0108`) el `Users` de `IdentityDbContext` -> sus checks de unicidad
      consultaban una tabla vacia y SIEMPRE devolvian "libre". Aqui solo existe `ApplicationUser`.

```sh
dotnet ef migrations add AddIdentitySupport
dotnet ef database update
```

- --- Equivalencias mentales desde Spring Security (`ServiceCollectionExtensions.cs`)
  - -- `UserManager`   ~ `UserDetailsService` + `PasswordEncoder`
  - -- `SignInManager` ~ `AuthenticationManager`
  - -- `RoleManager`   ~ gestion de `GrantedAuthority`

- --- `AddIdentityCore` y NO `AddIdentity`
  - -- `AddIdentity` registra ademas los esquemas de COOKIE de Identity
    - en una API stateless con Bearer nadie los usa y se pelean por ser el "default scheme"
  - -- Con Core hay que pedir explicito lo que si se usa:
```csharp
services.AddIdentityCore<ApplicationUser>(o => { /* password policy, lockout */ })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()          // <- necesario para CheckPasswordSignInAsync + lockout
    .AddDefaultTokenProviders();
```

- --- Capas (el curso metia TODO en el repositorio: consulta + hash + JWT + DTOs)
```
AuthController -> IAuthService -> UserManager/SignInManager
                             \-> IJwtTokenService  (firma el token, Singleton)
```
  - -- `IJwtTokenService` aparte: el dia que el token sea opaco cambia 1 clase

- --- Config TIPADA y validada al arranque (`Shared/Auth/JwtOptions.cs`)
```csharp
services.AddOptions<JwtOptions>()
    .Bind(configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();          // <- secreto vacio/corto = NO arranca
```
  - -- Es el `@ConfigurationProperties` + `@Validated` de Spring
  - -- Sin `ValidateOnStart` el fallo no sale en el arranque, sale en el primer login, en prod

- --- `TokenValidationParameters`: las CUATRO validaciones ENCENDIDAS
```csharp
ValidateIssuerSigningKey = true,
ValidateIssuer   = true,   ValidIssuer   = jwt.Issuer,
ValidateAudience = true,   ValidAudience = jwt.Audience,
ValidateLifetime = true,   ClockSkew = TimeSpan.FromSeconds(30)
```
  - -- El curso apagaba `ValidateIssuer`/`ValidateAudience` porque su token no emitia `iss`/`aud`
    - eso es parchear el sintoma: un token firmado por CUALQUIER otro servicio con la misma clave valia
  - -- `ClockSkew` por defecto son 5 MIN de gracia -> un token expirado seguia valiendo 5 min

- --- Claims que se emiten: `sub`, `unique_name`, `email`, `jti` + `NameIdentifier`/`Name`/`Role`
  - -- `jti` = id del token -> es lo que permitiria revocarlo (denylist en Redis)
  - -- UN claim `role` POR ROL (el curso mandaba solo `roles.FirstOrDefault()` -> multi-rol perdia permisos)
  - -- `exp`/`nbf` van en UTC SIEMPRE (RFC 7519). Unica excepcion al `DateTime.Now` local del repo.

- --- Endpoints
```
POST /api/v1/auth/register   201 + token   (409 user/email repetido, 422 password debil)
POST /api/v1/auth/login      200 + token   (401 credenciales, 403 cuenta bloqueada)
GET  /api/v1/auth/me         200           (requiere token)
```

- --- Seguridad que el curso NO tenia
  - -- El `Role` NO se acepta en el body del register
    - en el curso `POST /Users` era anonimo y aceptaba `"Role":"Admin"` -> CUALQUIERA se hacia admin
  - -- Mismo mensaje para "no existe" y "password mala" -> si no, el login es un oraculo para enumerar usuarios
  - -- `CheckPasswordSignInAsync(user, pwd, lockoutOnFailure: true)`
    - sin ese flag Identity cuenta fallos pero NUNCA bloquea
  - -- Nada de `SHA256(password)` a mano: `UserManager.CreateAsync(user, password)` ya hashea

```sh
# probar
curl -X POST localhost:8021/api/v1/auth/login -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"Admin123!"}'
```










## 2. Autorizacion por roles  <- EL GOTCHA MAS IMPORTANTE
- --- `Shared/Auth/Roles.cs` con `const string` (no `static readonly`)
  - -- `[Authorize(Roles = ...)]` es un ATRIBUTO: solo admite constantes de compilacion

- --- ⚠️ Varios `[Authorize]` se COMBINAN (AND), NO se sobreescriben
  - -- clase `[Authorize(Roles="admin")]` + accion `[Authorize]`  ==  sigue exigiendo admin
  - -- el UNICO atributo que gana sobre la clase es `[AllowAnonymous]`
  - -- Me pico de verdad: `POST /product/buy` daba 403 a un usuario normal

- --- Patron correcto (cerrado por defecto, pero relajable):
```csharp
[Authorize]                          // CLASE: el requisito mas DEBIL = estar autenticado
public class ProductController
{
    [AllowAnonymous]                 // lectura publica          -> gana sobre la clase
    [Authorize(Roles = Roles.Admin)] // escritura                -> suma (AND) al de la clase
    // sin atributo                  -> hereda [Authorize] = cualquier autenticado (buy)
}
```
  - -- Si mañana alguien olvida el atributo en un endpoint nuevo: queda AUTENTICADO, no publico

- --- Reparto final
```
GET  categorias/productos/search/paged   anonimo
POST /product/buy                        autenticado (cualquier rol)
POST PATCH DELETE + /image               rol admin
```
  - -- El curso pedia rol Admin para COMPRAR. En una tienda eso no tiene sentido.










## 3. API Versioning  (segmento de URL)
```sh
dotnet add package Asp.Versioning.Mvc              --version 8.1.0
dotnet add package Asp.Versioning.Mvc.ApiExplorer  --version 8.1.0
# OJO: la 10.x es solo net10.0 -> hay que pinear 8.1.0 en net9.0
```

- --- Ruta: `[Route("api/v{version:apiVersion}/[controller]")]` + `[ApiVersion("1.0")]`
  - -- `/api/category`  ->  404      `/api/v1/category`  ->  200
```csharp
options.ApiVersionReader = new UrlSegmentApiVersionReader();
options.ReportApiVersions = true;              // cabeceras api-supported/deprecated-versions
options.AssumeDefaultVersionWhenUnspecified = false;
```
  - -- `AssumeDefault...= true` es HUMO con versionado por ruta: `/api/category` no matchea
       ninguna plantilla y da 404 ANTES de que el versionador opine. El curso lo tenia en true.

- --- ⚠️ `CreatedAtRoute` con ruta versionada necesita el parametro `version`
```csharp
return CreatedAtRoute("GetCategory",
    new { version = HttpContext.ApiVersionValue(), id = newId }, null);
```
  - -- Si no, falla la generacion del header `Location` -> 500 sin relacion aparente con el POST

- --- ⚠️ Todo controller necesita version, tambien los de infra
  - -- `/health` empezo a dar 404 al activar versionado -> se arregla con `[ApiVersionNeutral]`

- --- Swagger: un documento POR VERSION, generado solo
  - -- `Shared/Http/ConfigureSwaggerOptions.cs` = `IConfigureOptions<SwaggerGenOptions>`
  - -- lee `IApiVersionDescriptionProvider` -> añadir `v2` es poner `[ApiVersion("2.0")]`, CERO cambios en Program.cs
  - -- El curso tenia `SwaggerDoc("v1")` y `SwaggerDoc("v2")` hardcodeados en 2 sitios
```csharp
options.GroupNameFormat = "'v'VVV";        // v1, v2
options.SubstituteApiVersionInUrl = true;  // en la UI sale /api/v1/... y no /api/v{version}/...
```

- --- Deprecar de verdad: `[ApiVersion("1.0", Deprecated = true)]`
  - -- NO el `[Obsolete]` de la BCL (ese no emite la cabecera HTTP, solo marca el JSON)

- --- Comentarios `///` en Swagger: en el `.csproj`
```xml
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<NoWarn>$(NoWarn);1591</NoWarn>   <!-- 1591 = "falta doc XML en miembro publico" -->
```










## 4. CORS
- --- Origenes desde CONFIG (`Cors:AllowedOrigins`), nunca hardcodeados
```csharp
options.AddPolicy(CorsPolicies.Default, policy => policy
    .WithOrigins(origins).AllowAnyMethod().AllowAnyHeader()
    .WithExposedHeaders("api-supported-versions", "api-deprecated-versions"));
```
  - -- El curso tenia `WithOrigins("*")` en una politica llamada `AllowSpecificOrigin` (nombre mentiroso)
    - con JWT en cabecera `Authorization`, comodin = cualquier pagina llama la API desde el navegador
  - -- Lista vacia -> no permite ningun origen. FALLA CERRADO, no abierto.

- --- Posicion en el pipeline: `UseCors` ANTES de `UseAuthentication`
  - -- un preflight `OPTIONS` no lleva token y tiene que poder responderse sin pasar por autorizacion










## 5. Cache: Redis  (en vez del `[ResponseCache]` del curso)
```sh
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis --version 9.0.9
```

- --- Por que NO `AddResponseCaching` (lo del curso):
  - -- vive en la memoria de UN proceso -> inutil con varias replicas
  - -- NO cachea NADA si el request lleva cabecera `Authorization` (causa #1 de "no me funciona")
  - -- NO se puede invalidar -> una categoria borrada se sigue sirviendo hasta que expire el TTL

- --- Aqui: cache-aside sobre `IDistributedCache`
```
Shared/Caching/  ICacheService  (GetOrSetAsync / RemoveAsync)
                 RedisCacheService  <- JSON sobre IDistributedCache
                 NoCacheService     <- Null Object, si Redis:Configuration esta vacio
                 CacheKeys          <- las claves en UN sitio
```
  - -- Interfaz propia y no `IDistributedCache` pelado: ese habla en `byte[]` y el
       "get, si null calcula y set" se repetiria en cada servicio

- --- ⭐ El decorador: `Service/CachedCategoryService.cs`
  - -- `CategoryService` NO sabe que existe cache. Se testea sin Redis. Quitar cache = borrar 1 linea de DI
  - -- Es el `@Cacheable` / `@CacheEvict` de Spring pero EXPLICITO
```csharp
// lectura
=> cache.GetOrSetAsync(CacheKeys.Category(id), t => inner.GetByIdAsync(id, t), Ttl, ct);

// escritura: invalida DESPUES de que la escritura haya ido bien
await inner.UpdateAsync(id, dto, ct);
await cache.RemoveAsync(ct, CacheKeys.CategoryAll, CacheKeys.Category(id));
```
  - -- Registro DI (a mano, sin Scrutor):
```csharp
services.AddScoped<CategoryService>();
services.AddScoped<ICategoryService>(sp => new CachedCategoryService(
    sp.GetRequiredService<CategoryService>(), sp.GetRequiredService<ICacheService>()));
```

- --- Reglas que me costaron entender
  - -- La cache FALLA EN ABIERTO: si Redis se cae se loguea y se sirve de la base.
       Una cache que tumba la API convierte una optimizacion en punto unico de fallo.
  - -- Solo se cachea lo PUBLICO. Nada que dependa del usuario -> cache compartida = fuga entre cuentas.
  - -- Los listados PAGINADOS no se cachean: cada `page/pageSize` seria una clave que
       ninguna invalidacion conoce. Para eso hace falta invalidar por prefijo / versionar la clave.

```sh
# ver las claves en el redis de verdad
redis-cli -h 172.17.0.1 -p 6999 KEYS 'apiecommerce:*'
# -> apiecommerce:category:1003     (InstanceName + clave logica)
```










## 6. Paginacion
- --- `Shared/Paging/PagedResult.cs`  (`record`, inmutable)
```csharp
Items, Page, PageSize, TotalItems          // + calculados: TotalPages, HasNext, HasPrevious
```
  - -- Lleva `TotalItems` y no solo `TotalPages`: es el dato del "mostrando 1-10 de 137".
       El curso lo calculaba y lo TIRABA (pagaba el COUNT sin cobrarlo).

- --- `Models/Dtos/PageQuery.cs` con `[Range(1, 100)]` en `PageSize`
  - -- El tope NO es decorativo: sin el, `?pageSize=1000000` en un endpoint anonimo es DoS de 1 peticion

- --- `BaseRepository.GetPagedAsync` reusa `ApplyDefaultOrder` -> orden ESTABLE
  - -- `Skip/Take` sin orden estable puede repetir una fila en dos paginas y saltarse otra
  - -- En productos: `.OrderByDescending(CreatedAt).ThenByDescending(Id)`  <- desempate

- --- `ProductRepository.GetPagedWithCategoryAsync` con `.Include(p => p.Category)`
  - -- El curso olvido el Include SOLO en el metodo paginado -> `categoryName` salia vacio en silencio

- --- Pagina fuera de rango -> **200 con `[]`**, NO 404
  - -- 404 es "el recurso no existe"; la coleccion SI existe, lo que no hay son resultados
  - -- El curso devolvia 404 con la tabla vacia

```
GET /api/v1/product/paged?page=1&pageSize=10
GET /api/v1/category/paged?page=1&pageSize=10
```










## 7. Subida de imagenes
- --- Endpoint APARTE, no un campo del PATCH
```
POST /api/v1/product/{id}/image     multipart/form-data, campo `file`     [admin]
```
  - -- El curso cambio `POST/PUT /products` de `[FromBody]` a `[FromForm]`:
       breaking change no versionado que rompio a todos sus clientes JSON

- --- `Shared/Storage/`  `IFileStorage` + `LocalFileStorage` + `FileUpload`
  - -- `FileUpload` (record) desacopla el servicio de `IFormFile`
    - el CONTROLLER adapta `IFormFile` -> `FileUpload`. El servicio no conoce ASP.NET.
    - el curso metia `IFormFile` DENTRO de los DTOs de `Models/Dtos` (modelo acoplado al framework web)

- --- Validacion en TRES niveles (el curso no tenia ninguna)
```
1. tamaño        -> [RequestSizeLimit] corta ANTES de leer el body entero + MaxBytes
2. extension     -> ALLOWLIST (.jpg .jpeg .png .gif .webp), nunca denylist
3. magic bytes   -> la firma real del archivo (FF D8 FF = jpg, 89 50 4E 47 = png...)
```
  - -- Por que allowlist: estos archivos se sirven desde el MISMO ORIGEN que la API
       -> subir un `.svg`/`.html` es XSS almacenado con las cookies de la API
  - -- Extension y Content-Type los pone el CLIENTE y se pueden mentir los dos. Los bytes no.

- --- El nombre del archivo lo genera el SERVIDOR: `Guid.NewGuid():N` + extension
  - -- usar el nombre del cliente (aunque sea "solo la extension") es la puerta al path traversal
  - -- ademas se verifica con `Path.GetFullPath` que la ruta siga dentro de la carpeta gestionada

- --- Se guarda RUTA RELATIVA (`/ProductsImages/xxx.png`), NO URL absoluta
  - -- el curso persistia `{Request.Scheme}://{Request.Host}/...`
    - `Host` es una cabecera que CONTROLA EL CLIENTE -> host header injection almacenada
    - y la URL queda rota al cambiar de dominio o al meter un proxy delante
  - -- tampoco se expone la ruta del filesystem (el curso mandaba `ImgUrlLocal` al cliente)

- --- Borrado del archivo anterior: SI existe (en el curso era codigo muerto -> huerfano por cada update)
  - -- se guarda la NUEVA antes de borrar la vieja: si la validacion falla, se conserva la que habia
  - -- `DeleteAsync` del producto tambien borra el archivo

- --- Otros detalles
  - -- `IWebHostEnvironment.WebRootPath`, NO `Directory.GetCurrentDirectory()`
       (el CWD del proceso no es el del proyecto al publicar / en systemd / en Docker)
  - -- `CopyToAsync`, no `CopyTo`: una subida sincrona bloquea un hilo del pool
  - -- `app.UseStaticFiles()` para servir `wwwroot/`
  - -- Limitacion asumida: disco local no escala horizontal. Por eso existe `IFileStorage`:
       cambiar a S3/Blob es 1 clase.










## 8. Seeding
- --- `Data/DataSeeder.cs`, llamado desde `Program.cs` con SCOPE PROPIO
```csharp
using (var scope = app.Services.CreateScope())
    await DataSeeder.SeedAsync(scope.ServiceProvider);
```
  - -- El contenedor RAIZ no puede resolver `Scoped` (`AppDbContext`, `UserManager`) -> lanza en el arranque
  - -- El curso usaba el hook `.UseSeeding(...)` de EF9 SIN llamar a `Migrate()`
       -> con `dotnet run` el seed NO CORRIA NUNCA (falso "siembra en cada arranque")

- --- Roles y usuarios con `RoleManager` / `UserManager`, NUNCA con INSERT directo
  - -- un insert a mano se salta `SecurityStamp` y `ConcurrencyStamp`
  - -- sin `SecurityStamp` el lockout y la invalidacion de credenciales de Identity dejan de funcionar

- --- `SaveChangesAsync()` de las categorias ANTES de crear los productos
  - -- es lo que asigna los `Id` reales
  - -- el curso hacia `Categories.Find(1)` sobre categorias aun no persistidas: solo funcionaba por accidente

- --- Guardas: `Seed:Enabled` (false por defecto) + password del admin desde CONFIG
  - -- el seeder del curso sembraba `Admin123!` hardcodeado y SIN guarda de entorno -> tambien en produccion

```json
"Seed": { "Enabled": true, "AdminUsername": "admin",
          "AdminEmail": "admin@apiecommerce.local", "AdminPassword": "Admin123!" }
```










## 9. Rate limiting  (no estaba en el curso)
- --- Integrado en .NET 9 (`System.Threading.RateLimiting`), CERO paquetes
```csharp
options.GlobalLimiter = ...FixedWindow(100 / 1 min)   // por IP
options.AddPolicy("auth", ...FixedWindow(10 / 1 min)) // login + register
```
- --- `[EnableRateLimiting(RateLimitPolicies.Auth)]` en `AuthController`
  - -- Por que dos politicas: el lockout de Identity cuenta fallos POR USUARIO.
       Sin limite por IP se sortea probando contraseñas contra MUCHOS usuarios (password spraying).
- --- `app.UseRateLimiter()` ANTES de auth: rechazar una avalancha es mas barato que validar su token
- --- Detras de proxy hace falta `UseForwardedHeaders` o todos comparten la IP del proxy










## 10. Logging estructurado + Health checks
```sh
dotnet add package Serilog.AspNetCore
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore --version 9.0.9
dotnet add package AspNetCore.HealthChecks.Redis
```

- --- Serilog: mismo `ILogger<T>` en el codigo, pero la salida lleva PROPIEDADES tipadas
```csharp
logger.LogInformation("User {Username} registered with role {Role}", username, Roles.User);
// -> se puede filtrar por Username. Con string interpolado, no.
```
```csharp
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console());
app.UseSerilogRequestLogging();   // 1 linea por request: metodo, ruta, codigo, duracion
```

- --- ⚠️ Serilog NO lee la seccion `Logging` del scaffold. Lee la seccion `Serilog`.
  - -- me comi el fallo: puse los niveles en `Logging` y no hacia NADA
  - -- se quito `Logging` de appsettings para no tener dos fuentes de verdad
```json
"Serilog": { "MinimumLevel": { "Default": "Information", "Override": {
  "Microsoft.AspNetCore": "Warning",
  "Microsoft.EntityFrameworkCore.Database.Command": "Warning",   // sin esto: TODO el SQL en consola
  "ApiEcommerce.Shared.Caching": "Debug"                          // para ver Cache HIT/MISS
}}}
```

- --- Dos sondas, NO una
```
GET /health         liveness   -> ¿el proceso responde?  NO toca base ni Redis
GET /health/ready   readiness  -> ¿SQL Server y Redis responden?
```
  - -- Si la sonda de VIDA dependiera de la base, una caida de la base haria que el
       orquestador reiniciase procesos que estan perfectamente sanos










## 11. Comandos y gotchas del dia a dia
```sh
dotnet build                                       # unico check (no hay tests aun)
dotnet watch run --urls "http://0.0.0.0:8021"      # dev
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update
```

- --- ⚠️ `dotnet ef ... --no-build` usa el ENSAMBLADO YA COMPILADO
  - -- si cambiaste el modelo y no compilaste: `PendingModelChangesWarning` que no tiene sentido
  - -- `dotnet build` primero y vuelve a intentar

- --- ⚠️ `dotnet ef` global es 10.x y el proyecto es EF Core 9.0.9
  - -- funciona, pero es lo primero que hay que mirar si una migracion se comporta raro

- --- Secretos FUERA del repo
```sh
dotnet user-secrets init
dotnet user-secrets set "Jwt:SecretKey" "una-clave-larga-de-minimo-32-caracteres"
dotnet user-secrets set "ConnectionStrings:ConexionSql" "Server=..."
# o por variable de entorno (doble guion bajo = ':')
export Jwt__SecretKey=...
```
  - -- `appsettings.json` va con los valores VACIOS; `appsettings.Development.json` con los de dev

- --- Orden final del pipeline (`Program.cs`) — el orden NO es decorativo
```
UseSerilogRequestLogging -> UseExceptionHandler -> UseStatusCodePages -> Swagger
-> UseHttpsRedirection -> UseStaticFiles -> UseCors -> UseRateLimiter
-> UseAuthentication -> UseAuthorization -> MapControllers -> MapHealthChecks
```
  - -- `UseExceptionHandler` arriba del todo: SOLO captura lo que ocurre DESPUES de el
  - -- `UseStatusCodePages`: convierte los 401/403/404 "vacios" del framework en ProblemDetails
       (los que NO pasan por una excepcion) -> el cliente recibe SIEMPRE el mismo formato de error

- --- Lo que NO se trajo del curso
  - -- **Mapster** (sec 15): `AGENTS/docs` manda y dice AutoMapper. Ademas el curso hizo
       `.TwoWays()` indiscriminado en los DTOs de escritura, que es justo lo que produce
       que un update pise `CreatedAt` y campos que el cliente no deberia poder escribir.
  - -- **`ResponseCaching`** (sec 11): sustituido por Redis (ver cap. 5)
  - -- **Entidad `User` legacy** (sec 8): borrada de raiz, solo `ApplicationUser`










## 12. Refactor del DI: composition root + registro por feature
- --- Punto de partida: UN archivo `ServiceCollectionExtensions.cs` de 342 lineas
  - -- importaba 20 namespaces y conocia las 7 capas a la vez
  - -- el patron (metodos de extension sobre `IServiceCollection`) esta BIEN y es el
       idioma canonico de ASP.NET Core -> es lo que hacen `AddControllers()`, `AddDbContext()`...
  - -- lo que estaba mal era la GRANULARIDAD, no el patron

- --- ⭐ Regla nueva: **cada feature registra lo suyo, en SU carpeta**
```
Data/PersistenceExtensions.cs            AddPersistence(config)
Repository/RepositoryExtensions.cs       AddRepositories()
Mapping/MappingExtensions.cs             AddObjectMapping()
Service/ApplicationServiceExtensions.cs  AddDomainServices()
Service/Auth/AuthExtensions.cs           AddIdentityAndJwt(config)
Shared/Caching/CachingExtensions.cs      AddDistributedCaching(config)
Shared/Storage/StorageExtensions.cs      AddFileStorage(config)
Shared/Http/ApiDocumentationExtensions.cs  AddApiVersioningAndDocs()
Shared/Http/CorsPolicies.cs              AddCorsPolicy(config)     <- const + registro JUNTOS
Shared/Http/RateLimitPolicies.cs         AddRateLimiting()         <- idem
Shared/Http/ErrorHandlingExtensions.cs   AddErrorHandling()
Shared/Http/HealthCheckExtensions.cs     AddHealthProbes(config)
```
  - -- añadir un feature = tocar UNA carpeta, no un archivo compartido que crece sin fin
  - -- cuando el nombre de la politica ya vive en un archivo (`CorsPolicies`), el `Add...`
       va EN ESE archivo: la constante y su registro no deben poder separarse

- --- `Shared/DependencyInjection/ServiceCollectionExtensions.cs` = **composition root**
  - -- NO registra nada. Solo compone, en 3 bloques por capa:
```csharp
builder.Services
    .AddApplication()                          // mapeo + reglas + CRUD + servicios
    .AddInfrastructure(builder.Configuration)  // EF Core, Redis, disco, Identity + JWT
    .AddWebApi(builder.Configuration);         // controllers, versionado, CORS, rate limit, errores, health
```
  - -- los 3 bloques declaran la DIRECCION de dependencias: **Web -> Infrastructure -> Application**
  - -- en proyecto unico eso es CONVENCION, no frontera del compilador (todo se ve con todo)
    - pero son las costuras exactas por donde se parte en `.Api` / `.Infrastructure` / `.Application`
      el dia que haga falta, SIN reescribir el registro

- --- Es el simil de las clases `@Configuration` de Spring Boot: una por concern,
      y una clase raiz que las importa.










## 13. Dos bugs reales que salieron del review del DI
- --- ⚠️ BUG 1: `UseSqlServer` sin `EnableRetryOnFailure`
```csharp
// antes
options.UseSqlServer(configuration.GetConnectionString("ConexionSql"));
// ahora
options.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(
    maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null));
```
  - -- Sin eso, `db.Database.CreateExecutionStrategy()` devuelve una estrategia NO reintentante
  - -- Y `Shared/Db/TransactionalAttribute` la usa -> el `[Transactional]` de `/product/buy`
       estaba escrito para sobrevivir a un corte transitorio y NO sobrevivia a ninguno
  - -- Contra SQL Server en contenedor / gestionado: cada micro-corte de red = 500

  - -- ⚠️ Contrapartida: con estrategia reintentante EF PROHIBE `BeginTransactionAsync`
       fuera de `strategy.ExecuteAsync(...)`
    - lanza `InvalidOperationException: ... does not support user-initiated transactions`
    - el `[Transactional]` ya lo envuelve bien -> `/buy` sigue dando 200 (asi se comprobo)
    - cualquier transaccion NUEVA tiene que hacerlo igual

- --- ⚠️ BUG 2: mismo servicio, dos lifetimes segun la rama
```csharp
// antes
if (redisConfigurado) services.AddScoped<ICacheService, RedisCacheService>();
else                  services.AddSingleton<ICacheService, NoCacheService>();
```
  - -- Hoy no rompia: el unico consumidor (`CachedCategoryService`) es Scoped
  - -- Pero es una MINA: el dia que algo Singleton pida `ICacheService`
    - funciona en la maquina SIN Redis
    - revienta por **captured dependency** en la que SI tiene Redis
    - o sea, falla solo en el entorno que tiene la infra de verdad
  - -- Arreglo: las dos ramas `Singleton` (ninguna guarda estado por request, y las
       dependencias de `RedisCacheService` ya son singletons)
  - -- REGLA: **un servicio se registra con el mismo lifetime en TODAS sus ramas**

- --- Lifetimes del proyecto, resumido
```
Scoped     todo lo que dependa de AppDbContext (repos, servicios, reglas)
Singleton  sin estado por request y solo depende de singletons:
           IJwtTokenService, ICacheService, IFileStorage
```










## 14. `IConfigureOptions<T>`: configurar el framework desde TU config
- --- Antes, dentro de `AddJwtBearer(...)`:
```csharp
var jwt = configuration.GetSection("Jwt").Get<JwtOptions>()!;   // <- bindeo #2
```
  - -- Funcionaba, pero **por coincidencia**: el `!` solo era seguro porque
       `ValidateOnStart` aborta el arranque antes de que ese lambda llegue a correr
  - -- Y era bindear DOS VECES la misma seccion: una validada y otra no = dos fuentes de verdad

- --- Ahora: `Service/Auth/ConfigureJwtBearerOptions.cs`
```csharp
public sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions)
  : IConfigureNamedOptions<JwtBearerOptions>
{
  public void Configure(string? name, JwtBearerOptions options)
  {
    if (name != JwtBearerDefaults.AuthenticationScheme) return;   // no tocar otros esquemas
    Configure(options);
  }
  public void Configure(JwtBearerOptions options) { /* TokenValidationParameters */ }
}
```
```csharp
services.ConfigureOptions<ConfigureJwtBearerOptions>();
services.AddAuthentication(...).AddJwtBearer();   // <- ya sin lambda
```
  - -- La config entra por el CONSTRUCTOR, tipada y validada, como en cualquier servicio
  - -- `IConfigureNamedOptions` (y no `IConfigureOptions`) porque los esquemas de
       autenticacion son opciones CON NOMBRE: hay que filtrar por el esquema propio

- --- Es el mismo patron que ya usaba `Shared/Http/ConfigureSwaggerOptions.cs`
      (un documento por version). Ahora los dos siguen la misma forma -> uniformidad.

- --- REGLA: los servicios reciben **`IOptions<T>`**, NUNCA `IConfiguration`
  - -- excepcion legitima: leer config *eager* EN EL REGISTRO cuando lo que se decide es
       QUE implementacion registrar (`AddDistributedCaching`, `AddHealthProbes`)
    - el grafo de DI se construye UNA vez, ahi no hay nada que recargar

- --- Decorador: hay que registrar tambien el tipo CONCRETO
```csharp
services.AddScoped<CategoryService>();                 // el concreto
services.AddScoped<ICategoryService>(sp => new CachedCategoryService(
    sp.GetRequiredService<CategoryService>(), sp.GetRequiredService<ICacheService>()));
```
  - -- si solo estuviera la interfaz, `inner` no tendria de donde salir sin recursion infinita










## 15. Condiciones de carrera  <- lo que NO se ve probando de uno en uno
- --- Todo lo de abajo pasaba los tests manuales. Solo aparece con peticiones SIMULTANEAS.

- --- ⚠️ CARRERA 1: sobreventa de stock
  - -- `BuyAsync` hacia **read-then-write**:
```
leer stock=1  →  comprobar 1>=1 ok  →  descontar  →  guardar
        ↑ otra peticion hace LO MISMO aqui en medio  ↑
```
  - -- Resultado: dos compras se llevan la MISMA ultima unidad

  - -- Intento 1 (⚠️ MAL para este caso): concurrencia optimista con `[Timestamp] RowVersion`
    - EF mete RowVersion en el WHERE del UPDATE; si otro toco la fila → 0 filas → `DbUpdateConcurrencyException`
    - Con reintentos: **medido, stock=10 y 10 compras simultaneas → 5x200 + 5x409, stock final 5**
    - O sea: NO sobrevende (bien) pero RECHAZA compras validas (mal). 3 reintentos no bastan con contencion alta.

  - -- ✅ Intento 2 (CORRECTO): **UPDATE condicional atomico**
```csharp
var affected = await _db.Products
    .Where(p => p.Id == productId && p.Stock >= quantity)   // condicion DENTRO del UPDATE
    .ExecuteUpdateAsync(s => s
        .SetProperty(p => p.Stock, p => p.Stock - quantity)
        .SetProperty(p => p.UpdatedAt, _ => DateTime.Now), ct);
return affected == 1;   // 0 = no habia stock
```
    - **Medido: stock=10, 15 compras simultaneas → 10x200 + 5x409, stock final 0.** Perfecto.
    - No hay nada que reintentar: la BASE evalua la condicion y descuenta en la misma sentencia

  - -- ⭐ REGLA que me llevo:
```
RowVersion (concurrencia optimista)  ->  EDITAR una entidad (dos admins tocando el mismo producto)
UPDATE condicional atomico           ->  CONTADORES (stock, saldo, cupos)
```
    - Se queda RowVersion en Product igual, pero para el PATCH, no para la compra

  - -- ⚠️ Dos trampas de `ExecuteUpdateAsync`:
    - NO pasa por `SaveChangesAsync` → la auditoria automatica de AppDbContext **no se dispara**
      → hay que poner `UpdatedAt` a mano en el propio SetProperty
    - NO toca el change tracker → la instancia que ya tenias sigue con el valor viejo
      → hay que releer para devolver el estado real

- --- ⚠️ CARRERA 2: categorias duplicadas
  - -- `CategoryRules` comprobaba el nombre ANTES de insertar. Entre la comprobacion y el INSERT cabe otra peticion.
  - -- **Medido: 8 POST simultaneos del mismo nombre → se creaban 8 categorias.**
  - -- ✅ Arreglo: indice unico EN LA BASE. Es la unica garantia real.
```csharp
[Index(nameof(Name), IsUnique = true)]
public class Category : IAuditable
```
  - -- **Medido despues: 8 POST simultaneos → 1x201 + 7x409, una sola fila.**
  - -- La regla aplicativa se queda: da un 409 con mensaje util en el caso normal.
       El indice es la RED que atrapa la carrera.

  - -- ⚠️ Gotcha: `Name` era `nvarchar(max)` y **SQL Server NO puede indexar eso** (limite 900 bytes)
    - Hay que poner `[MaxLength(50)]` en la ENTIDAD (no solo en el DTO)
    - De paso: la BD ahora impone lo mismo que promete el contrato de la API

- --- ⚠️ Y un tercero que aparece al arreglar los otros dos:
  - -- Al poner el indice unico, la carrera pasa de "duplicado silencioso" a **500**
  - -- Hay que traducir la excepcion de EF en `GlobalExceptionHandler`:
```csharp
DbUpdateConcurrencyException => (409, "concurrency_conflict", ...),
DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (409, "conflict", ...),
```
  - -- 2601 / 2627 = violacion de indice unico / restriccion unica en SQL Server
  - -- Sin esto, **arreglar la carrera EMPEORA la respuesta**: el 409 correcto se vuelve 500










## 16. Idempotencia con Redis  (`SET NX`)
- --- Problema que NI RowVersion NI el indice unico cubren:
  - -- el usuario pulsa "Comprar" dos veces
  - -- el movil reintenta porque se cayo la red **despues** de que el server procesara
  - -- para la BASE son dos compras legitimamente distintas. No hay conflicto que detectar.

- --- Solucion: cabecera `Idempotency-Key` + `Shared/Idempotency/`
```
IIdempotencyStore      TryAcquire / Get / Save / Release
RedisIdempotencyStore  <- SET NX sobre StackExchange.Redis
NoIdempotencyStore     <- Null Object si no hay Redis
IdempotentAttribute    <- IAsyncActionFilter, igual forma que [Transactional]
```

- --- ⭐ La primitiva clave: **`SET key value NX EX ttl`** = comprobar y reservar en UNA operacion
```csharp
await redis.GetDatabase().StringSetAsync(prefix + key, "__in_progress__", ttl, When.NotExists);
```
  - -- Un `GET` y luego un `SET` seria read-then-write OTRA VEZ y dos peticiones pasarian las dos
  - -- ⚠️ Por eso aqui se usa `IConnectionMultiplexer` y NO `IDistributedCache`:
       esa abstraccion solo tiene Get/Set/Remove, **no tiene "set si no existe"**

- --- La clave se compone con **usuario + metodo + ruta + clave del cliente**
  - -- Sin el usuario, dos clientes con el mismo GUID se pisan
  - -- y peor: uno recibe la RESPUESTA del otro = fuga de datos entre cuentas

- --- Flujo del filtro
```
1. sin cabecera        -> no hace nada (la idempotencia la pide el CLIENTE, que sabe si reintenta)
2. respuesta guardada  -> la reproduce + header `Idempotency-Replayed: true`
3. reserva fallida     -> 409 (hay otra igual EN CURSO ahora mismo)
4. ejecuta
5. exito (2xx)         -> guarda la respuesta 24h
6. error               -> RELEASE, para que el cliente pueda reintentar de verdad
```
  - -- ⚠️ El paso 6 importa: memorizar un error convertiria un fallo transitorio en permanente 24h

- --- Medido: 5 compras de 2 uds con la MISMA clave, stock 10 → **stock final 8** (1 ejecuta, 4 reproducen)
  - -- control sin cabecera: 3 compras de 2 uds → stock 2. Descuenta las 3, correcto.

```sh
curl -X POST localhost:8021/api/v1/product/buy \
  -H "Idempotency-Key: $(uuidgen)" -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"sku":"X-1","quantity":2}'
```










## 17. Outbox transaccional  <- el patron clave de mensajeria
- --- ⚠️ El problema: **no se puede escribir en la BD y en el broker atomicamente**
```
publicar y luego commit  -> si falla el commit, anunciaste una compra que NO existe
commit y luego publicar  -> si falla la publicacion, la compra existe y NADIE se entera
```

- --- ⭐ Solucion: el evento es UNA FILA MAS, escrita en la MISMA transaccion
```
[Transactional] abre transaccion
  ├─ ExecuteUpdateAsync   (descuenta stock)
  ├─ OutboxMessages.Add   (el evento)
  └─ SaveChangesAsync     -> commit: o pasan las dos cosas o no pasa ninguna
                             luego, un BackgroundService lo publica
```
  - -- `IEventOutbox.EnqueueAsync` **NO hace SaveChanges** a proposito: manda la transaccion de negocio

- --- ⭐ Consecuencia practica: **la API funciona con RabbitMQ CAIDO**
  - -- Medido: broker apagado → 3 compras → **200, 200, 200**, stock 7→4, eventos en la tabla
  - -- el publicador reintenta: `Failed to publish ... (attempt 1/5)`
  - -- cuando el broker vuelve, se drenan solos

- --- ⚠️ Orden de las operaciones en el publicador: **publicar → marcar procesado**
  - -- al reves se PIERDEN mensajes si el proceso muere en medio
  - -- asi, como mucho, se publica dos veces = **at-least-once**
  - -- por eso el consumidor TIENE que deduplicar (cap. 18)

- --- Indice filtrado: el publicador solo busca pendientes
```csharp
modelBuilder.Entity<OutboxMessage>()
    .HasIndex(m => m.OccurredAt)
    .HasFilter("[ProcessedAt] IS NULL");
```
  - -- la tabla crece sin parar; indexar TODAS las filas seria cada vez mas caro










## 18. RabbitMQ: publicar y consumir bien
```sh
dotnet add package RabbitMQ.Client   # 7.2.2, API async
```

- --- Topologia (se declara sola al conectar, es idempotente)
```
exchange `apiecommerce.events` (topic, durable)
   └── routing key `product.purchased`
        └── queue `apiecommerce.product-purchased` (durable)
             └── x-dead-letter-exchange -> `apiecommerce.events.dlx` -> `...dlq`
```
  - -- `topic` para que un consumidor se suscriba a `product.*` sin que el publicador sepa quien escucha
  - -- **DLQ obligatoria**: sin ella un mensaje envenenado se reencola PARA SIEMPRE y bloquea la cola

- --- ⚠️ Conexiones vs canales
```
1 conexion TCP por PROCESO   (el handshake AMQP es caro, el broker tiene limite)
N canales, uno por componente (son baratos, pero NO son thread-safe)
```
  - -- Abrir una conexion por mensaje es EL error clasico con RabbitMQ

- --- Publicador
  - -- `publisherConfirmations: true` → `BasicPublishAsync` no vuelve hasta el ack del broker
    - sin eso "publicado" solo significa "escrito en un socket", y el outbox marcaria como
      enviado algo que el broker nunca recibio
  - -- `DeliveryMode.Persistent` → el mensaje sobrevive a un reinicio del broker
    - cola durable + mensaje transitorio = la cola sobrevive VACIA, lo peor de los dos
  - -- `MessageId` = el Id del outbox → es lo que usa el consumidor para deduplicar

- --- Consumidor: las 4 cosas que hay que hacer bien
```
1. autoAck: FALSE      -> confirmamos nosotros, DESPUES de procesar
                          (con autoAck true, un fallo al procesar PIERDE el mensaje)
2. idempotencia        -> tabla ProcessedMessages con el MessageId como PK
3. reintentos acotados -> args.Redelivered distingue 1er intento de reintento; al 2o fallo -> DLQ
4. BasicQos(prefetch)  -> sin esto RabbitMQ empuja la cola entera a la primera replica
                          y las demas quedan ociosas
```
  - -- La PK de `ProcessedMessages` es lo que de verdad impide el duplicado:
       si dos replicas procesan el mismo mensaje a la vez, una revienta al insertar
       → `catch (DbUpdateException)` → ack. **Eso es la deduplicacion funcionando, no un error.**

- --- ⚠️ Un `BackgroundService` que lanza SE MUERE y no vuelve hasta reiniciar el proceso
  - -- el bucle va envuelto en try/catch y siempre reintenta

- --- ⚠️ Logging de condiciones esperadas
  - -- el broker caido loguea CADA 5s. Con la traza completa, el log se inunda
       justo cuando hace falta leerlo
  - -- → mensaje corto en los intentos intermedios, traza completa solo al agotar reintentos










## 19. Docker
- --- `Dockerfile` multi-stage
```dockerfile
FROM ...sdk:9.0 AS build      # compila
COPY ApiEcommerce.csproj .    # <- SOLO el csproj primero
RUN dotnet restore            #    asi Docker cachea el restore si no cambian las deps
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM ...aspnet:9.0 AS runtime # ~110 MB en vez de ~800 MB del sdk
RUN adduser --system --group --no-create-home appuser
USER appuser                  # root dentro del contenedor = root del host si hay escape
```

- --- `.dockerignore` no es cosmetico: evita que `bin/`, `.git/` y
      `appsettings.Development.json` (con secretos) entren en la imagen

- --- ⚠️ Las imagenes subidas van en VOLUMEN
  - -- sin eso, cada redespliegue del contenedor borra las fotos de los productos

- --- Config por variables de entorno: **`__` es el separador de `:`**
```yaml
ConnectionStrings__ConexionSql: "Server=sqlserver_ecommerce,1433;..."
Redis__Configuration: "redis_generic:6379"
RabbitMq__ConnectionString: "amqp://guest:guest@rabbitmq_generic:5672"
Cors__AllowedOrigins__0: "http://localhost:3000"    # arrays con indice
Seed__Enabled: "false"                              # NUNCA sembrar admin fuera de dev
```
  - -- dentro de la red de compose los hosts son los NOMBRES de servicio, no 172.17.0.1

- --- `docker-compose.fragment.yml` en la raiz: los bloques para pegar en el compose central
  - -- lo unico que falta ahi es `rabbitmq_generic` (5672 + 15672 para la UI)










## 20. Lo que encontro la revision multiagente  <- los bugs que YO no vi
- --- 3 agentes con /dotnet-best-practices sobre concurrencia, mensajeria e infra.
      Todo lo de abajo estaba escrito por mi y compilaba y pasaba el smoke test.

- --- ⚠️⚠️ P0: la app **CRASHEABA AL ARRANCAR EN PRODUCTION**
  - -- `DataSeeder` leia `IOptions<SeedOptions>.Value` ANTES de mirar `options.Enabled`
  - -- `.Value` dispara `ValidateDataAnnotations()`, y `AdminPassword` era `[Required]`
  - -- Con `Seed__Enabled=false` (lo normal en prod, sin password definida) -> `OptionsValidationException`
       -> con `restart: unless-stopped` = **crash-loop infinito**
  - -- ⭐ LECCION: **una regla condicional NO se expresa con un atributo**
```csharp
services.AddOptions<SeedOptions>()
    .Bind(...)
    .Validate(o => !o.Enabled || o.AdminPassword.Length >= 8,   // <- condicional
              "Seed:AdminPassword is required when Seed:Enabled is true")
    .ValidateOnStart();
```

- --- ⚠️ P0: **nadie aplicaba las migraciones**
  - -- 8 migraciones en `Migrations/` y ni un `Migrate()` en el codigo
  - -- La imagen runtime (`aspnet`) NO lleva SDK ni `dotnet-ef` -> no puede aplicarlas
  - -- Y lo peor: `/health/ready` respondia **Healthy** igual, porque `AddDbContextCheck`
       solo comprueba que se puede CONECTAR, no el esquema
    - o sea: el orquestador mandaba trafico a un servicio que devolvia 500 en todo
  - -- Arreglo: `await db.Database.MigrateAsync()` en el scope de arranque

- --- ⚠️ P0: el `HEALTHCHECK` del Dockerfile **nunca podia funcionar**
  - -- `curl` NO viene en `mcr.microsoft.com/dotnet/aspnet:9.0`
    - su base `runtime-deps` solo instala ca-certificates, libc6, libgcc, libicu, libssl, tzdata
  - -- `curl: not found` -> exit 127 -> contenedor **`unhealthy` PARA SIEMPRE**
  - -- y eso bloquea cualquier `depends_on: condition: service_healthy` que apunte a el
  - -- ⭐ LECCION: no asumas que un binario esta en una imagen slim. Verificalo.

- --- ⚠️ P0: `RedisIdempotencyStore` fallaba **EN CERRADO** (sin un solo try/catch)
  - -- Escenario: commit OK (stock descontado) -> Redis se cae -> `SaveAsync` lanza
       -> 500 al cliente por una compra QUE SI SE HIZO
  - -- Y al reintentar: la clave seguia reservada -> 409 durante 24h
  - -- **El mecanismo que existe para evitar el doble cobro era el que lo provocaba**
  - -- ⭐ LECCION: si un componente es una OPTIMIZACION, tiene que fallar en abierto.
       Y la decision debe ser la MISMA en todas sus implementaciones
       (`RedisCacheService` ya fallaba en abierto; este no. Incoherencia silenciosa.)

- --- ⚠️ P0: un solo TTL para dos cosas distintas
  - -- La RESERVA (`__in_progress__`) usaba el mismo TTL de 24h que la RESPUESTA
  - -- Si el proceso muere entre reservar y guardar (deploy, OOM-kill):
       clave bloqueada 24h por una operacion **que nunca se ejecuto**
  - -- ⭐ Arreglo: dos TTL. Reserva = 60s (timeout de request). Respuesta = 24h.

- --- ⚠️ P0: el handler no cubria el caso que introduce `ExecuteUpdateAsync`
```
SaveChangesAsync    -> DbUpdateException { SqlException }   <- el patron acertaba
ExecuteUpdateAsync  -> SqlException DESNUDO                 <- caia en `_` -> 500
```
  - -- `ExecuteUpdate/Delete` NO pasan por `SaveChanges`, asi que no hay envoltorio
  - -- ⭐ LECCION: **no hagas pattern matching sobre la FORMA del anidamiento.**
       Recorre la cadena de `InnerException`:
```csharp
static SqlException? FindSqlException(Exception? e) {
  for (; e is not null; e = e.InnerException) if (e is SqlException s) return s;
  return null;
}
_ when FindSqlException(ex) is { Number: 2601 or 2627 } => 409 conflict,
_ when FindSqlException(ex) is { Number: 1205 }         => 409 deadlock,
_ when FindSqlException(ex) is { Number: 547 }          => 409 fk_violation,
```

- --- ⚠️⚠️ P0: `[Transactional]` + estrategia reintentante = **ejecuta la accion DOS VECES**
  - -- Lo encontraron DOS agentes por separado. Es el mas profundo.
  - -- `EnableRetryOnFailure` obliga a meter la transaccion en `strategy.ExecuteAsync(...)`
  - -- ...y esa estrategia **REEJECUTA el delegado** ante un fallo transitorio
  - -- pero `ActionExecutionDelegate` (el `next()` de un filtro) **NO es reentrante**
  - -- Resultado: commit falla transitoriamente -> reintento -> `next()` otra vez
       -> doble descuento de stock y dos eventos, o commit vacio con 200 al cliente
  - -- ⭐ Arreglo: la transaccion baja al SERVICIO, con una unidad REPLAYABLE
```csharp
// Shared/Db/ITransactionRunner.cs  <- el servicio no ve AppDbContext
return await _tx.ExecuteAsync(async token => {
    var product = await _repository.GetBySkuAsync(dto.SKU, token);   // RELEE todo
    ...
}, ct);
```
```csharp
// dentro del runner, en CADA intento:
db.ChangeTracker.Clear();   // sin esto el reintento NO es equivalente:
                            // las entidades del intento 1 quedaron `Unchanged`
                            // -> el 2o intento hace commit de una transaccion VACIA
```
  - -- Y `[Transactional]` se queda con una **guarda de reentrada** que lanza si se
       reintenta: convertir corrupcion silenciosa en error ruidoso
  - -- ⭐ LECCION GRANDE: **la transaccion es politica de NEGOCIO, no de HTTP.**
       Un atributo de controller no puede darte una unidad de trabajo reintentable.

- --- ⚠️ La config de .NET **FUSIONA arrays, no los reemplaza**
  - -- `Cors__AllowedOrigins__0=https://prod.com` NO borra los indices 1 y 2 de appsettings.json
  - -- Resultado medido: en produccion seguian permitidos `localhost:4200` y `localhost:5173`
  - -- ⭐ Arreglo: lista VACIA en `appsettings.json` + filtrar vacios al leer

- --- ⚠️ Sin `UseForwardedHeaders`, detras de un proxy:
  - -- el rate limiter particiona por la IP DEL PROXY = **un solo cubo de 100 req/min para todo internet**
  - -- los logs registran esa misma IP para todo el mundo
  - -- `UseHttpsRedirection` es un no-op (avisa `Failed to determine the https port`)

- --- ⚠️ `UseStaticFiles` estaba antes de CORS y del rate limiter
  - -- es TERMINAL para los archivos que sirve -> las imagenes no pasaban por el limitador
       (descarga en bucle sin cuota) ni recibian cabeceras CORS

- --- ⚠️ Paquetes: `Serilog.AspNetCore 10.0.0` y `StackExchange.Redis 3.x` metian
      ~12 paquetes **10.x** en una app `net9.0`, sombreando el shared framework
  - -- `AspNetCore.HealthChecks.Redis 9.0.0` esta compilado contra SE.Redis **2.7**
       -> NuGet unificaba a 3.x -> `MissingMethodException` en RUNTIME, no al compilar
  - -- ⭐ Arreglo: fijar `StackExchange.Redis 2.8.x` y `Serilog.AspNetCore 9.x`

- --- ⚠️ `ExecuteUpdateAsync` con `DateTime.Now` dentro del arbol de expresion
  - -- EF NO lo evalua en cliente: lo traduce a **`GETDATE()`** = reloj del SERVIDOR SQL
  - -- El resto del proyecto estampa con el reloj del PROCESO
  - -- ⭐ Arreglo: capturar `var now = DateTime.Now;` FUERA y usar la variable

- --- Otros arreglados: dedup del consumidor marcaba DESPUES del efecto (podia aplicarlo 2 veces);
      `catch (DbUpdateException)` a secas se tragaba deadlocks y hacia ack (perdia mensajes);
      `RabbitMqConnection` publicaba el campo antes de declarar la topologia (3 bugs en 4 lineas);
      `OperationCanceledException` excluida del catch mataba el `BackgroundService` **y el host**;
      el replay de idempotencia perdia el header `Location`; `InvalidOperationException => 409`
      filtraba mensajes internos de EF al cliente.

- --- ⭐⭐ LA LECCION DE TODO ESTO
```
Compilar + pasar un smoke test manual NO es evidencia de correctitud.
Casi todos estos bugs solo aparecen con: concurrencia, fallo de una dependencia,
reinicio a mitad, o el entorno de PRODUCCION.
Por eso el paso 7 del roadmap (tests) es el siguiente y no es opcional.
```










## 21. Vertical slicing por contexto acotado
- --- Punto de partida: organizacion por CAPA TECNICA
```
Controllers/   los 4 controllers de todo el proyecto
Service/       ICategoryService, CategoryService, IProductService, ProductService,
               CategoryRules, ProductRules, IAuthService, AuthService... TODO junto
Repository/    todos los repositorios
Models/        todas las entidades + Dtos/ con TODOS los DTOs
```
  - -- Tocar UNA feature = abrir 5 carpetas distintas
  - -- Y cada carpeta crece con cada entidad nueva, sin techo

- --- ⭐ Ahora: por DOMINIO, con las carpetas de capa DENTRO de cada slice
```
Features/
  Catalog/                      <- contexto acotado
    Models/       Category.cs  Product.cs
    Dtos/         CategoryDto, CreateProductDto, BuyProductDto...
    Repository/   ICategoryRepository + impl, IProductRepository + impl
    Service/      ICategoryService + impl, CategoryRules, ProductService...
    Mapping/      CategoryProfile, ProductProfile
    Controllers/  CategoryController, ProductController
    CatalogExtensions.cs        <- AddCatalogFeature(): el DI del slice
  Accounts/                     <- identidad, JWT, autorizacion
    Models/ Dtos/ Service/ Controllers/ + AccountsExtensions.cs
Shared/                         <- transversal, de NINGUN dominio
  Persistence/ Crud/ Caching/ Db/ Http/ Idempotency/ Messaging/ Paging/ Storage/ Mapping/
  DependencyInjection/          <- composition root, NO registra nada
```

- --- ⚠️⚠️ LA REGLA QUE MAS IMPORTA: **un slice es un CONTEXTO ACOTADO, no una entidad**
  - -- `UnitOfMeasurement` NO es una feature. `ProductTag` NO es una feature. `Brand` tampoco.
  - -- Todas son parte del vocabulario del CATALOGO -> viven dentro de `Catalog/`
  - -- La pregunta es de DDD:
```
¿esto tiene su propio lenguaje ubicuo y sus propias invariantes,
 o es parte del vocabulario de otro contexto?
```
  - -- Un slice POR ENTIDAD reproduce exactamente la dispersion que el slicing venia a
       quitar, solo que con mas carpetas. Es el error clasico al descubrir vertical slicing.
  - -- Contextos previstos segun crezca: `Catalog`, `Accounts`, `Ordering`, `Payments`, `Shipping`

- --- Que se queda en `Shared/` y que baja al slice
```
Shared/   lo que NO pertenece a ningun dominio y usan todos:
          BaseRepository<T>, ICrudService, cache, storage, mensajeria, paginacion
Slice/    lo que habla el lenguaje de ESE dominio:
          sus entidades, DTOs, repos concretos, reglas, servicios, controllers
```
  - -- Los **genericos abiertos** (`IBaseRepository<>`, `NoEntityRules<,,>`) son mecanismo
       transversal -> `Shared/`, registrados UNA vez
  - -- Los **registros cerrados** por entidad -> en el `AddXxxFeature()` de su slice

- --- Composition root: 3 bloques que declaran la DIRECCION de dependencias
```csharp
builder.Services
    .AddSharedInfrastructure(builder.Configuration)  // Web -> Features -> Shared
    .AddFeatures(builder.Configuration)
    .AddWebApi(builder.Configuration);
```
  - -- **Añadir un slice = crear su carpeta + UNA linea en `AddFeatures()`**
  - -- En proyecto unico esa direccion es CONVENCION, no frontera del compilador.
       Pero son las costuras por donde se parte en proyectos sin reescribir nada.

- --- ⚠️ Trampas del refactor (me pasaron las dos)
  - -- Un script que inserta `using` buscando "la ultima linea que empieza por using"
       los mete DENTRO del bloque comentado de aprendizaje del final del archivo
    - hay que acotar la insercion a la cabecera, ANTES del `namespace`
  - -- `pkill -f 'ApiEcommerce'` **se mata a si mismo**: el cwd contiene esa cadena y por
       tanto tambien la linea de comando del propio shell

- --- ⚠️ Al borrar `RepositoryExtensions.cs` y `ApplicationServiceExtensions.cs` se fueron
      con ellos los registros de los **genericos abiertos** -> la app compilaba pero
      reventaba al ARRANCAR
  - -- Lo cazo la validacion del contenedor en Development (`ValidateOnBuild`), no el compilador
  - -- Buena señal de que esa validacion vale: el error dice exactamente que no se podia
       resolver `IBaseRepository<Category>` al activar `CrudService<...>`

- --- Verificado tras el refactor: smoke test completo + las 3 pruebas de concurrencia con
      resultados IDENTICOS (10x200/5x409 stock 0 · 1x201/7x409 una fila · idempotencia 1 compra)






## 22. Compose de la app vs compose de la infraestructura
- --- Dos ficheros, y la division tiene un porque: la infra (SQL Server, Redis, RabbitMQ)
      la comparto con OTROS proyectos y vive en `~/Documents/code/000_infra`. Un segundo
      compose que la redeclarara pelearia por los puertos 1434/6999/5672 con el que ya la tiene.
  - -- `docker-compose.fragment.yml` -> bloques para PEGAR alla. Hoy solo falta `rabbitmq_generic`.
  - -- `docker-compose.prod.yml` -> SOLO la API, enganchada a la red de alla como **externa**.

- --- ⚠️ La red externa lleva el PREFIJO del proyecto compose
```yaml
networks:
  backend:
    external: true
    name: ${INFRA_NETWORK:-000_infra_backend}   # NO "backend" a secas
```
  - -- compose prefija con el nombre del proyecto, que por defecto es el nombre de la
       CARPETA: `backend` declarada en `000_infra/` se llama en realidad `000_infra_backend`
  - -- comprobarlo siempre con `docker network ls | grep backend`; por eso va parametrizado

- --- ⚠️ `depends_on` **no cruza ficheros compose**
  - -- solo ordena servicios del MISMO fichero. Con la infra fuera, ese arranque ordenado
       no existe y hay que darlo desde la app
  - -- aqui ya lo esta: `MigrateAsync` reintenta por `EnableRetryOnFailure`, y si aun asi
       muere, `restart: unless-stopped` la vuelve a levantar

- --- ⚠️ Dos formas de direccionar la MISMA base, segun quien pregunte
  - -- desde el **dev container** (fuera de la red `backend`): `172.17.0.1` + puerto
       **publicado** -> `172.17.0.1,1434`, `172.17.0.1:6999`, `172.17.0.1:5672`
  - -- desde **dentro** de la red: nombre de servicio + puerto **interno** ->
       `sqlserver_ecommerce,1433`, `redis_generic:6379`, `rabbitmq_generic:5672`
  - -- confundirlos da un timeout de conexion que parece de red y es de puerto

- --- `${VAR:?mensaje}` en el compose **aborta el `up`** si la variable no esta definida
  - -- lo contrario (`${VAR:-default}`) arrancaria con un secreto de ejemplo
  - -- importa aqui porque `JwtOptions` usa `ValidateOnStart`: una clave vacia tumba el
       proceso igual, pero con un stack trace en vez de una linea que dice que falta
  - -- `.env.example` commiteado (plantilla), `.env` gitignorado (valores)

- --- ⚠️ **No hay docker dentro del dev container**
  - -- `docker ps` no existe ahi; ni el `Dockerfile` ni estos composes se pueden construir
       ni levantar desde dentro. Se ejecutan en el HOST
  - -- por eso el camino real del broker sigue sin ejercitarse: es el unico ⚠️ del slice 09






## 23. El bug que solo se ve con el broker DE VERDAD
- --- Contexto: el outbox llevaba semanas "verificado" sin que RabbitMQ existiera.
      En cuanto el broker existio, el camino feliz funciono a la primera... y el
      camino de la CAIDA resulto estar roto desde el principio.

- --- ⚠️ **`Attempts` contaba dos cosas distintas como si fueran una**
  - -- "este mensaje falla" (payload malo, sin cola destino) -> SI es culpa del mensaje
  - -- "el broker esta caido" -> NO es culpa del mensaje: le pasa igual a todos
  - -- las dos llegaban como `InvalidOperationException("RabbitMQ is not available.")`
       y las dos incrementaban `Attempts`

- --- Lo MEDIDO (que es lo que convierte una sospecha en un bug)
```sh
# app apuntando a un puerto muerto = broker caido, sin tocar la infraestructura
RabbitMq__ConnectionString="amqp://guest:guest@172.17.0.1:5673" dotnet bin/Debug/net9.0/ApiEcommerce.dll
```
```
23:57:09 WRN Failed to publish ... (attempt 1/5)
23:57:29 ERR Giving up on ... after 5 attempts
>>> ABANDONADO tras 25 segundos de broker caido
```
  - -- 5 intentos x 5 s = **25 segundos** de caida y el evento queda con `Attempts=5`
  - -- el publicador filtra por `Attempts < MaxAttempts`, asi que **no vuelve a mirarlo
       jamas**: al volver el broker, sigue enterrado
  - -- comprobado: broker arriba + 12 s -> `ProcessedAt` seguia en NULL
  - -- 25 s es MENOS que el `start_period: 30s` del healthcheck del propio contenedor de
       RabbitMQ: **un reinicio rutinario del broker perdia eventos**

- --- El fix: una excepcion propia para distinguir las dos cosas
```csharp
catch (BrokerUnavailableException ex)   // infraestructura caida
{
  // NO toca Attempts, NO guarda. Corta la tanda y reintenta en la vuelta siguiente.
  return;
}
catch (Exception ex) ...                // culpa del mensaje
{
  message.Attempts++;
  continue;   // <- `continue`, no `break`: el broker esta vivo y un mensaje envenenado
              //    no debe bloquear la cabecera de la tanda (head-of-line blocking)
}
```
  - -- verificado: **45 s de caida (9 vueltas) -> `Attempts` sigue en 0**, y al volver el
       broker se publica y se consume

- --- De paso: `MaxAttempts` estaba **duplicado** como `const` en el publicador y en la
      sonda de salud, con un comentario que pedia "mantenerlos sincronizados"
  - -- eso no es un acuerdo, es una bomba de relojeria: subir uno deja al otro contando
       como perdidos mensajes que aun se reintentan
  - -- ahora es `RabbitMq:MaxPublishAttempts` en las options, una sola fuente

- --- --- Lo que si funciono a la primera contra el broker real
  - -- los 13 eventos acumulados durante semanas se drenaron solos al arrancar
  - -- deduplicacion: mismo `MessageId` publicado dos veces -> `Duplicate ... ignored`
  - -- DLQ: `type` inesperado -> nack sin reencolar, y el mensaje aparece en la DLQ
       **sin haberse deserializado** (el chequeo del tipo va ANTES del deserializado)
```sh
# publicar a mano para forzar los caminos raros, sin tocar el codigo
curl -u guest:guest -X POST http://172.17.0.1:15672/api/exchanges/%2F/apiecommerce.events/publish \
  -H 'Content-Type: application/json' \
  -d '{"properties":{"message_id":"<guid>","type":"product.created"},"routing_key":"product.purchased","payload":"{...}","payload_encoding":"string"}'
```

- --- ⚠️ El dev container ya solo trae **.NET 10** y el proyecto es `net9.0`
  - -- `dotnet build` va (el SDK 10 compila para net9.0), pero `dotnet run` **no arranca**:
       "You must install or update .NET... The following frameworks were found: 10.0.11"
  - -- parche para seguir trabajando: `DOTNET_ROLL_FORWARD=Major`
  - -- ojo con lo que eso implica: se esta probando sobre el runtime **10**, no sobre el
       **9** que usa el `Dockerfile` (`aspnet:9.0`). Hay que decidir: instalar el runtime 9
       o migrar el proyecto a net10.0
