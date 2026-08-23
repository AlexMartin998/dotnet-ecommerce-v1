using ApiEcommerce.Shared.DependencyInjection;


var builder = WebApplication.CreateBuilder(args);


// // // Add SERVICES to the container ---------------------------------
// Cada bloque vive en Shared/DependencyInjection/ServiceCollectionExtensions.cs
builder.Services.AddPersistence(builder.Configuration);   // EF Core + SQL Server
builder.Services.AddObjectMapping();                      // AutoMapper (un Profile por entidad)
builder.Services.AddRepositories();                       // IBaseRepository<> + repos por entidad
builder.Services.AddApplicationServices();                // CrudService + reglas + servicios
builder.Services.AddErrorHandling();                      // GlobalExceptionHandler + ProblemDetails

// Controllers ----
builder.Services.AddControllers();

// Swagger / OpenAPI ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// // // Configure the HTTP request pipeline ------------------------------

// Lo más arriba posible: solo captura lo que ocurre DESPUÉS de él.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // swagger only in development -----
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

// endpoints ----
app.MapControllers();

app.Run();
