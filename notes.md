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
- --- s
































