using Microsoft.EntityFrameworkCore;
using ApiEcommerce.Data;
using ApiEcommerce.Repository;
using ApiEcommerce.Service;
using ApiEcommerce.Mapping;


var builder = WebApplication.CreateBuilder(args);


// // // Add SERVICES to the container ---------------------------------
// DB ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ConexionSql"))
);


// AutoMapper ----
// registra todos los Profile dentro del assembly donde está CategoryProfile
builder.Services.AddAutoMapper(typeof(CategoryProfile).Assembly);


// DI ----
// repositorios (genérico + específicos)
builder.Services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();

// servicios (genérico + específicos)
builder.Services.AddScoped(typeof(IGenericService<>), typeof(GenericService<>));
builder.Services.AddScoped<ICategoryService, CategoryService>();

// Controllers ----
builder.Services.AddControllers();

// Swagger / OpenAPI ----
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// // // Configure the HTTP request pipeline ------------------------------

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
