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

- --- ⭐ EL FLUJO COMPLETO, de la peticion HTTP al ack (esto es el mapa; lo de abajo, el detalle)
```
POST /api/v1/product/buy
   |
   |  (1) UNA SOLA transaccion de negocio (cap. 17)
   |      descuenta stock  +  INSERT en OutboxMessages
   v
[ SQL Server ]  ---> 200 al cliente AQUI. La compra ya esta confirmada.
   |                 El broker todavia no sabe nada, y da igual: si esta caido, la
   |                 compra igual se completa y el evento espera en la tabla.
   |
   |  (2) OutboxPublisher (BackgroundService, cada RabbitMq:PublishIntervalSeconds = 5s)
   |      SELECT ... WHERE ProcessedAt IS NULL AND Attempts < MaxPublishAttempts
   v
+----------------------------------------------------------+
|  exchange  apiecommerce.events        (topic, durable)    |   <- AQUI publica la app
+----------------------------------------------------------+
   |   routing key = "product.purchased"   (= ProductPurchased.EventType)
   |
   |  (3) binding: la cola se suscribio al patron "product.purchased"
   v
+----------------------------------------------------------+
|  queue  apiecommerce.product-purchased  (durable)         |
|         x-dead-letter-exchange = apiecommerce.events.dlx  |
+----------------------------------------------------------+
   |
   |  (4) ProductPurchasedConsumer: prefetch 10, autoAck FALSE
   |      comprueba Type -> deduplica por MessageId -> aplica efecto
   v
  ack  (OK)                       nack sin requeue  (mensaje malo)
                                          |
                                          v
                        exchange  apiecommerce.events.dlx   (fanout)
                                          |
                                          v
                        queue  apiecommerce.product-purchased.dlq
```

- --- ⚠️ En la UI (`:15672` -> Exchanges) veras **9 exchanges y 7 NO son tuyos**
  - -- `(AMQP default)` y los `amq.*` (direct, fanout, headers, match, topic, rabbitmq.trace)
       los crea RabbitMQ solo en CADA vhost. Estan siempre. Ignoralos
  - -- los tuyos son exactamente dos: `apiecommerce.events` (topic) y `apiecommerce.events.dlx` (fanout)
  - -- **el correcto para publicar es `apiecommerce.events`**; el `.dlx` no se publica a mano nunca,
       solo recibe lo que el consumidor rechaza

- --- Glosario: que es cada termino y **donde vive EN ESTE REPO**  (el porque, cap. 24)
  - -- **vhost** — namespace del broker: exchanges y colas viven dentro de uno. Aqui el
       default, `/`. En la HTTP API va URL-encoded como `%2F`, de ahi las URLs raras del curl
  - -- **exchange** — donde PUBLICAS. Recibe el mensaje, decide a que colas va y se olvida.
       Aqui `apiecommerce.events` (`RabbitMq:Exchange`)
  - -- **routing key** — la etiqueta que lleva el mensaje al publicarse, y lo unico que el
       exchange mira para decidir. Aqui `"product.purchased"`
  - -- **binding** — la regla que une cola y exchange: *"mandame lo que case con este patron"*.
       La declara la COLA, no el publicador. Aqui `RabbitMq:RoutingKey`
  - -- **queue** — el buzon, y **lo unico que guarda mensajes**. Aqui
       `apiecommerce.product-purchased` (`RabbitMq:Queue`)
  - -- **tipos de exchange** — como se compara la routing key con el binding:
```
direct   igualdad exacta
topic    patron:  *  = una palabra,  #  = varias      -> `product.*` casa `product.purchased`
fanout   a TODAS las colas atadas, ignora la clave
headers  por cabeceras en vez de por clave            (no se usa aqui)
```
  - -- **DLX / DLQ** (dead letter) — el exchange y la cola a donde cae lo que el consumidor
       rechaza. Aqui `apiecommerce.events.dlx` -> `apiecommerce.product-purchased.dlq`.
       No se publica ahi a mano nunca: el broker lo hace por el `x-dead-letter-exchange` de la cola
  - -- **publisher confirm** — el ack **del broker al publicador**: "lo tengo yo". Distinto
       del ack del consumidor de abajo: son dos confirmaciones en extremos opuestos
  - -- **ack / nack** — el ack **del consumidor al broker**: "procesado, borralo". `nack` con
       `requeue: false` es lo que dispara el dead-lettering
  - -- **prefetch (QoS)** — cuantos mensajes sin confirmar te entrega el broker a la vez.
       Aqui 10 (`RabbitMq:PrefetchCount`)
  - -- **connection / channel** — una conexion TCP por proceso, N canales dentro (ver abajo)
  - -- **mandatory** — al publicar: "si no hay ninguna cola destino, **devuelvemelo**" en vez
       de descartarlo en silencio
  - -- ⚠️ **durable ≠ persistent**, y hacen falta las DOS
```
durable     -> propiedad del EXCHANGE y de la COLA: sobreviven al reinicio del broker
persistent  -> propiedad del MENSAJE (DeliveryMode): se escribe a disco
```
  - -- **propiedades del mensaje** — cabeceras AMQP que viajan aparte del cuerpo. Aqui dos
       llevan peso: `MessageId` (el Id del outbox) y `Type` (el nombre del evento)

- --- ⭐ **El PORQUE de cada eleccion esta en el cap. 24**: por que el publicador nunca
      conoce la cola, por que `topic` y no `direct`, por que el DLX es `fanout`, las tres
      formas de caer en la DLQ, y por que RabbitMQ y no Kafka

- --- Topologia (se declara sola al conectar, es idempotente)
  - -- declarar algo que ya existe con los mismos parametros no hace nada; declararlo con
       parametros DISTINTOS da error 406 y cierra el canal
  - -- todo `durable` y los mensajes persistentes: si no, reiniciar el broker se lleva la cola
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


- --- ⭐ La pregunta que decide DONDE va un archivo: **¿mecanismo o vocabulario?**
  - -- lo aprendi con `ProductPurchased`, que estaba en `Shared/Messaging/Events/`
       junto a `IDomainEvent`, y no cuadraba
```
IDomainEvent        -> CONTRATO, no habla de ningun dominio        -> Shared/Messaging/
ProductPurchased    -> habla de SKU, stock, producto = catalogo    -> Features/Catalog/Events/
ProductPurchasedConsumer -> QUIEN reacciona a un evento del catalogo -> Features/Catalog/Messaging/
```
  - -- ⭐ y el argumento que zanja la duda no es la simetria, es la **DIRECCION DE
       DEPENDENCIAS**: con el evento y el consumidor en `Shared/`, `Shared` nombraba tipos
       de `Catalog`. La direccion declarada es **Web -> Features -> Shared**, o sea al reves
  - -- la costura para arreglarlo sin que `Shared` conozca al consumidor:
```csharp
// Shared/Messaging: el mecanismo y la POLITICA de "hay broker", una sola vez
public static IServiceCollection AddEventConsumer<TConsumer>(this IServiceCollection, IConfiguration)
    where TConsumer : class, IHostedService

// Features/Catalog/CatalogExtensions.cs: el slice registra el SUYO
services.AddEventConsumer<ProductPurchasedConsumer>(configuration);
```

- --- ⚠️ Al buscar mas fugas aparecio algo mas tonto: **`using` MUERTOS**
  - -- 7 archivos de `Shared/` tenian `using ApiEcommerce.Features.Catalog...` que **no
       usaba nadie**: los dejo el refactor a vertical slicing al mover archivos
  - -- un using sin usar **no da warning**, asi que parecia que media `Shared/` dependia de
       `Catalog` cuando no era verdad. Ruido que hace ilegible la dependencia REAL
  - -- comprobarlo es trivial y no hay que fiarse de leer:
```sh
sed -i '/^using ApiEcommerce.Features.Catalog/d' <archivos> && dotnet build
```
  - -- la unica dependencia REAL era el escaneo de AutoMapper
       (`typeof(CategoryProfile).Assembly`). Se **invirtio**: el ensamblado lo pasa ahora el
       composition root, que es el unico sitio de `Shared/` que puede conocer ambos lados
  - -- ⚠️ `Shared/Mapping/MappingProfile.cs` conserva los suyos a proposito: es el archivo
       LEGACY comentado, y no se toca





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

- --- ⚠️ El dev container ya solo traia **.NET 10** y el proyecto es `net9.0`
  - -- `dotnet build` va (el SDK 10 compila para net9.0), pero `dotnet run` **no arranca**:
       "You must install or update .NET... The following frameworks were found: 10.0.11"
  - -- parche de emergencia: `DOTNET_ROLL_FORWARD=Major`. Pero entonces estas probando
       sobre el runtime **10** y el `Dockerfile` despliega sobre el **9**: no vale como
       verificacion

