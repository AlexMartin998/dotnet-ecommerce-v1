using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Accounts.Dtos;
using ApiEcommerce.Features.Accounts.Models;
using ApiEcommerce.Features.Accounts.Service;
using ApiEcommerce.Shared.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ApiEcommerce.Tests.Features.Accounts;


/// <summary>
/// Registro y login. Casi todo lo que se prueba aquí es <b>seguridad</b>, no
/// funcionalidad: el rol que se asigna, el mensaje que se devuelve y lo que NO se
/// filtra al cliente.
/// </summary>
public class AuthServiceTests
{
  private readonly Mock<UserManager<ApplicationUser>> _users = MockUserManager();
  private readonly Mock<SignInManager<ApplicationUser>> _signIn;
  private readonly Mock<IJwtTokenService> _tokens = new();

  public AuthServiceTests()
  {
    _signIn = MockSignInManager(_users);
    _tokens.Setup(t => t.CreateToken(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
           .Returns(("jwt-de-prueba", DateTime.Now.AddHours(1)));
    _users.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync([Roles.User]);
  }

  private AuthService Sut() =>
      new(_users.Object, _signIn.Object, _tokens.Object, NullLogger<AuthService>.Instance);

  // ---- registro -----------------------------------------------------------

  [Fact]
  public async Task RegisterAsync_WhenTheUsernameIsTaken_ThrowsConflict()
  {
    _users.Setup(u => u.FindByNameAsync("ana")).ReturnsAsync(new ApplicationUser());

    var ex = await Assert.ThrowsAsync<ConflictAppException>(() => Sut().RegisterAsync(Register()));

    // 409 y no 400: el request es válido en sí mismo, choca con el estado de la base.
    Assert.Equal(HttpStatusCode.Conflict, ex.Status);
  }

  [Fact]
  public async Task RegisterAsync_WhenTheEmailIsTaken_ThrowsConflict()
  {
    _users.Setup(u => u.FindByNameAsync("ana")).ReturnsAsync((ApplicationUser?)null);
    _users.Setup(u => u.FindByEmailAsync("ana@test.com")).ReturnsAsync(new ApplicationUser());

    await Assert.ThrowsAsync<ConflictAppException>(() => Sut().RegisterAsync(Register()));
  }

  [Fact]
  public async Task RegisterAsync_WhenIdentityRejectsThePassword_Throws422WithTheFieldName()
  {
    SetupFreeCredentials();
    _users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Failed(new IdentityError
          {
            Code = "PasswordTooShort", Description = "Passwords must be at least 6 characters."
          }));

    var ex = await Assert.ThrowsAsync<ValidationAppException>(() => Sut().RegisterAsync(Register()));

    // 422 y no 400: la FORMA del DTO era válida (eso ya lo filtró DataAnnotations); lo
    // que falla es la política de Identity. Y llega agrupado por campo, en el mismo
    // formato que produce ValidationProblem(ModelState).
    Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.Status);
    Assert.True(ex.Errors.ContainsKey(nameof(RegisterUserDto.Password)));
  }

