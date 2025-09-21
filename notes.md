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










