- --- La solucion: **los runtimes de .NET conviven side-by-side**
```sh
curl -sSL -o /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh && chmod +x /tmp/dotnet-install.sh
sudo /tmp/dotnet-install.sh --channel 9.0 --runtime aspnetcore --install-dir /usr/share/dotnet --no-path
dotnet --list-runtimes   # 9.0.19 Y 10.0.11
```
  - -- cada app carga el runtime de SU `TargetFramework`, asi que instalar el 9 **no le
       cambia nada** a lo que apunta a net10.0. "Instalar el 9 solo para este proyecto" es
       exactamente esto, no hace falta aislar nada
  - -- `--runtime aspnetcore` (no el SDK): el SDK 10 ya compila net9.0. Un SDK por version
       no hace falta; un RUNTIME por version si
  - -- ⚠️ `cp: '/usr/share/dotnet/dotnet': Text file busy` si tienes una app corriendo. Es
       el *muxer* y es inocuo: el del 10 sirve para los dos
  - -- comprobar QUE runtime cargo de verdad, sin fiarse:
```sh
tr '\0' '\n' < /proc/<pid>/maps | grep -oE "Microsoft.NETCore.App/[0-9.]+" | sort -u
```
  - -- ⚠️ no sobrevive a recrear el dev container

- --- Revivir los eventos que el bug habia enterrado
```sql
UPDATE OutboxMessages SET Attempts = 0, LastError = NULL
WHERE ProcessedAt IS NULL AND Attempts >= 5;
```
  - -- 14 filas -> publicadas y consumidas en la vuelta siguiente -> `/health/ready` de
       `Degraded` a `Healthy`
  - -- ⚠️ con `pymssql` hay que hacer `conn.commit()`: sin autocommit el UPDATE se pierde
       en silencio y el `SELECT` posterior te dice que no paso nada
  - -- purgar la DLQ: `DELETE /api/queues/%2F/<cola>/contents` (204). Las metricas de la UI
       tardan unos segundos en refrescarse: no te asustes si sigue diciendo 1






## 24. Por que cada pieza de RabbitMQ es la que es
> Los terminos estan definidos en el glosario del cap. 18. Esto es el PORQUE de cada
> eleccion: por que topic y no direct, por que el DLX es fanout, y por que no Kafka.

- --- ⭐ El diagrama que falta: **como decide el exchange** (el del cap. 18 es el camino feliz)
```
                     routing key = "product.purchased"
                                   |
                                   v
 +=================================================================+
 |  exchange  apiecommerce.events                    tipo: TOPIC   |
 |  NO guarda nada. Compara la clave contra CADA binding y copia.  |
 +=================================================================+
        |                      |                        |
   binding                binding                  binding
   "product.purchased"    "product.#"              "order.#"
   CASA -> copia          CASA -> copia            NO casa -> nada
        |                      |                        |
        v                      v                        x
 +--------------------+  +----------------------+
 | apiecommerce.      |  | facturacion.product  |   <- consumidor FUTURO:
 | product-purchased  |  | (ejemplo)            |      cola + binding nuevos,
 +--------------------+  +----------------------+      CERO cambios en la API
        |
        |  consumidor: comprueba Type -> deduplica por MessageId -> efecto
        |
   ack (OK)                       nack requeue:false  (mensaje malo)
                                          |
                                          v
 +=================================================================+
 |  exchange  apiecommerce.events.dlx               tipo: FANOUT   |
 |  IGNORA la routing key: copia a TODAS sus colas.                |
 +=================================================================+
                                          |
                                          v
                        +------------------------------------+
                        | apiecommerce.product-purchased.dlq |
                        +------------------------------------+

 Si la clave NO casa con NINGUN binding: el mensaje se pierde en silencio.
 Por eso el publicador va con `mandatory: true` -> el broker lo DEVUELVE.
```

- --- **Exchange** = una tabla de enrutado, **no un buzon**
  - -- recibe el mensaje, mira su etiqueta, decide a que colas copiarlo, y se olvida
  - -- no guarda nada, no tiene estado, **no puedes "leer de un exchange"**
  - -- ⚠️ consecuencia: si al publicar no hay ninguna cola atada que case, el mensaje
       **desaparece sin error**. De ahi `mandatory: true`

- --- **Routing key** = la etiqueta que el publicador pega al mensaje. Nada mas
  - -- NO es una direccion, NO nombra una cola, NO nombra un consumidor
  - -- es una **descripcion de lo que paso**, y es el unico criterio que mira el exchange
```csharp
// RabbitMqEventPublisher.cs
await channel.BasicPublishAsync(
    exchange: _options.Exchange,   // "apiecommerce.events"
    routingKey: eventType,         // "product.purchased"  <- LA ETIQUETA
    mandatory: true, ...);
```
  - -- convencion jerarquica de mayor a menor: `<sustantivo>.<que le paso>`
       (`product.purchased`, `product.created`, `order.shipped`). Esa jerarquia es la que
       luego deja filtrar por trozos
  - -- ⚠️ la clave con la que se PUBLICA sale del evento (`ProductPurchased.EventType`).
       `RabbitMq:RoutingKey` es otra cosa: el patron del BINDING. Se llaman parecido

- --- **Binding** = la regla que declara la COLA: *"atame a este exchange y mandame lo que
      case con este patron"*
  - -- ⭐ y aqui esta el punto de todo el diseño: **el publicador nunca nombra una cola**.
       No sabe cuantas hay, ni si hay cero
  - -- añadir mañana un servicio de facturacion = una cola nueva con su binding a
       `product.purchased`. **Cero lineas tocadas en la API.** Eso es lo que se compra

- --- Los **4 tipos de exchange**, y cuando quieres cada uno
```
direct    igualdad exacta del string      -> el destino es fijo y no va a crecer:
                                             colas de trabajo, RPC
topic     patron con comodines            -> EVENTOS DE DOMINIO: cada consumidor quiere
                                             un subconjunto distinto
fanout    no compara: copia a TODAS       -> difusion total: invalidar caches, notificar
                                             a N replicas, DLQ
headers   por cabeceras, no por clave     -> casi nunca; filtrar por varios criterios
                                             que no caben en un string
```
  - -- comodines de `topic`, sobre palabras separadas por PUNTOS:
```
*   exactamente UNA palabra    product.*   casa product.purchased, NO product.item.sold
#   cero o mas palabras        product.#   casa product.purchased Y product.item.sold
                               #           casa absolutamente todo
```
  - -- el `(AMQP default)` de la UI es un `direct` especial donde cada cola esta atada
       automaticamente con su propio nombre: por eso publicar con `exchange: ""` y
       `routingKey: "mi-cola"` funciona
    - ⚠️ no usarlo: acopla el publicador al NOMBRE de la cola, justo lo que se evita

- --- ⭐ Por que **`topic` y no `direct`** aqui
  - -- hoy el binding es exacto (`product.purchased`), asi que **`direct` funcionaria igual**
  - -- la diferencia es que `direct` cierra la puerta y `topic` la deja abierta:
    - con `topic`, un consumidor futuro se ata a `product.#` y recibe los eventos de
      producto de hoy **y los que añadas mañana**
    - con `direct`, ese consumidor necesita un binding por cada tipo nuevo, y **alguien
      tiene que acordarse cada vez**
  - -- y no cuesta nada: mismo rendimiento en la practica, misma API. Es elegir el tipo por
       la forma que **va a tener** el sistema, no por la que tiene hoy
  - -- ⚠️ si el destino fuera fijo por diseño (una cola `enviar-email` y punto), `direct`
       seria lo correcto y `topic` seria sobreingenieria

- --- **DLX / DLQ**: `DLQ` es la cola donde acaba lo que no se pudo procesar; `DLX` es el
      exchange por el que pasa para llegar
  - -- ⚠️ **NO hay "tipos de DLX"**. Un DLX es un exchange normal y corriente —puede ser
       direct, topic o fanout—. Lo que lo convierte en DLX es que **una cola lo señala**:
```csharp
// RabbitMqConnection.DeclareTopologyAsync
arguments: new Dictionary<string, object?>
{
    ["x-dead-letter-exchange"] = _options.DeadLetterExchange   // "apiecommerce.events.dlx"
}
```
  - -- un mensaje cae al DLX en **tres** casos, y conviene conocer los tres:
```
1. nack/reject con requeue:false   <- el nuestro, el consumidor lo hace a proposito
2. se le expira el TTL en la cola
3. la cola llego a x-max-length y se descarta por la cabeza
```
  - -- ⭐ **por que el DLX es `fanout`**: al dead-letterear, el mensaje **conserva su routing
       key original** (`product.purchased`). Con un DLX `topic` o `direct` habria que
       declarar bindings que casen con TODAS las claves que puedan llegar a morir, y el dia
       que añadas `order.shipped` sus fallos **se perderian en silencio** porque nadie ato
       ese patron. `fanout` ignora la clave y lo manda todo: es lo que quieres de un cubo
       de basura, **no discriminar**
  - -- y por que existe: sin DLQ, un mensaje envenenado (un payload que no va a deserializar
       nunca) se reencola **para siempre** y bloquea la cola

