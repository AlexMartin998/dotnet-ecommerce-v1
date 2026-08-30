# 07 — Compra y condiciones de carrera  ✅

Contrato: [`features/07_compra-y-concurrencia.feature`](../features/07_compra-y-concurrencia.feature) · Commit `63269ac`

## Hecho
- [x] `IProductRepository.TryDecrementStockAsync` con `ExecuteUpdateAsync` (UPDATE condicional atómico)
- [x] Índice único en `Category.Name` + `[MaxLength(50)]` en la entidad
- [x] `Product.RowVersion` (`[Timestamp]`) para el PATCH
- [x] `GlobalExceptionHandler` recorre la cadena de `InnerException` → 2601/2627, 1205, 547
- [x] `ITransactionRunner`: la transacción baja al servicio

## La rectificación que importa
Se implementó primero con **`RowVersion` + reintentos** y **se midió que no servía**:
15 compras sobre stock 10 → **5×200 + 5×409**, stock 5. No sobrevendía, pero **rechazaba
compras válidas**. Con UPDATE condicional atómico: **10×200 + 5×409, stock 0**.

| Herramienta | Para qué |
|---|---|
| UPDATE condicional atómico | contadores (stock, saldo, cupos) |
| `RowVersion` | editar una entidad |
| Índice único en la base | unicidad |

## Trampas registradas
- `nvarchar(max)` **no es indexable**: `[MaxLength]` va en la **entidad**.
- `ExecuteUpdateAsync` no dispara la auditoría, no toca el change tracker, y `DateTime.Now`
  dentro del árbol de expresión se traduce a `GETDATE()` (reloj del servidor SQL).
- **`[Transactional]` no puede dar una unidad reintentable**: `ActionExecutionDelegate` no
  es reentrante. Se le puso una guarda que falla ruidosamente.
- Sin traducir las excepciones de EF, **arreglar la carrera empeora la respuesta**: el 409
  correcto se vuelve 500.

## Abierto
- **`RowVersion` no cierra el *lost update* entre dos admins** (el token no se expone).
  Cerrarlo = `ETag` + `If-Match` → [`12`](12_deuda-revision-multiagente.md).
- Si el producto se borra entre la lectura y el decremento, sale 409 "sin stock" en vez de 404.
