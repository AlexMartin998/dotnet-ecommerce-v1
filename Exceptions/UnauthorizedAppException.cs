using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>401 — falta credencial o el token no es válido ("no sé quién eres").</summary>
public sealed class UnauthorizedAppException(string message = "Authentication is required.")
    : AppException("unauthorized", message, HttpStatusCode.Unauthorized)
{
}