- --- ⭐ El porque del diseño ENTERO, en una linea: **no puedes escribir en SQL Server y en
      RabbitMQ atomicamente**. No hay transaccion que abarque a los dos
```
publicar -> commit    si falla el commit, anunciaste una compra que NO existe
commit -> publicar    si falla la publicacion, la compra existe y NADIE se entera
```
  - -- el **outbox** lo resuelve moviendo el problema: el evento es *una fila mas de la misma
       transaccion*. O se guardan el descuento de stock y el evento, o no se guarda ninguno
  - -- de ahi salen tres consecuencias **encadenadas**:
```
1. la API funciona con el broker CAIDO   -> la compra se completa, el evento espera
2. se publica primero y se marca despues -> un crash en medio REPUBLICA = at-least-once
3. luego el consumidor esta OBLIGADO a deduplicar (ProcessedMessages, MessageId como PK)
```
  - -- el 3 no es un extra: es la **contrapartida obligatoria** del 2

- --- ⭐ Por que **RabbitMQ y no Kafka**. Son cosas distintas, no dos marcas de lo mismo
```
                     RabbitMQ                        Kafka
modelo        COLA: reparte y BORRA al ack     LOG: el mensaje se queda, cada
                                               consumidor lleva su offset
enrutado      rico (exchanges, patrones),      ninguno: publicas a un topic y el
              lo decide el BROKER              consumidor filtra
confirmacion  POR MENSAJE (ack/nack)           por offset, avanza en bloque
reintentar 1  nativo                           incomodo: el offset es una POSICION,
                                               no puedes saltarte uno
DLQ           nativa (x-dead-letter-exchange)  a mano: topics de retry/DLT + codigo
fuerte en     enrutado y reintentos finos      volumen bruto y REPLAY historico
```
  - -- nuestro caso pide exactamente la columna izquierda: reintentar **un** mensaje concreto,
       mandar a la DLQ **ese** y no los demas, y enrutar por tipo de evento
  - -- "este mensaje fallo, los otros no" es justo lo que **peor** se hace en Kafka
  - -- **Kafka gana** cuando necesitas: reprocesar el historico desde el principio (event
       sourcing, rehacer una proyeccion), varios consumidores independientes leyendo el
       mismo flujo a su ritmo, orden estricto por clave, o decenas de miles de msg/s.
       Nada de eso aplica aqui
  - -- ⭐ y lo importante: **la decision es reversible barata**. El outbox —la parte que de
       verdad importa— es agnostico. Migrar seria reimplementar `IEventPublisher` y el
       consumidor; ni el servicio de negocio, ni la tabla, ni la transaccion se enteran.
       Por eso no merece la pena elegir Kafka "por si acaso"






## 25. El primer proyecto de tests  <- y como saber si un test sirve
```sh
dotnet new xunit -o tests/ApiEcommerce.Tests   # ojo: la plantilla del SDK 10 solo ofrece net10.0
dotnet sln add tests/ApiEcommerce.Tests/ApiEcommerce.Tests.csproj
dotnet test tests/ApiEcommerce.Tests
```

- --- ⚠️ **`tests/**` hay que EXCLUIRLO del .csproj de la API**, igual que `AGENTS/**`
```xml
<Compile Remove="tests/**" />
```
  - -- el glob implicito del SDK compila TODO el .cs que cuelgue de la carpeta del csproj
  - -- sin esto: xunit y Moq acaban dentro de la imagen de produccion, y ademas hay
       referencia circular (la API compila los tests, que referencian a la API)
  - -- la raiz de este repo ES la carpeta del proyecto, por eso `tests/` va dentro y no en
       `../ApiEcommerce.Tests` como suele ponerse

- --- ⚠️ El TFM del proyecto de tests se pone **a mano**
  - -- la plantilla del SDK 10 solo ofrece `net10.0` y la API es `net9.0`
  - -- un test que corre sobre un runtime distinto del que se despliega no prueba lo que crees

- --- ⚠️ **FluentAssertions NO** (aunque la skill dotnet-best-practices la pida)
  - -- desde la **v8 exige licencia comercial**. Ya tenemos ese problema abierto con
       AutoMapper 15; no hacen falta dos
  - -- con `Assert` de xunit sobra. Alternativa MIT si se quiere fluidez: Shouldly

- --- ⭐ `MockBehavior.Strict` convierte "no llamar" en una ASERCION
```csharp
private readonly Mock<ICategoryRepository> _repository = new(MockBehavior.Strict);
// en un PATCH sin nombre, CUALQUIER llamada al repositorio hace fallar el test
// -> eso es exactamente lo que se quiere probar: que no consulta la base
```
  - -- con el modo Loose por defecto, "no se llamo" hay que verificarlo aparte y se olvida

- --- ⭐ Verificar el **ORDEN**, no solo que se llamo
```csharp
var order = new List<string>();
_rules.Setup(...).Callback(() => order.Add("rules"));
_repo.Setup(...).Callback(() => order.Add("repository"));
Assert.Equal(["rules", "repository"], order);
```
  - -- media arquitectura de este repo son invariantes de ORDEN: las reglas ANTES de
       escribir, el mapeo DESPUES de las reglas (para que la regla vea el estado previo),
       la invalidacion de cache DESPUES de que la escritura vaya bien
  - -- `Times.Once` sobre cada uno pasa igual aunque el orden este invertido

- --- ⚠️ `SqlException` **no tiene constructor publico**: la crea el driver
```csharp
// hay que llegar por reflexion al CreateException interno
Activator.CreateInstance(typeof(SqlError), BindingFlags.NonPublic | BindingFlags.Instance, ...)
typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic)
```
  - -- es feo y es fragil ante un cambio de version de Microsoft.Data.SqlClient
  - -- pero la alternativa es no poder probar el mapeo 2601/2627/1205/547 sin levantar SQL
       Server, **y ese mapeo ya se rompio una vez aqui**
  - -- si un dia el helper falla al actualizar el paquete, el fallo es DEL HELPER y no del
       handler: se arregla en un sitio

- --- ⚠️ **Identity no expone interfaces**: `UserManager` y `SignInManager` son clases
      concretas con constructores enormes
```csharp
new Mock<UserManager<ApplicationUser>>(Mock.Of<IUserStore<ApplicationUser>>(),
    null!, null!, null!, null!, null!, null!, null!, null!);
```
  - -- Moq las puede simular porque sus metodos son `virtual`; hay que pasar los
       argumentos posicionales y tragarse los `null!`

- --- ⭐⭐ **Como saber si un test SIRVE: prueba de mutacion**
  - -- un test que pasa contra el codigo roto no vale nada, y eso no se nota nunca
  - -- se reintroducen bugs REALES ya corregidos y se mira si la suite los caza
```sh
# 1) quitar el MapFrom explicito de CategoryId  (el bug del 500 por FK)
# 2) volver FindSqlException a mirar solo el InnerException directo (el 500 en TryDecrementStock)
dotnet test    # -> Failed: 7, y son EXACTAMENTE los 4 metodos que debian caer
```
  - -- si la suite sigue verde con el bug dentro, el test estaba probando otra cosa
  - -- hacerlo con cada bloque nuevo de tests, no una sola vez

- --- Lo que los unitarios NO cubren, que es donde han salido TODOS los bugs de este repo
```
concurrencia   -> Task.WhenAll de peticiones REALES (secuencialmente pasaba con el bug dentro)
degradacion    -> con Redis o el broker CAIDOS
arranque       -> ASPNETCORE_ENVIRONMENT=Production con la config minima
```
  - -- eso es la fase 3 en adelante: WebApplicationFactory + Testcontainers






## 26. Tests de integracion  <- y el test que pasaba sin probar NADA
```sh
dotnet test tests/ApiEcommerce.Tests            # 153 tests, ~18 s
```

- --- Montaje: `WebApplicationFactory<Program>` contra SQL Server y Redis **reales**
```
base de datos   ApiEcommerceNET8_Tests    <- se BORRA y se migra en cada corrida
prefijo Redis   apiecommerce-tests:       <- no pisa las claves de desarrollo
broker          desactivado (RabbitMq:ConnectionString vacio)
```
  - -- ⚠️ **sin Testcontainers**, que es lo que dice todo el mundo: no hay Docker dentro
       del dev container. Se usa la infra del host con recursos propios. Testcontainers
       en CI, que si tiene Docker
  - -- las migraciones y el seeding los aplica el propio `Program` al arrancar el host:
       asi el test cubre TAMBIEN ese camino, que ya rompio produccion una vez

