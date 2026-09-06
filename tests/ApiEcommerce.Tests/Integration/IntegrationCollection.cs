namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Todos los tests de integración comparten UNA instancia de <see cref="ApiFactory"/> y
/// corren en serie.
/// </summary>
/// <remarks>
/// <b>Sin paralelismo a propósito.</b> Comparten una base de datos real: dos clases
/// creando y borrando categorías a la vez producen fallos intermitentes que no señalan
/// ningún bug. ⚠️ Y no basta con las que usan el fixture: las que levantan su PROPIO host
/// (degradación, arranque) también van aquí, porque comparten la misma base y en paralelo
/// dos <c>MigrateAsync</c> chocan con <c>Database 'ApiEcommerceNET8_Tests' already exists</c>.
/// Pasaban en aislado y fallaban en la suite completa, que es el peor modo de fallo. Los tests que SÍ deben ser concurrentes lo son dentro de su propio test,
/// con <c>Task.WhenAll</c>, que es donde la concurrencia se está probando de verdad.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationCollection : ICollectionFixture<ApiFactory>
{
  public const string Name = "integration";
}
