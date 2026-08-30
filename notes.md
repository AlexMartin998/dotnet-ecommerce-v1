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