- --- ⚠️ `Program` de instrucciones de nivel superior nace `internal`
```csharp
public partial class Program;   // al final de Program.cs, o WebApplicationFactory<Program> no compila
```

- --- ⚠️⚠️ **`UseSetting`, NO `ConfigureAppConfiguration`**  <- lo que mas me costo ver
```csharp
// MAL: los callbacks se aplican DESPUES de que Program haya hecho sus registros
builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(...));

// BIEN: entra antes de que corra Program
foreach (var (k, v) in settings) builder.UseSetting(k, v);
```
  - -- varias piezas (`AddDistributedCaching`, `AddMessaging`, `AddHealthProbes`) leen la
       configuracion **eager** para decidir QUE implementacion registran
  - -- con `ConfigureAppConfiguration` la config FINAL era correcta (`Redis:Configuration`
       tenia el valor bueno) pero el contenedor ya tenia registrado `NoIdempotencyStore`
  - -- ⭐ **y el sintoma es lo peor de todo: los tests de idempotencia pasaban en VERDE
       sin probar nada.** Un no-op no rompe "dos operaciones distintas dan dos
       resultados": solo cayeron los DOS que exigian un replay de verdad
  - -- leccion: cuando un test de integracion pasa, preguntarse si pasaria igual con la
       feature APAGADA. Si la respuesta es si, el test no vale. De ahi `TestHostGuardTests`

- --- ⚠️ El limitador de tasa se limita a SI MISMO
  - -- 100 req/min por IP, y en `WebApplicationFactory` todas las peticiones comparten IP
  - -- a las 100, media suite empieza a recibir 429 por un motivo ajeno a lo que prueba
  - -- solucion: los limites pasan a `RateLimit:*` en configuracion (que ademas hacia
       falta en produccion) y el host de tests los sube

- --- ⚠️ Aislamiento: los que levantan su PROPIO host tambien van sin paralelismo
```
Passed en aislado  ->  Failed en la suite completa
"Database 'ApiEcommerceNET8_Tests' already exists. Choose a different database name."
```
  - -- dos hosts arrancando a la vez ejecutan dos `MigrateAsync` sobre la misma base
  - -- todos a la misma `[Collection]` con `DisableParallelization = true`, usen o no su fixture

- --- ⭐ La concurrencia, con `Task.WhenAll` de peticiones REALES
```csharp
var responses = await Task.WhenAll(Enumerable.Range(0, 15)
    .Select(_ => user.PostAsJsonAsync("/api/v1/product/buy", new { sku, quantity = 1 })));
```
  - -- da exactamente lo mismo que la medicion manual: 10x200, 5x409, **stock 0**
  - -- en secuencia esto pasaba TAMBIEN con la implementacion defectuosa. Por eso el bug
       del stock sobrevivio tanto

- --- ⭐ La degradacion: correcta pero **inservible**, y solo se ve midiendo
```
Redis inalcanzable, timeouts de fabrica (ConnectTimeout 5s x ConnectRetry 3, SyncTimeout 5s):
   GET /category                     11 s
   POST /product/buy + Idempotency   34 s
```
  - -- responde bien, pero a esa latencia el cliente ya corto, los hilos se acumulan y la
       caida de una OPTIMIZACION se lleva la API entera. "Degradar en abierto" tambien
       tiene que ser RAPIDO
  - -- acotar timeouts a 1 s bajo la compra de 34 s a 11 s... y ahi se vio lo otro:
  - -- ⚠️ **`AddStackExchangeRedisCache` crea su PROPIO multiplexer** y se quedaba con los
       timeouts de fabrica. Los mios solo protegian la mitad del sistema
```csharp
var multiplexer = new Lazy<IConnectionMultiplexer>(() => Connect(options.Configuration));
services.AddStackExchangeRedisCache(r => r.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer.Value));
services.AddSingleton<IConnectionMultiplexer>(_ => multiplexer.Value);
```
  - -- con una sola conexion: **3 s y 7 s**

- --- ⭐ El arranque: el P0 del crash-loop **seguia vivo en otro atributo**
  - -- ya se habia quitado el `[Required]` de `SeedOptions.AdminPassword` por esto mismo
  - -- pero `[EmailAddress]` sobre `AdminEmail` es igual de incondicional: con el seeding
       APAGADO y `Seed__AdminEmail=` vacio, `OptionsValidationException` al arrancar
  - -- lo destapo el test de "arranca en Production con el seeding apagado", que era
       literalmente el test escrito para el bug anterior
  - -- **la regla condicional va en `.Validate(...)`, nunca en un atributo.** Otra vez






## 27. Secretos fuera del repo, y CI
- --- ⭐ **user-secrets**: los secretos de DESARROLLO tampoco se commitean
```sh
# en el .csproj
<UserSecretsId>apiecommerce-dev-2026</UserSecretsId>

dotnet user-secrets set "Jwt:SecretKey" "$(openssl rand -base64 48)"
dotnet user-secrets set "ConnectionStrings:ConexionSql" "Server=...;Password=..."
dotnet user-secrets set "Seed:AdminPassword" "..."
dotnet user-secrets list
```
  - -- se guardan en `~/.microsoft/usersecrets/<id>/secrets.json`, **fuera del repo**
  - -- `appsettings.Development.json` sigue commiteado, pero solo con lo NO sensible
       (Redis, RabbitMQ, CORS, Serilog): sirve de documentacion del entorno de dev
  - -- ⚠️ user-secrets **solo se cargan en Development**. En cualquier otro entorno son
       variables de entorno (`Jwt__SecretKey`). No es un despiste del framework: es que en
       produccion no debe existir un fichero de secretos en el disco del desarrollador
  - -- comprobar las DOS direcciones, no solo que arranca:
```sh
ASPNETCORE_ENVIRONMENT=Production dotnet bin/Debug/net9.0/ApiEcommerce.dll
# -> OptionsValidationException: 'Jwt:SecretKey is required'   <- CORRECTO
```
    - una clave de firma **no puede degradar en abierto**. Si el arranque sin clave no
      falla, es que se esta firmando con algo que no es una clave

- --- ⚠️ Quitar el secreto del fichero **no lo quita del historial**
  - -- sigue en los commits anteriores. Limpiarlo de verdad es `git filter-repo`, que
       reescribe hashes; solo compensa si el repo se hace publico
  - -- lo barato y efectivo es **rotar** la credencial, que es lo que se hizo con la JWT

- --- 🔴 Y la peor: **un token de GitHub en `.git/config`**
```sh
git remote -v
# origin  https://ghp_XXXXXXXX@github.com/usuario/repo.git   <- PAT en texto plano
```
  - -- lo escribe `git clone https://<token>@github.com/...`, que es comodo y se olvida
  - -- aparece en cualquier `git remote -v`: logs, capturas, pantallas compartidas
  - -- `.git/` no se versiona, asi que **ningun .gitignore te protege de esto**
  - -- se arregla revocando el token en GitHub y volviendo a autenticar:
```sh
git remote set-url origin https://github.com/usuario/repo.git
gh auth login          # o pasar el remoto a SSH
```

- --- CI: `.github/workflows/ci.yml`
  - -- SQL Server y Redis como **`services` del runner**, no Testcontainers: mismo camino
       de codigo que en local y sin Docker-in-Docker
```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    options: >-
      --health-cmd "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P '...' -C -Q 'SELECT 1'"
      --health-start-period 30s
```
  - -- ⚠️ la imagen `mssql` **no trae healthcheck propio** y tarda ~20 s en aceptar
       conexiones: sin `--health-cmd`, el primer test falla por "connection refused" y
       parece un fallo del codigo
  - -- ⚠️ dentro del job, el service escucha en su puerto **INTERNO** (1433), no en el
       1434 que se publica en local. De ahi que el `ApiFactory` lea `TEST_SQL_HOST`,
       `TEST_SQL_PORT`, `TEST_SQL_PASSWORD` y `TEST_REDIS` del entorno
  - -- ⭐ `-warnaserror` en el build: la regla de "0 warnings" dura exactamente hasta el
       primer warning que nadie mire. O algo la obliga, o no es una regla
  - -- job aparte que **construye el Dockerfile**: aqui nunca se habia podido construir
       por no haber Docker en el dev container






