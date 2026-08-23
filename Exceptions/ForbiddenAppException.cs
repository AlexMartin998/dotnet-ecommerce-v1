using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>403 — autenticado pero sin permiso sobre el recurso ("sé quién eres y no puedes").</summary>
public sealed class ForbiddenAppException(string message = "You are not allowed to perform this operation.")
    : AppException("forbidden", message, HttpStatusCode.Forbidden)
{
}
