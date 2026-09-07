# 22 — Pagos (`Features/Payments`)

> Quinto contexto acotado. Cobra de verdad contra Stripe, con el proveedor elegido **por
> petición** para que añadir PayPal sea una clase y una línea de DI.
>
> Depende de: `20_ordenes-y-comprobante`. Cambia el ciclo de vida de la orden.

---

## 0. Las tres decisiones que lo ordenan

1. **La orden nace `Placed`, no `Paid`.** Hoy `OrderService` la deja pagada al colocarla, lo
   que es cómodo y mentira. A partir de aquí, el único que puede pasarla a `Paid` es un pago
   **capturado por la pasarela**. Es lo que hace que Payments sea un contexto acotado y no
   una tabla más.
2. **El proveedor lo elige el comprador → Strategy + factory.** ⚠️ Esto va **contra** la
   regla general del repo (`CLAUDE.md` §5.4: la implementación de un puerto se elige una vez
   en el composition root), y a propósito: allí la elección la hace la **infraestructura**
   (disco vs S3), aquí la hace **cada petición**. Cuando el que elige es el request, Strategy
   es la respuesta correcta.
3. **El webhook es la única fuente de verdad del cobro.** La respuesta de
   `POST /payment` solo dice «intento creado». Nadie marca un pago como cobrado por haber
   llamado a la API: eso lo dice la pasarela, firmado.

---

## 1. Modelo

```
Payment
  Id, Reference           PAY-2026-000001, por secuencia (como Order.Number)
  OrderId, BuyerUserId    a quién pertenece; el filtro por comprador va en la consulta
  Provider                stripe | (paypal)         <- enum, persistido como string
  ProviderPaymentId       el id del PaymentIntent en Stripe   <- índice ÚNICO
  Amount, Currency        congelados del total de la orden
  Status                  pending | captured | failed | cancelled
  FailureReason?          lo que dijo la pasarela, para poder responder al cliente
  CreatedAt, UpdatedAt

ProcessedWebhookEvent     dedupe del webhook: Id = el id del evento de la pasarela
  Id (PK), Provider, Type, ReceivedAt
```

- [ ] `ProviderPaymentId` con índice **único**: es lo que ata el webhook a nuestra fila, y
      repetido haría que dos pagos se pisaran.
- [ ] Sin `Authorized` en el enum: con captura automática ese estado no existe, y un estado
      inalcanzable es exactamente el bug de `ReceiptStatus.Failed` de `planning/21`.

## 2. Puerto y Strategy

- [ ] `IPaymentGateway`: `Provider`, `CreateIntentAsync(PaymentRequest)`, `ParseEvent(payload, signature)`.
- [ ] `IPaymentGatewayRegistry.For(provider)` — resuelve `IEnumerable<IPaymentGateway>` a un
      diccionario por `Provider`. **Añadir PayPal = una clase + un `AddSingleton`.**
- [ ] Proveedor desconocido → **400** enumerando los disponibles, no 500.
- [ ] Ninguno configurado → la API **arranca igual** y `POST /payment` da **503**. Cobrar no
      es una optimización: no degrada en abierto (`rules.md` §8).
- [ ] `StripePaymentGateway` con `Stripe.net`: `PaymentIntentService.CreateAsync`, y
      `EventUtility.ConstructEvent` para verificar la firma **con tolerancia temporal**.

## 3. Flujo

```
POST /api/v1/payment  { orderId, provider }
  -> IIdempotentCommandRunner  (marca + efecto en la MISMA transaccion)
       valida: la orden es mia, esta en Placed, no tiene pago capturado
       fila Payment(pending)  +  llamada a la pasarela  -> ProviderPaymentId
  -> 201 { reference, clientSecret }

POST /api/v1/payment/webhook/{provider}     [AllowAnonymous]
  -> firma valida? (si no: 400)   -> evento ya visto? (si: 200 y fuera)
  -> transaccion: marcar el evento + mover el Payment + encolar payment.captured
  -> 200

payment.captured  -> Ordering/OrderPaymentConsumer
  -> orden Placed -> Paid   +   encola order.paid
order.paid        -> Ordering/OrderPaidConsumer
  -> genera el comprobante   (antes lo hacia order.placed)
```

- [ ] ⚠️ La llamada a la pasarela va **dentro** de la transacción, como el PDF: si el commit
      falla queda un PaymentIntent huérfano en Stripe, que es basura, frente a un cobro sin
      fila que lo recuerde, que es dinero perdido. Misma decisión que en `planning/20`, y por
      la misma razón.
- [ ] El webhook es `[AllowAnonymous]`: **la firma es la autenticación**. Y se excluye del
      rate limiter de auth, o un pico de reintentos de Stripe se auto-bloquearía.
- [ ] Hay que leer el **cuerpo crudo** para verificar la firma: el JSON reserializado no
      produce el mismo HMAC.

## 4. La deuda que crea mover `Paid` más adelante

- [ ] Una orden `Placed` que nadie paga **retiene stock para siempre**. `AbandonedOrderCleaner`
      (`PeriodicBackgroundService`) la cancela pasada `Payments:ReservationMinutes` y devuelve
      el stock por `ICatalogGateway.ReturnAsync`.
- [ ] La devolución y el `placed -> cancelled` van en la **misma transacción**, y solo esa
      transición autoriza a devolver: es lo que lo hace idempotente si pasa dos veces.
- [ ] El efecto vive fuera del `BackgroundService` (`IAbandonedOrderCollector`) para poder
      probarlo sin esperar horas. Lección de `planning/18`, ya aplicada dos veces.

## 5. Verificación

- [ ] Unitarios: registry (elige, rechaza, enumera), máquina de estados, mapeo de eventos.
- [ ] Integración **sin tocar Stripe**: `IPaymentGateway` mockeado → flujo completo
      pago → webhook → orden `Paid` → comprobante.
- [ ] Webhook **firmado a mano** con el `WebhookSecret` de pruebas: firma buena → 200, firma
      mala → 400, marca de tiempo vieja → 400, mismo evento dos veces → una sola transición.
- [ ] Contra Stripe **de verdad** (necesita `sk_test_…` del owner): crear un PaymentIntent y
      comprobar que vuelve con `client_secret`.
- [ ] Concurrencia: dos webhooks del mismo evento **simultáneos** → una sola transición.
- [ ] Arranque en `Production` sin claves → arranca, y `POST /payment` da 503.

## 6. Lo que este paso NO hace

- **Reembolsos.** Tienen sus propias invariantes y su propio flujo de webhook.
- **Pagos parciales o en varias monedas.** El importe se congela del total de la orden.
- **PayPal.** El punto de todo el diseño es que quepa después sin tocar nada; añadirlo ahora
  sin necesitarlo es adivinar su forma.
- **Reintentar un pago fallido.** Hoy se pide uno nuevo sobre la misma orden.