## 28. Cerrar la deuda: outbox multi-replica, reintentos reales, ETag y trazas
- --- ⭐ **Sequence (`bigint IDENTITY`) en vez de ordenar por `OccurredAt`**
```
OccurredAt = DateTime.Now DEL PROCESO que escribio la fila
   -> con 2 replicas, el orden de publicacion depende del RELOJ DE CADA MAQUINA
   -> y no desempata las filas del mismo milisegundo (con insercion en lote, lo normal)
```
  - -- un IDENTITY lo asigna UN SOLO arbitro: el servidor SQL
  - -- ⚠️ EF genera `ALTER TABLE ADD [Sequence] bigint NOT NULL IDENTITY` y quita solo el
       `DEFAULT 0` que pondria en cualquier otra columna. Revisar el script igualmente:
```sh
dotnet ef migrations script <migracion-anterior>
```
  - -- a las filas YA existentes SQL les asigna la secuencia en orden fisico, arbitrario.
       Da igual: son historico, la garantia es de aqui en adelante

- --- ⭐ **`sp_getapplock` y no un claim por filas** (la decision, no el codigo)
```
claim con LockedUntil + UPDATE ... OUTPUT   -> 2 replicas drenan EN PARALELO
                                            -> adios a la garantia de orden que acabas de ganar
                                            -> y hay que gestionar la expiracion del claim
sp_getapplock exclusivo                     -> drena UNA a la vez, orden intacto, cero columnas
```
  - -- drenar es un trabajo de fondo cada 5 s con lote acotado: serializarlo no cuesta nada
  - -- ⚠️ `@LockOwner='Transaction'`, NUNCA `'Session'`: "session" es la conexion, y la
       conexion sale de un POOL y se reutiliza para otra cosa
  - -- `@LockTimeout=0`: si otro lo tiene, se salta la vuelta. Encolar replicas esperando
       un lock solo acumula latencia
  - -- prueba determinista, mucho mejor que "lanzar dos replicas y ver":
```python
# retener el lock desde otra sesion SQL y ver que la app NO publica
cur.execute("EXEC sp_getapplock @Resource='apiecommerce:outbox-publisher', "
            "@LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0")
```
    - resultado: compras siguen dando **200** (el lock no toca al negocio), 0 publicaciones,
      y al soltarlo se publica. Eso es lo que hay que demostrar

- --- ⭐ **Reintentos del consumidor: cola de ESPERA con `x-message-ttl`**
```
cola principal --(falla)--> [publico yo] --> exchange .retry --> cola .retry (TTL 30s, sin consumidor)
                                                                      |  caduca
                                                                      v
                                                          dead-letter -> exchange principal
                                                                      -> cola principal otra vez
```
  - -- cada vuelta el broker incrementa `x-death[].count`: **ese** es el contador real.
       `args.Redelivered` es una BANDERA (se pone a true por un reinicio del pod sin fallo)
  - -- ⚠️ se añade como topologia NUEVA, sin tocar el `x-dead-letter-exchange` de la cola
       principal. Redeclarar una cola con argumentos distintos da **406 PRECONDITION_FAILED**:
       desplegarlo obligaria a BORRAR la cola en produccion, con sus mensajes dentro
  - -- ⚠️ publicar al reintento **ANTES** de hacer ack. Al reves, morir entremedias pierde
       el mensaje. Duplicar antes que perder: la misma regla del outbox
  - -- ⚠️ los valores de texto de `x-death` viajan como **byte[]**: compararlos contra un
       string sin convertir da siempre false y el contador se queda en 0 PARA SIEMPRE
```csharp
byte[] bytes => Encoding.UTF8.GetString(bytes),
```

- --- ⭐ **ETag / If-Match: `RowVersion` sola NO cierra el lost update**
```
A lee (rowversion=7) -> B edita (rowversion pasa a 8) -> A guarda
   el PATCH de A RELEE la fila -> EF compara contra 8 -> cuadra -> A pisa a B en silencio
```
  - -- el unico valor que prueba QUE VERSION LEYO A es el que A traiga de vuelta: el ETag
  - -- 412 y no 409: el 409 dice "chocas con el estado"; el 412 dice "la precondicion que
       TU pusiste no se cumple", y eso le dice al cliente que relea
  - -- OPCIONAL a proposito: exigir `If-Match` romperia a todos los clientes actuales
  - -- ⚠️ hay que quitar el envoltorio del RFC (comillas, y el `W/` de las etiquetas
       debiles) o el token no casa nunca y TODO PATCH con If-Match da 412

- --- ⚠️ **El replay de idempotencia no era identico byte a byte**
  - -- el filtro re-serializaba con SUS opciones: un `+` de base64 salia como `+`
  - -- solo se nota cuando el `rowVersion` lleva un `+`: fallo **aleatorio y dependiente de
       los datos**, el peor de diagnosticar. Lo cazo un test que comparaba los dos cuerpos
  - -- leccion: si memorizas una respuesta para reproducirla, tienes que memorizar lo que
       de verdad se escribio, no volver a serializarlo

- --- OpenTelemetry: lo que no es obvio
  - -- **se instrumenta siempre, se exporta solo si hay `OtlpEndpoint`**. Misma regla que
       Redis y RabbitMQ: la observabilidad no puede ser el motivo de que la API no arranque
  - -- `ParentBasedSampler`: si quien te llamo decidio trazar, trazas. Decidir por tu cuenta
       parte las trazas distribuidas por la mitad
  - -- filtrar `/health`: se ejecuta cada pocos segundos y ahoga cualquier traza que importe
  - -- ⚠️ el TEXTO de las consultas SQL **no** se captura (desde la 1.10 hay que activar una
       bandera experimental). No activarla: lleva correos, nombres y precios al backend de
       trazas, que casi nunca esta tan protegido como la base
  - -- el `TraceId` en cada linea de log sale de `Activity.Current`, sin paquete extra:
```csharp
using (LogContext.PushProperty("TraceId", Activity.Current?.TraceId.ToString()))
```

- --- ⚠️ `UseHsts()` **fuera de Development**
  - -- en local la cabecera queda cacheada en el navegador para `localhost` y rompe
       cualquier otro proyecto servido en claro por ese host — y el fallo aparece en OTRA
       aplicacion, que es lo que lo hace dificil de atar

- --- ⚠️ `dotnet add package` escribe `Version="*"`
  - -- en CI eso significa que dos builds del MISMO commit pueden no ser el mismo binario
  - -- fijar siempre la version resuelta (`obj/project.assets.json` la dice)


---
---


## 29. La idempotencia bajo carga  <- validar es tambien medir lo que aguanta

- --- ⭐ **Lo primero que hay que entender: el mecanismo era correcto, y aun asi fallaba**
  - -- exactamente-una-vez aguanto rafagas de 350 simultaneas con la misma clave (baja 1)
  - -- y aguanto **entre DOS REPLICAS**, dos procesos contra el mismo Redis. Es la razon
       por la que el store no esta en memoria, y no se habia comprobado nunca
  - -- lo que falla no es la logica: es que **se apaga sola** cuando hay carga

- --- 🔴 **Una decision buena para un caso, mala para el otro**
```
SyncTimeout = AsyncTimeout = 1000 ms   <- se eligio para "Redis CAIDO"
                                          (con los 5 s de fabrica, una compra tardaba 34 s)
```
  - -- pero con Redis **vivo y sano** una rafaga tambien agota 1000 ms: un solo multiplexer,
       3 operaciones por peticion, y el `SET NX` expira
  - -- el `catch` degrada en abierto y **devuelve "eres el primero"** -> la peticion se
       ejecuta SIN garantia, en silencio
  - -- medido: 26 de 1000 peticiones (2,6 %) a 64 conexiones; 174 de 14 400 (1,21 %) con
       carga sostenida sobre dos replicas
  - -- ⚠️ **el caso patologico se muerde la cola**: la API va lenta -> el cliente reintenta
       -> el reintento cae en la ventana saturada -> y justo ahi la proteccion contra el
       doble cobro esta apagada
  - -- la leccion general: **"degradar en abierto" no es una decision, son dos**. Una cosa
       es la dependencia caida (mantener la API viva) y otra la dependencia sana pero lenta
       por tu propia carga. Se defendian con el mismo argumento y no son lo mismo

- --- **`SET clave valor EX ttl NX GET`: reservar y leer en UN viaje** (Redis >= 7.0)
```
1er SET -> (nil)    y reserva            <- la clave era mia, ejecutar
2do SET -> "uno"    y NO pisa, TTL intacto <- ya existia, aqui esta lo que hay
```
  - -- quita el `GET` previo: **3,00 -> 2,00 viajes por peticion**
  - -- ⚠️ hasta la 6.2, combinar `NX` con `GET` era un error de sintaxis. Si esto corriera
       contra una version vieja, el `catch` lo tomaria por "Redis no responde" y **la
       idempotencia se apagaria entera, en silencio**
  - -- ⚠️ `INFO commandstats` **cuenta comandos, no viajes**: lo que corre dentro de un
       script Lua aparece ahi. Medido mal daba 4,00 ops/peticion cuando eran 2 idas y venidas

