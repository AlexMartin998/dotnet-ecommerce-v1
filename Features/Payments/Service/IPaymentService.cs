using ApiEcommerce.Features.Payments.Dtos;
using ApiEcommerce.Features.Payments.Models;
using ApiEcommerce.Shared.Idempotency;
using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Features.Payments.Service;


/// <summary>Cobrar una orden y reaccionar a lo que diga la pasarela.</summary>
public interface IPaymentService
{
  /// <summary>Crea el intento de cobro de una orden del comprador.</summary>
  /// <remarks>
  /// La respuesta NO significa que se haya cobrado: solo que la pasarela aceptó el intento.
  /// Lo que mueve el pago a <c>captured</c> es el webhook, firmado.
  /// </remarks>
  /// <exception cref="Exceptions.NotFoundAppException">La orden no existe o no es suya.</exception>
  /// <exception cref="Exceptions.ConflictAppException">Ya tiene un pago vivo.</exception>
  /// <exception cref="Exceptions.CustomAppException">
  /// 503 si no hay pasarela configurada; 400 si el proveedor pedido no existe.
  /// </exception>
  Task<CommandOutcome<PaymentDto>> StartAsync(
      StartPaymentDto dto, CommandIntent intent, string buyerUserId, string? buyerEmail,
      CancellationToken ct = default);

  /// <summary>Procesa un webhook ya recibido: verifica, deduplica y mueve el pago.</summary>
  /// <remarks>
  /// Devuelve normalmente ante un evento desconocido o de un pago que no está en la base:
  /// contestar con error haría que la pasarela reintentara para siempre algo que nunca va a
  /// existir.
  /// </remarks>
  /// <exception cref="Exceptions.BadOperationAppException">La firma no es válida o caducó.</exception>
  Task HandleWebhookAsync(
      PaymentProvider provider, string rawPayload, string? signatureHeader,
      CancellationToken ct = default);

  /// <exception cref="Exceptions.NotFoundAppException">No existe o no es suyo.</exception>
  Task<PaymentDto> GetForBuyerAsync(int id, string buyerUserId, CancellationToken ct = default);

  Task<PagedResult<PaymentDto>> GetPagedForBuyerAsync(
      PageQuery query, string buyerUserId, CancellationToken ct = default);

  /// <summary>Todos los pagos. Quien llame tiene que exigir el rol antes.</summary>
  Task<PagedResult<PaymentDto>> GetPagedForAdminAsync(
      PageQuery query, string? reference, CancellationToken ct = default);
}
