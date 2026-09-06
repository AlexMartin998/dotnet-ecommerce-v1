using System.Net;

namespace ApiEcommerce.Exceptions;

/// <summary>Error de negocio con código y estado a medida, para los casos sin clase propia.</summary>
public class CustomAppException(string code, string message, HttpStatusCode status) : AppException(code, message, status)
{
}