- --- ⭐ **Una reserva sin dueño: `Release` borrando la reserva de OTRO**
```
A reserva (60 s) -> la accion de A se eterniza -> la reserva CADUCA
B reserva y ejecuta  <- duplicado, hasta aqui es la deuda ya conocida del lease
A termina en error y hace Release -> BORRA la reserva VIVA de B
un tercer reintento vuelve a pasar el SET NX -> ejecuta OTRA VEZ
```
  - -- lo grave no es el duplicado: es que la ventana **deja de estar acotada por el TTL**
       y se reabre en cada vuelta
  - -- se arregla con un token de propiedad y un CAS; el token va de PREFIJO del valor:
```lua
local v = redis.call('GET', KEYS[1])
if v and string.sub(v, 1, #ARGV[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) end
return 0
```
  - -- ⚠️ meter el token DENTRO del JSON y buscarlo con `string.find` **no es exacto**: el
       cuerpo memorizado podria contener esa misma subcadena. Por eso prefijo y `string.sub`
  - -- y sin token (camino degradado, no reservamos nada) **no se borra nada**: un `DEL`
       incondicional ahi es justo el bug

- --- ⚠️ **La comprobacion de la huella tiene que estar en TODOS los caminos**
  - -- habia un `Replay` —el de quien pierde la reserva por poco— que reproducia **sin
       comparar el cuerpo**: te devolvia la compra de 1 unidad cuando pediste 5
  - -- dos disparadores distintos, y el segundo ni siquiera necesita simultaneidad: basta
       con que el `GET` inicial degrade por un timeout (devuelve `null`, se salta el 422)
  - -- con una sola operacion el estado existente **solo puede llegar por un sitio**, asi
       que la comprobacion ya no se puede esquivar. Arreglar la forma > añadir un `if`

- --- ⚠️ **Un filtro de accion NO ve el `Location`**
  - -- habia codigo para memorizar esa cabecera y reproducirla en un 201. Nunca funciono
  - -- cuando un `IAsyncActionFilter` recupera el control tras `await next()`, MVC **aun no
       ha ejecutado el `IActionResult`**: `CreatedAtRouteResult` escribe `Location` en
       `ExecuteResultAsync`, que corre despues de todos los filtros de accion
  - -- captura vacia -> `Headers = null` siempre. Codigo muerto que la documentacion daba
       por resuelto, que es peor que no tenerlo

- --- **Los plazos como constantes hacen el codigo INTESTABLE**
  - -- `ReservationTtl` era un `private static readonly` de 60 s: el unico test posible
       tardaba un minuto, asi que no existia
  - -- pasarlo a `IdempotencyOptions` no es burocracia de la regla §5: es lo que permite
       bajarlo a 1 s y **fijar con un test** una limitacion conocida, para que nadie la
       descubra creyendo que es un bug nuevo

- --- ⚠️ **Medir con el arnes equivocado inventa hallazgos**
  - -- primera medicion, 200 hilos de `urllib`: «con `Idempotency-Key` el throughput cae
       13x, de 234 a 18 rps». Lo iba a apuntar como hallazgo
  - -- con un cliente asyncio sobre sockets crudos: el filtro **no se mide** por encima del
       ruido, y el pico de 20 s aparecia tambien en la columna SIN clave
  - -- el cuello era el GIL de Python, no la API. **Antes de apuntar un numero, comprobar
       que el que mide no es el que estorba**

- --- **Y lo que NO se consiguio**, que tambien se anota
  - -- no se logro provocar una duplicacion REAL en ~20 tandas: hace falta que el timeout
       caiga sobre el duplicado, y los duplicados son una fraccion minuscula del trafico
  - -- se reporta como es —camino de codigo confirmado y contado, probabilidad baja,
       impacto alto— y no como un bug reproducido
  - -- bajar los viajes **movio el umbral, no elimino el modo de fallo**: sigue habiendo
       1,21 % sin garantia. Decir "arreglado" habria sido falso


---
---


## 30. Cuando la pregunta esta mal planteada  <- idempotencia como GARANTIA

- --- ⭐ **La pregunta era «¿fallo en cerrado con 503?» y la respuesta fue «ninguna de las dos»**
  - -- venia del cap. 29: bajo carga el store de Redis se apagaba solo y la compra se
       ejecutaba sin garantia (174 de 14 400, con Redis SANO)
  - -- se monto un debate a cuatro: un abogado por postura, investigacion de la industria,
       y una lectura DDD/Clean
  - -- **los dos abogados llegaron por su cuenta al mismo sitio**: el 503 sobre Redis es
       una media medida, compra riesgo de disponibilidad sin comprar correccion
  - -- leccion de metodo: cuando las dos posturas de un debate convergen en una tercera
       cosa, la tercera cosa es la respuesta

- --- **Lo que hace la industria** (fuentes primarias, no memoria)
  - -- **nadie ejecuta sin garantia**. Cero casos documentados
  - -- Adyen: `503` + codigo 703 + cabecera `transient-error: true`, y la salida sin
       garantia la toma **el cliente** omitiendo la cabecera. La renuncia es suya, explicita
  - -- AWS Powertools: falla cerrado por construccion, y **no se puede desactivar**; el
       registro `INPROGRESS` se escribe ANTES de invocar la funcion
  - -- ⭐ Azure documenta el patron bueno: *marcador de deduplicacion y efectos de negocio
       en la MISMA transaccion*, con una restriccion de unicidad como arbitro (inbox pattern)
  - -- Stripe guarda sus claves de idempotencia en **su misma base de negocio**, no en cache
  - -- ⚠️ Stripe: un 429 del rate limiter puede dar resultados distintos con la misma clave,
       porque **el limitador corre ANTES de la capa de idempotencia**. El orden del pipeline
       es una decision, no un detalle

- --- ⭐ **El reencuadre que lo resuelve todo**
```
si el almacen de idempotencia ES la base de datos del negocio,
   "el almacen no esta"  ==  "la operacion no puede ocurrir"
   -> la pregunta "¿ejecuto sin garantia?" DESAPARECE
```
  - -- la pregunta solo es dificil cuando el almacen es infraestructura SEPARADA (Redis,
       DynamoDB) de la que escribe el negocio
  - -- y el repo **ya lo hacia bien** en el consumidor (`ProcessedMessages` + efecto en una
       transaccion). La misma pregunta tenia dos respuestas, y la debil estaba en el camino
       que toca el dinero

- --- **No mover la idempotencia entera: PARTIRLA**
  - -- protocolo (leer la cabecera, validar forma, elegir el codigo) -> adaptador
  - -- politica ("este intento no se ejecuta dos veces") -> servicio, en su transaccion
  - -- ⚠️ lo que NO debe bajar es la respuesta HTTP. El servicio no conoce `StatusCodes`
  - -- el concepto que baja **no es «Idempotency-Key»**, es **intencion de comando**: un job
       tiene la suya (el `MessageId`) y no manda cabeceras

- --- ⭐ **El parametro obligatorio como diseño, no como capricho**
```csharp
Task<CommandOutcome<ProductDto>> BuyAsync(BuyProductDto dto, CommandIntent intent, ...)
```
  - -- sin valor por defecto **a proposito**: es el mismo bug que motivo mover la
       transaccion —«llamar a BuyAsync desde un job la perdia en silencio»— y se evita igual
  - -- renunciar hay que ESCRIBIRLO: `CommandIntent.None`. La diferencia entre una decision
       y un descuido es que una de las dos aparece en el diff

- --- **La base de datos ya sabe hacer esto; no lo emules**
  - -- se fueron: la reserva, el TTL de 24 h, el estado "en curso", el token de propiedad
       de la garantia. Todo eso emulaba, mal, un `INSERT` sobre una clave primaria
  - -- si dos replicas insertan la misma PK a la vez, la segunda **se bloquea** hasta que la
       primera confirme y entonces choca; su transaccion entera se deshace, stock incluido
  - -- por eso con Redis caido las 40 simultaneas dan **200 todas** y ni un 409: no hay
       nada "en curso" que reportar, hay una fila que arbitra
  - -- ⚠️ hay que mirar el NUMERO de error (2601/2627) recorriendo `InnerException`, no
       cazar `DbUpdateException` a secas: eso se tragaria timeouts y deadlocks

- --- ⭐ **La identidad byte a byte se arreglo SOLA**
  - -- era deuda desde el cap. 28: el `+` del base64 salia como `+` en el replay
  - -- causa real: el filtro memorizaba el cuerpo YA SERIALIZADO, o sea una **segunda copia**
       que no pasaba por el formateador de MVC
  - -- al memorizar el **DTO** en vez de la respuesta HTTP, el replay vuelve a pasar por el
       mismo formateador. El test paso de comparar JSON parseado a comparar bytes
  - -- leccion: una deuda que se resiste suele ser el sintoma de que la pieza esta en el
       sitio equivocado, no de que falte codigo

