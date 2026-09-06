namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Todos los tests de integración comparten una instancia de <see cref="ApiFactory"/> y
/// corren en serie.
/// </summary>
/// <remarks>
/// Sin paralelismo porque comparten una base real, y también las clases que levantan su
/// propio host: dos <c>MigrateAsync</c> a la vez chocan al crear la base. La concurrencia
/// que sí hay que probar va dentro de cada test con <c>Task.WhenAll</c>.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationCollection : ICollectionFixture<ApiFactory>
{
  public const string Name = "integration";
}
