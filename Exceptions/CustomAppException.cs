using System.Net;

namespace ApiEcommerce.Exceptions;

public class CustomAppException(string code, string message, HttpStatusCode status) : AppException(code, message, status)
{
}