- --- **Lo que se queda Redis, y por que sigue mereciendo la pena**
  - -- puerta de admision: si ya hay una identica EN VUELO, 409 sin tocar la base
  - -- sin ella, 300 reintentos simultaneos se quedarian bloqueados en la clave primaria
       **reteniendo cada uno su conexion**. Serian correctos, pero a costa del pool
  - -- el marcador vive solo mientras dura la peticion: se suelta en un `finally`
  - -- ⚠️ y se quito la cabecera `Idempotency-Guaranteed: false` que se habia añadido el
       dia antes: **ahora seria mentira**. La garantia ya no depende de la puerta

- --- ⚠️ **`IsConnected` no distingue lo que parecia distinguir**
  - -- la idea era: `IsConnected == true` + timeout => "Redis sano pero saturado" => 503
  - -- pero es `true` con Redis sano-ocioso **y** con sano-saturado, y con
       `AbortOnConnectFail = false` oscila durante un incidente real
  - -- habria dado 503 intermitentes justo en el peor momento: lo peor de las dos politicas
  - -- me lo tumbo el abogado de la postura contraria, y era mi recomendacion del dia antes

- --- **Y el patron no monotono que delata una cola, no un fallo semantico**
```
64 conexiones  -> 2,6 % de degradaciones
128 conexiones -> 0 %
260 concurrentes -> 22 %
```
  - -- un fallo semantico no es no monotono con la concurrencia; una **cola** si
  - -- es la firma de head-of-line blocking sobre UNA conexion TCP compartida por cache,
       idempotencia y health check


---
---


## 31. Lo que no se puede probar suele estar en el sitio equivocado

- --- ⭐ **El patron que unia casi toda la deuda de mensajeria**
  - -- cuatro puntos distintos, una sola causa: **vivian dentro de un `BackgroundService`
       atado a AMQP**, y por eso "verificado a mano" era lo maximo a lo que se llegaba
  - -- sacarlos de ahi convirtio tres cosas verificadas a ojo en **diez tests**
  - -- la regla que queda: si algo no se puede probar sin levantar media infraestructura,
       el problema no es el test, es donde vive el codigo

- --- **El P0 que llevaba meses sin red**
```
marca ANTES del efecto  ->  efecto falla  ->  la reentrega se ve como duplicado
                        ->  ack  ->  el mensaje DESAPARECE sin procesarse
```
  - -- se arreglo (marca y efecto en una transaccion) pero el test no se podia escribir:
       `ProcessAsync` era privado y solo escribia un log. **No habia forma de hacerlo fallar**
  - -- se parte en dos piezas: `IProductPurchasedHandler` (el efecto) e `IMessageInbox` (la
       unidad transaccional, gemelo de `IEventOutbox`)
  - -- y entonces el test es trivial: pasarle un efecto que lanza
  - -- ⚠️ el test de concurrencia enseño algo que no estaba escrito: **la perdedora LANZA**
       el choque de PK. El consumidor depende de reconocerlo para hacer ack; si dejara de
       reconocerlo, el mensaje daria vueltas hasta la DLQ **sin que nada fallara a la vista**

- --- ⭐ **Un contador que escribe otro no es tu contador**
  - -- `x-death` lo escribe el broker, y **sobrevive al paso por la DLQ**: un mensaje que un
       operador reencolaba volvia con el presupuesto agotado y moria en la primera entrega
  - -- o sea que **la herramienta que existe para recuperar mensajes no los recuperaba**
  - -- ademas el parseo era fragil: los valores de texto viajan como `byte[]` y compararlos
       con un `string` sin convertir devuelve `false` EN SILENCIO (error ya cometido)
  - -- y filtraba por el NOMBRE de la cola de reintento... que justo iba a cambiar
  - -- con cabecera propia (`x-retry-attempt`) el replay es **borrar una cabecera conocida**

- --- **Configuracion que no se puede cambiar no es configuracion**
  - -- `x-message-ttl` se fija al DECLARAR la cola: cambiar `RetryDelaySeconds` daba
       **406 PRECONDITION_FAILED** y dejaba la mensajeria abajo
  - -- solucion: **el TTL en el NOMBRE** (`...retry.7s`). Cambiarlo declara una cola nueva:
       despliegue aditivo, sin borrar nada en produccion
  - -- ⚠️ se descarto el TTL **en el mensaje**: en una cola FIFO un mensaje con TTL largo
       bloquea a los de detras aunque ya hayan caducado (head-of-line blocking)

- --- 🔴 **Y el arreglo abrio un bug que SOLO se vio ejecutando**
```
colas de espera ligadas a un exchange  ->  cada reintento se copia a TODAS
   retry (vieja) = 1 msg,  retry.30s = 1 msg,  retry.7s = 1 msg   <- el MISMO mensaje
```
  - -- las colas de plazos anteriores siguen existiendo **y ligadas**: no eran huerfanas
       inofensivas, seguian recibiendo
  - -- el inbox lo deduplicaba (no se ejecutaba de mas) pero multiplicaba el trafico y hacia
       ilegible lo que pasaba
  - -- arreglo: publicar al **exchange por defecto** con el NOMBRE DE LA COLA como routing
       key. Sin binding, sin fan-out — y es lo que de verdad se queria decir: "este mensaje,
       a esperar AQUI"
  - -- `RetryExchange` desaparece entero: nunca aporto enrutado, solo tenia un binding
  - -- ⭐ leccion: un cambio "aditivo y seguro" en topologia hay que **mirarlo en la UI del
       broker**, no razonarlo. `curl` a `/api/queues` tarda 2 segundos

- --- **Un canal AMQP por mensaje**
  - -- abrir un canal es un viaje de ida y vuelta al broker; con `BatchSize` 50 eran 50
  - -- reutilizarlo: medido, 30 eventos publicados abriendo **1** canal (de 1 a 2 en total)
  - -- ⚠️ los `IChannel` **no prometen ser thread-safe**: el acceso va con semaforo. Y si el
       publish falla hay que **descartar el canal**, o el siguiente falla con "canal cerrado"
       en vez de reconectar

- --- ⚠️ **`global.json` y el `rollForward` que no salta de major**
  - -- aqui SDK 10.0.400, en CI 9.0.x: "cero warnings" se medía con analizadores distintos
  - -- fijar `9.0.100` + `latestFeature` **no arranca aqui**: `latestFeature` se mueve dentro
       de la misma banda major.minor, no salta a la 10
  - -- se fija la **10** (la unica que hay aqui) y la CI instala LAS DOS:
```yaml
dotnet-version: |
  9.0.x     # el SDK 10 NO trae el runtime 9: sin esto compila pero no EJECUTA net9.0
  10.0.x    # el que fija global.json
```

- --- **Y lo que NO se verifico, dicho a proposito**
  - -- el ciclo completo de reintentos con un efecto que falla de verdad
  - -- todos los fallos de CONTENIDO (tipo inesperado, cuerpo ilegible) van a la DLQ **por
       diseño**, asi que el unico disparador del reintento es un fallo de infraestructura
  - -- provocarlo aqui exigia tumbar SQL Server, que es compartido
  - -- se cubre en dos mitades (4 tests del inbox + 6 del contador) y se deja escrito, en vez
       de dar por probado mas de lo que se probo


---
---


## 32. Los "errores" que no eran errores