  [Fact]
  public async Task RegisterAsync_AlwaysAssignsTheUserRoleAndNeverAdmin()
  {
    // ⚠️ EL test de seguridad del registro. El rol NO se lee del body bajo ninguna
    // circunstancia: si se pudiera, cualquiera se registra como admin.
    SetupFreeCredentials();
    _users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Success);
    _users.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Success);

    await Sut().RegisterAsync(Register());

    _users.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), Roles.User), Times.Once);
    _users.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), Roles.Admin), Times.Never);
  }

  [Fact]
  public async Task RegisterAsync_WhenTheRoleCannotBeAssigned_DeletesTheHalfCreatedUser()
  {
    // Sin esto quedaría una cuenta creada y sin permisos: puede iniciar sesión y no
    // puede hacer nada, y su username queda ocupado para siempre.
    SetupFreeCredentials();
    _users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
          .ReturnsAsync(IdentityResult.Success);
    _users.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), Roles.User))
          .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "RoleNotFound", Description = "no existe" }));
    _users.Setup(u => u.DeleteAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

    await Assert.ThrowsAsync<ValidationAppException>(() => Sut().RegisterAsync(Register()));

    _users.Verify(u => u.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Once);
  }

  [Fact]
  public async Task RegisterAsync_TrimsTheCredentials()
  {
    // "ana " y "ana" son la misma persona para todo el mundo menos para un índice único.
    _users.Setup(u => u.FindByNameAsync("ana")).ReturnsAsync((ApplicationUser?)null).Verifiable();
    _users.Setup(u => u.FindByEmailAsync("ana@test.com")).ReturnsAsync((ApplicationUser?)null).Verifiable();
    _users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
    _users.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), Roles.User)).ReturnsAsync(IdentityResult.Success);

    await Sut().RegisterAsync(new RegisterUserDto
    {
      Username = "  ana  ", Email = "  ana@test.com  ", Password = "Passw0rd!", Name = "  Ana  "
    });

    _users.Verify();
    _users.Verify(u => u.CreateAsync(It.Is<ApplicationUser>(x => x.UserName == "ana" && x.Name == "Ana"),
                                     It.IsAny<string>()), Times.Once);
  }

  // ---- login --------------------------------------------------------------

  [Fact]
  public async Task LoginAsync_WithAnUnknownUser_Throws401WithTheGenericMessage()
  {
    _users.Setup(u => u.FindByNameAsync("fantasma")).ReturnsAsync((ApplicationUser?)null);

    var ex = await Assert.ThrowsAsync<UnauthorizedAppException>(
        () => Sut().LoginAsync(new LoginUserDto { Username = "fantasma", Password = "x" }));

    Assert.Equal("Invalid username or password.", ex.Message);
  }

  [Fact]
  public async Task LoginAsync_WithAWrongPassword_ThrowsTheExactSameMessage()
  {
    // ⚠️ EL test de seguridad del login, y hay que compararlo con el de arriba: si los
    // mensajes difieren, el login se convierte en un ORÁCULO para enumerar usuarios
    // ("este existe, este no"). Por eso los dos tests afirman el mismo string literal.
    var user = new ApplicationUser { UserName = "ana" };
    _users.Setup(u => u.FindByNameAsync("ana")).ReturnsAsync(user);
    _signIn.Setup(s => s.CheckPasswordSignInAsync(user, "mala", true)).ReturnsAsync(SignInResult.Failed);

    var ex = await Assert.ThrowsAsync<UnauthorizedAppException>(
        () => Sut().LoginAsync(new LoginUserDto { Username = "ana", Password = "mala" }));

    Assert.Equal("Invalid username or password.", ex.Message);
    Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);
  }

  [Fact]
  public async Task LoginAsync_WhenTheAccountIsLockedOut_Throws403AndNot401()
  {
    // 403 y no 401: aquí SÍ sabemos quién eres; lo que pasa es que no puedes ahora
    // mismo. Devolver 401 haría que el cliente reintentara pidiendo credenciales,
    // gastando intentos y alargando el bloqueo.
    var user = new ApplicationUser { UserName = "ana" };
    _users.Setup(u => u.FindByNameAsync("ana")).ReturnsAsync(user);
    _signIn.Setup(s => s.CheckPasswordSignInAsync(user, It.IsAny<string>(), true))
           .ReturnsAsync(SignInResult.LockedOut);

    var ex = await Assert.ThrowsAsync<ForbiddenAppException>(
        () => Sut().LoginAsync(new LoginUserDto { Username = "ana", Password = "x" }));

    Assert.Equal(HttpStatusCode.Forbidden, ex.Status);
  }

  [Fact]
  public async Task LoginAsync_CountsFailedAttempts()
  {
    // lockoutOnFailure: true. Sin ese flag Identity NUNCA cuenta fallos y el bloqueo
    // configurado en AddIdentity no se dispara jamás: la protección existe en la
    // configuración y no en la práctica.
    var user = new ApplicationUser { UserName = "ana" };
    _users.Setup(u => u.FindByNameAsync("ana")).ReturnsAsync(user);
    _signIn.Setup(s => s.CheckPasswordSignInAsync(user, "buena", true)).ReturnsAsync(SignInResult.Success);

    await Sut().LoginAsync(new LoginUserDto { Username = "ana", Password = "buena" });

    _signIn.Verify(s => s.CheckPasswordSignInAsync(user, "buena", true), Times.Once);
  }

  [Fact]
  public async Task LoginAsync_OnSuccess_ReturnsTheTokenAndTheUserWithItsRoles()
  {
    var user = new ApplicationUser { UserName = "ana", Email = "ana@test.com" };
    _users.Setup(u => u.FindByNameAsync("ana")).ReturnsAsync(user);
    _signIn.Setup(s => s.CheckPasswordSignInAsync(user, It.IsAny<string>(), true)).ReturnsAsync(SignInResult.Success);

    var response = await Sut().LoginAsync(new LoginUserDto { Username = "ana", Password = "buena" });

    Assert.Equal("jwt-de-prueba", response.Token);
    Assert.Equal("ana", response.User.Username);
    Assert.Contains(Roles.User, response.User.Roles);
  }

  // ---- perfil -------------------------------------------------------------

  [Fact]
  public async Task GetProfileAsync_WithAnUnknownId_ThrowsNotFound()
  {
    _users.Setup(u => u.FindByIdAsync("abc")).ReturnsAsync((ApplicationUser?)null);

    await Assert.ThrowsAsync<NotFoundAppException>(() => Sut().GetProfileAsync("abc"));
  }

  [Fact]
  public async Task NullArguments_FailFast()
  {
    await Assert.ThrowsAsync<ArgumentNullException>(() => Sut().RegisterAsync(null!));
    await Assert.ThrowsAsync<ArgumentNullException>(() => Sut().LoginAsync(null!));
  }

  // ---- helpers ------------------------------------------------------------

  private void SetupFreeCredentials()
  {
    _users.Setup(u => u.FindByNameAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
    _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
  }

  private static RegisterUserDto Register() => new()
  {
    Username = "ana", Email = "ana@test.com", Password = "Passw0rd!", Name = "Ana"
  };

  /// <summary>
  /// <c>UserManager</c> y <c>SignInManager</c> son clases concretas con constructores
  /// enormes; Moq las puede simular porque sus métodos son <c>virtual</c>, pero hay que
  /// pasarles los argumentos posicionales. Es feo y es el precio de que Identity no
  /// exponga interfaces.
  /// </summary>
  private static Mock<UserManager<ApplicationUser>> MockUserManager()
      => new(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

  private static Mock<SignInManager<ApplicationUser>> MockSignInManager(Mock<UserManager<ApplicationUser>> users)
      => new(users.Object,
             Mock.Of<IHttpContextAccessor>(),
             Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
             null!, null!, null!, null!);
}
