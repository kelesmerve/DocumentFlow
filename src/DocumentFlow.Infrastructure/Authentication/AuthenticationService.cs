using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DocumentFlow.Application.Authentication;
using DocumentFlow.Domain;
using DocumentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DocumentFlow.Infrastructure.Authentication;

public sealed class AuthenticationService(DocumentFlowDbContext db, IPasswordHasher<User> hasher, IOptions<JwtOptions> options) : IAuthenticationService
{
    private readonly JwtOptions _jwt = options.Value;
    public async Task<LoginResult?> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null || !user.IsActive || hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed) return null;
        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpirationMinutes);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, user.Email), new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role.ToString()), new Claim(ClaimTypes.GivenName, user.FirstName), new Claim(ClaimTypes.Surname, user.LastName) };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, expires: expires, signingCredentials: credentials);
        return new LoginResult(new JwtSecurityTokenHandler().WriteToken(token), expires, ToDto(user));
    }
    public async Task<AuthenticatedUser?> GetUserAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken);
        return user is null ? null : ToDto(user);
    }
    private static AuthenticatedUser ToDto(User user) => new(user.Id, user.Email, user.FirstName, user.LastName, user.Role);
}