- --- ⭐ **Un cliente que cuelga NO lanza `OperationCanceledException`**
  - -- cuando el cliente corta, ASP.NET cancela `RequestAborted` -> EF cancela el
       `SqlCommand` -> **SqlClient lanza un `SqlException`** ("A severe error occurred on
       the current command" / "Operation cancelled by user", con un Win32 258 dentro)
  - -- la OCE si estaba mapeada a 499; el SqlException no, asi que salia **500 con traza**
  - -- medido: 27 "errores" en una sola prueba de carga que no eran errores de nadie
  - -- el coste no es la respuesta (no hay nadie al otro lado): es el **ruido**. Las
       metricas de error y el log se llenan de incidentes falsos justo cuando hace falta
       leerlos, y un fallo real queda sepultado

- --- ⭐ **Decidir por el ESTADO de la peticion, no por el TIPO de la excepcion**
```csharp
catch (Exception ex) when (context.RequestAborted.IsCancellationRequested)
```
  - -- la cancelacion se propaga distinto segun donde pille: EF, el cliente AMQP,
       `HttpClient`, Kestrel leyendo el cuerpo... perseguir cada tipo es una lista que
       **nunca esta completa**
  - -- sintoma de que el enfoque por tipos era malo: **`SqlException` ni se puede construir
       en un test** (no tiene constructor publico)

- --- ⚠️ **EL detalle que lo hace funcionar: el ORDEN en el pipeline**
```
app.UseExceptionHandler();                      <- el diagnostico del framework
app.UseMiddleware<ClientAbortMiddleware>();     <- DEBAJO, o llega tarde
```
  - -- `ExceptionHandlerMiddleware` escribe su "An unhandled exception has occurred" a
       nivel **Error ANTES** de llamar a ningun `IExceptionHandler`
  - -- lo puse primero dentro de `GlobalExceptionHandler` y **no servia**: la linea de
       Error ya estaba escrita. Hay que interceptar antes de que le llegue
  - -- 499 (Client Closed Request) no es del RFC pero es la convencion de nginx, y es lo
       que hace que estas peticiones **no cuenten como 5xx** en las metricas

- --- **Un timeout de base tampoco es un 500**
  - -- `SqlException.Number == -2` es el timeout de comando (el Win32 258 de dentro es
       WAIT_TIMEOUT). No es un bug nuestro ni una peticion mal formada
  - -- **503 + `Retry-After`**, porque ES reintentable y un 500 le dice al cliente lo
       contrario. Misma idea que la cabecera `transient-error` de Adyen: no basta con
       rechazar, hay que decir si el rechazo es transitorio

- --- ⚠️ **"≥500 -> mensaje generico" era una regla demasiado gruesa**
  - -- existe para no filtrar el mensaje de una excepcion NO controlada
  - -- pero un 503 que escribes tu no revela nada y su texto es accionable: censurarlo
       dejaba al cliente sin saber si podia reintentar
  - -- el corte correcto es **"¿lo mapeamos nosotros?"**, no "¿es 5xx?"
  - -- ⚠️ al cambiarlo rompi el `Detail` de los 4xx —donde el mensaje de dominio
       ("Insufficient stock for SKU 'X'") es JUSTO el util— y **lo cazo un test que ya
       existia**. Es exactamente para lo que estan

- --- ✏️ **Y un error de metodo mio, que vale mas que el hallazgo**
```sh
grep -c "\[ERR\]" api.log      # 0 SIEMPRE: Serilog escribe "[18:55:27 ERR]"
grep -cE "^\[[0-9:]+ ERR\]"    # el bueno
```
  - -- di por buenos varios "0 errores" que eran un **falso negativo del patron**
  - -- leccion: antes de celebrar un cero, comprobar que el patron casa con algo cuando
       **deberia** casar. Un grep que nunca acierta y un sistema que nunca falla se ven
       exactamente igual

- --- **Lo que se deja a proposito**
  - -- EF Core sigue escribiendo a nivel Error sus "An error occurred using the connection
       to database..." (7 lineas), cada una seguida de nuestra Information y su 499 con el
       mismo id de correlacion
  - -- bajar esa categoria a Warning escondería tambien **las caidas reales de base**, que
       es lo ultimo que uno quiere no ver
  - -- un error correlado con su explicacion en la linea siguiente es mucho mejor que una
       categoria silenciada


---
---


## 33. Sesiones revocables  <- y dos cookies que fallan en silencio

- --- **El problema de fondo de un JWT: es autocontenido**
  - -- firma buena + no expirado = valido, y no hay estado del servidor que consultar
  - -- por eso un token robado valia los 60 min enteros: ni el logout ni cambiar la
       contraseña lo invalidaban
  - -- la solucion no es "revocar el JWT" (no se puede sin romper lo que lo hace util):
       es **acortarlo** (60 -> 15 min) y darle un mecanismo de renovacion que SI sea
       revocable

- --- ⭐ **Rotacion: lo que convierte un robo en algo DETECTABLE**
```
cada refresco gasta el token y entrega otro
   -> si reaparece uno YA GASTADO, o hay dos copias circulando o el cliente
      lanzo dos refrescos a la vez
```
  - -- sin rotacion no hay nada que detectar: el ladron usa el token y nadie se entera
  - -- al detectar reuso se revoca **la familia entera**: con dos copias circulando no se
       sabe cual es la del dueño. Duro a proposito

- --- ⚠️ **Y la ventana de gracia, que NO es un parche**
  - -- sin ella la deteccion es **inutilizable en la practica**: un movil o una SPA lanzan
       peticiones en paralelo, dos reciben 401 casi a la vez, las dos refrescan con el
       mismo token y la segunda parece un ladron
  - -- resultado: cerrar la sesion de usuarios legitimos constantemente
  - -- dentro de la ventana se rechaza igual (ese token esta gastado) pero **no** se revoca
       la familia, asi que el token nuevo que ya recibio la otra peticion sigue valiendo
  - -- es el *leeway* de Auth0. El precio: un ladron que reuse en los primeros segundos
       pasa desapercibido — a cambio de poder tener el mecanismo ENCENDIDO

- --- **`UPDATE ... WHERE RevokedAt IS NULL`, no leer-y-escribir**
  - -- entre "compruebo que esta vivo" y "lo marco" cabe otra peticion, y entonces las dos
       rotan el mismo token: dos sesiones validas donde debia haber una
  - -- misma forma que el descuento de stock. La condicion va DENTRO de la sentencia

- --- **La misma division del cap. 30, otra vez**
```
GARANTIA      "esta sesion no se puede extender"  -> familia revocada en la BASE
OPTIMIZACION  "y el token que ya tienes muere ya" -> denylist de jti en Redis
```
  - -- verificado con Redis muerto: el logout **sigue cortando la sesion**; lo unico que
       sobrevive es el access token actual, y dura como mucho 15 min
  - -- que la denylist falle en abierto es correcto: corre en CADA peticion autenticada, y
       fallar en cerrado ahi convierte un corte de Redis en "nadie puede usar la API"

- --- 🔴 **DOS trampas de cookies con la MISMA forma**
```
login responde 200 -> la cookie NO se guarda -> el refresh falla SIEMPRE
   y en el servidor no hay ni un error
```
  - -- **1) el prefijo `__Host-`**: obliga al navegador a exigir `Secure` **y** `Path=/`
       **y** ningun `Domain`; si algo no cuadra **descarta la cookie sin avisar**. Aqui
       chocaba dos veces (Path acotado a `/api/v1/auth`, y HTTP en local)
  - -- **2) `Secure` decidido por el ENTORNO**: puse `!IsDevelopment()`, y el host de tests
       usa `"Testing"` y sirve por HTTP -> cookie `Secure` sobre HTTP -> **ningun test de
       sesion podia pasar**. Lo cazaron los tests
  - -- lo correcto es el **esquema real**: `Request.IsHttps`. Se ajusta solo en local, en
       tests y en produccion. ⚠️ Detras de un proxy TLS depende de `UseForwardedHeaders`
  - -- leccion: una condicion sobre el ENTORNO se rompe en cuanto alguien llama al entorno
       de otra forma. Una condicion sobre el HECHO, no

- --- ⚠️ **`AllowCredentials()` en CORS o la cookie no viaja**
  - -- sin eso el navegador **no manda** la cookie a otro origen ni acepta la respuesta que
       la establece
  - -- y combinarlo con `AllowAnyOrigin()` esta **prohibido por la especificacion** (ASP.NET
       lanza en ejecucion): es la mejor razon retrospectiva para no haber puesto el comodin

- --- **Detalles pequeños que evitan agujeros**
  - -- `Cookies.Delete` hay que llamarlo con **las mismas opciones** con las que se escribio
       (Path, SameSite): si no, el navegador lo trata como otra cookie y la original sigue
  - -- `logout` es `[AllowAnonymous]`: el caso mas habitual de cerrar sesion es que el
       access token YA haya expirado. Exigirlo dejaria al usuario sin poder salir
  - -- el logout es **idempotente**: dos veces, o sin cookie, devuelve 204. Un error dejaria
       al usuario sin saber si esta dentro o fuera
  - -- `FamilyId` en vez de seguir `ReplacedByToken`: recorrer la cadena es una consulta por
       eslabon y basta que falte uno para dejar media sesion viva
  - -- SHA-256 **a secas** para la huella, no bcrypt: el valor ya son 256 bits de un CSPRNG,
       no hay nada que adivinar por fuerza bruta y un hash lento solo añade latencia
  - -- la purga va **en el slice** (`RefreshTokenCleaner`), no en `OutboxCleaner`: meterla
       ahi haria que `Shared/Messaging` nombrara una entidad de `Accounts`

- --- ✏️ **Y otra vez el falso negativo del que mide**
  - -- mi comprobacion decia `HttpOnly=False`. Kestrel emite `httponly` en **minuscula** y yo
       comparaba sensible a mayusculas
  - -- segundo caso en dos dias (el otro, `grep "[ERR]"`). El patron: **cuando algo sale
       "mal" en una comprobacion propia, sospechar primero de la comprobacion**
