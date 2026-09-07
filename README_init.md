```sh
# 1. Crear nuevo proyecto de API Web
dotnet new webapi -o ProductApi --framework net9.0
cd ProductApi

# 2. Estructura inicial del proyecto:
# Controllers/      -> Controladores de la API
# Models/           -> Modelos de datos
# Program.cs        -> Configuración inicial
# appsettings.json  -> Configuraciones



# SQL Server DB --------
# appsettings.json
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Design
# install global tool for migrations
dotnet tool install --global dotnet-ef
# create migrations and run it
dotnet ef migrations add AddUsersTable
dotnet ef database update



# JWT --------
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer

```





# 1. Crear nuevo proyecto de API Web
dotnet new webapi -o ProductApi --framework net8.0
cd ProductApi

# 2. Estructura inicial del proyecto:
# Controllers/      -> Controladores de la API
# Models/           -> Modelos de datos
# Program.cs        -> Configuración inicial
# appsettings.json  -> Configuraciones

# 3. Crear modelo de datos (Models/Product.cs)
using System.ComponentModel.DataAnnotations;

namespace ProductApi.Models;

public class Product
{
    public int Id { get; set; }
    
    [Required]
    public string? Name { get; set; }
    
    [Range(0.01, double.MaxValue)]
    public decimal Price { get; set; }
    
    public bool InStock { get; set; } = true;
}

# 4. Crear repositorio en memoria (Services/ProductRepository.cs)
using ProductApi.Models;
using System.Collections.Concurrent;

namespace ProductApi.Services;

public interface IProductRepository
{
    IEnumerable<Product> GetAll();
    Product? GetById(int id);
    Product Create(Product product);
    void Update(int id, Product product);
    void Delete(int id);
}

public class ProductRepository : IProductRepository
{
    private readonly ConcurrentDictionary<int, Product> _products = new();
    private int _nextId = 1;

    public IEnumerable<Product> GetAll() => _products.Values;
    
    public Product? GetById(int id) => _products.GetValueOrDefault(id);
    
    public Product Create(Product product)
    {
        product.Id = _nextId++;
        _products[product.Id] = product;
        return product;
    }
    
    public void Update(int id, Product product)
    {
        if (!_products.ContainsKey(id))
            throw new KeyNotFoundException();
        
        product.Id = id;
        _products[id] = product;
    }
    
    public void Delete(int id) => _products.TryRemove(id, out _);
}

# 5. Configurar servicios en Program.cs
using ProductApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<IProductRepository, ProductRepository>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

# 6. Crear controlador (Controllers/ProductsController.cs)
using Microsoft.AspNetCore.Mvc;
using ProductApi.Models;
using ProductApi.Services;

namespace ProductApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ProductsController : ControllerBase
{
    private readonly IProductRepository _repository;
    
    public ProductsController(IProductRepository repository)
    {
        _repository = repository;
    }

    // GET: api/products
    [HttpGet]
    public ActionResult<IEnumerable<Product>> Get()
    {
        return Ok(_repository.GetAll());
    }

    // GET api/products/5
    [HttpGet("{id}")]
    public ActionResult<Product> Get(int id)
    {
        var product = _repository.GetById(id);
        return product is null ? NotFound() : Ok(product);
    }

    // POST api/products
    [HttpPost]
    public ActionResult<Product> Post([FromBody] Product product)
    {
        var createdProduct = _repository.Create(product);
        return CreatedAtAction(nameof(Get), new { id = createdProduct.Id }, createdProduct);
    }

    // PUT api/products/5
    [HttpPut("{id}")]
    public IActionResult Put(int id, [FromBody] Product product)
    {
        try
        {
            _repository.Update(id, product);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // DELETE api/products/5
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        _repository.Delete(id);
        return NoContent();
    }
}

# 7. Ejecutar la aplicación (dentro del Dev Container)
dotnet run

# 8. Probar los endpoints (desde el navegador o Postman)
# Swagger UI: https://localhost:7101/swagger
# Endpoints:
# GET    /api/products
# GET    /api/products/{id}
# POST   /api/products
# PUT    /api/products/{id}
# DELETE /api/products/{id}

# 9. Ejemplo de solicitud POST (Body JSON):
{
  "name": "Laptop Gamer",
  "price": 1599.99,
  "inStock": true
}

# 10. Configurar para recarga automática durante el desarrollo
```sh
dotnet watch run
```































<!-- -------------------------------------- -->

```sh
DTO
 │
 ├── Required
 ├── MinLength
 ├── MaxLength
 └── RegularExpression
 │
 ▼
[ApiController]
 │
 ├── inválido → 400
 │
 ▼
Service
 │
 ▼
Rules
 │
 ├── regla incumplida → AppException
 │                       ↓
 │                  GlobalHandler
 │                       ↓
 │                  409 / 422 / etc.
 │
 ▼
CrudService
 │
 ▼
Repository
 │
 ▼
EF Core
```

```sh
Controller
    ↓
CategoryService
    ↓
CrudService<Category, ...>
    ↓
Repository + Rules + Mapper
    ↓
AppDbContext
    ↓
SQL Server
```

---

## Puesta en marcha tras clonar (⚠️ obligatorio)

El repo **no lleva ningún secreto**, tampoco los de desarrollo. Sin estos tres valores la
aplicación **no arranca** — y es a propósito: `JwtOptions` usa `ValidateOnStart`, así que
una clave de firma ausente falla en el arranque con un mensaje claro en vez de aparecer en
el primer login.

```sh
dotnet user-secrets set "ConnectionStrings:ConexionSql" \
  "Server=172.17.0.1,1434;Database=ApiEcommerceNET8;User ID=SA;Password=<la tuya>;TrustServerCertificate=true"

# 32+ caracteres. Generar una: openssl rand -base64 48
dotnet user-secrets set "Jwt:SecretKey" "<clave larga>"

# Solo si Seed:Enabled es true (lo es en desarrollo)
dotnet user-secrets set "Seed:AdminPassword" "Admin123!"

dotnet user-secrets list      # comprobar
```

Se guardan en `~/.microsoft/usersecrets/apiecommerce-dev-2026/secrets.json`, **fuera del
repo**. El resto de la configuración de desarrollo (Redis, RabbitMQ, CORS, Serilog) sí está
en `appsettings.Development.json`, commiteado, porque no es sensible.

Para los **tests** no hace falta nada: usan su propia base (`ApiEcommerceNET8_Tests`) y
traen los valores por defecto de la infraestructura local. Se pueden apuntar a otro sitio
con `TEST_SQL_HOST`, `TEST_SQL_PORT`, `TEST_SQL_PASSWORD` y `TEST_REDIS`, que es lo que
hace la CI.
