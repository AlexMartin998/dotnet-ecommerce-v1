using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Ordering.Dtos;
using ApiEcommerce.Features.Ordering.Models;
using ApiEcommerce.Features.Ordering.Ports;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Features.Ordering.Service;
using ApiEcommerce.Shared.Documents;
using ApiEcommerce.Shared.Idempotency;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ApiEcommerce.Tests.Features.Ordering;


/// <summary>
/// Mover una orden por su ciclo de entrega. Lo que se prueba aquí es que la decisión la
/// toma el UPDATE condicional, y que el servicio solo lee cuando ese UPDATE no movió nada.
/// </summary>
public class OrderFulfillmentTests
{
  private readonly Mock<IOrderRepository> _repository = new();

  private OrderService Sut() => new(
      _repository.Object,
      Mock.Of<ICatalogGateway>(),
      Mock.Of<IDocumentStore>(),
      Mock.Of<IIdempotentCommandRunner>(),
      NullLogger<OrderService>.Instance);

  private void TransitionSucceeds(OrderStatus from, OrderStatus to)
      => _repository.Setup(r => r.TryTransitionAsync(7, from, to, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);

  private void NothingMoves()
      => _repository.Setup(r => r.TryTransitionAsync(
              It.IsAny<int>(), It.IsAny<OrderStatus>(), It.IsAny<OrderStatus>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(false);

  private static UpdateOrderStatusDto To(string status) => new() { Status = status };

  // ---- el camino feliz -----------------------------------------------------

  [Theory]
  [InlineData("preparing", OrderStatus.Paid, OrderStatus.Preparing)]
  [InlineData("shipped", OrderStatus.Preparing, OrderStatus.Shipped)]
  [InlineData("delivered", OrderStatus.Shipped, OrderStatus.Delivered)]
  public async Task AdvanceAsync_MovesOnlyFromTheStateThatTargetDemands(
      string target, OrderStatus from, OrderStatus to)
  {
    TransitionSucceeds(from, to);

    await Sut().AdvanceAsync(7, To(target));

    // El origen lo pone el servidor: si viniera del cliente se podría saltar un paso.
    _repository.Verify(r => r.TryTransitionAsync(7, from, to, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task AdvanceAsync_AcceptsTheStatusInAnyCasing()
  {
    TransitionSucceeds(OrderStatus.Paid, OrderStatus.Preparing);

    await Sut().AdvanceAsync(7, To("PREPARING"));
  }

  [Fact]
  public async Task AdvanceAsync_WhenItMoves_DoesNotReadTheOrder()
  {
    // Leer antes de escribir sería read-then-write: dos administradores a la vez podrían
    // saltarse un paso. Y leer después, si movió, es un viaje de más.
    TransitionSucceeds(OrderStatus.Paid, OrderStatus.Preparing);

    await Sut().AdvanceAsync(7, To("preparing"));

    _repository.Verify(
        r => r.FindStatusAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  // ---- los tres casos en que el UPDATE no mueve nada ------------------------

  [Fact]
  public async Task AdvanceAsync_WhenTheOrderIsAlreadyThere_IsANoOp()
  {
    // Reenviar la misma transición no es un error: es un duplicado, y por eso este endpoint
    // no necesita Idempotency-Key.
    NothingMoves();
    _repository.Setup(r => r.FindStatusAsync(7, It.IsAny<CancellationToken>()))
               .ReturnsAsync(OrderStatus.Shipped);

    await Sut().AdvanceAsync(7, To("shipped"));
  }

  [Fact]
  public async Task AdvanceAsync_FromTheWrongState_Conflicts()
  {
    NothingMoves();
    _repository.Setup(r => r.FindStatusAsync(7, It.IsAny<CancellationToken>()))
               .ReturnsAsync(OrderStatus.Placed);

    var error = await Assert.ThrowsAsync<CustomAppException>(
        () => Sut().AdvanceAsync(7, To("preparing")));

    Assert.Equal("invalid_transition", error.Code);
  }

  [Fact]
  public async Task AdvanceAsync_OnAnOrderThatDoesNotExist_IsNotFound()
  {
    NothingMoves();
    _repository.Setup(r => r.FindStatusAsync(7, It.IsAny<CancellationToken>()))
               .ReturnsAsync((OrderStatus?)null);

    await Assert.ThrowsAsync<NotFoundAppException>(() => Sut().AdvanceAsync(7, To("preparing")));
  }

  // ---- destinos que un administrador no alcanza ----------------------------

  [Theory]
  [InlineData("paid")]       // lo mueve un cobro capturado, no un administrador
  [InlineData("cancelled")]  // devolver stock tiene sus propias invariantes
  [InlineData("placed")]
  [InlineData("refunded")]   // no existe todavía
  [InlineData("")]
  public async Task AdvanceAsync_WithAnUnreachableTarget_IsABadRequest(string target)
  {
    var error = await Assert.ThrowsAsync<BadOperationAppException>(
        () => Sut().AdvanceAsync(7, To(target)));

    // Enumera los válidos, igual que con los proveedores de pago.
    Assert.Contains("preparing", error.Message);

    _repository.Verify(
        r => r.TryTransitionAsync(It.IsAny<int>(), It.IsAny<OrderStatus>(),
                                  It.IsAny<OrderStatus>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  // ---- contadores ----------------------------------------------------------

  [Fact]
  public async Task GetStatsAsync_FillsTheStatesWithNoRows()
  {
    // Un estado sin filas no sale en el GROUP BY, y el panel mostraría un hueco.
    _repository.Setup(r => r.CountByStatusAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(new Dictionary<OrderStatus, int>
               {
                 [OrderStatus.Placed] = 2,
                 [OrderStatus.Paid] = 3
               });

    var stats = await Sut().GetStatsAsync();

    Assert.Equal(5, stats.Total);
    Assert.Equal(2, stats.Placed);
    Assert.Equal(3, stats.Paid);
    Assert.Equal(0, stats.Delivered);
    Assert.Equal(0, stats.Cancelled);
  }
}
