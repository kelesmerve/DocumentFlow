using DocumentFlow.Domain;

namespace DocumentFlow.Application.Authentication;

public sealed record LoginRequest(string Email, string Password);
public sealed record AuthenticatedUser(Guid Id, string Email, string FirstName, string LastName, UserRole Role);
public sealed record LoginResult(string AccessToken, DateTime ExpiresAtUtc, AuthenticatedUser User);
public interface IAuthenticationService
{
    Task<LoginResult?> LoginAsync(string email, string password, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> GetUserAsync(Guid id, CancellationToken cancellationToken);
}
