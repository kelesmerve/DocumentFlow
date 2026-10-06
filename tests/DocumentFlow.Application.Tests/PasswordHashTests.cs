using DocumentFlow.Domain;
using Microsoft.AspNetCore.Identity;

namespace DocumentFlow.Application.Tests;

public sealed class PasswordHashTests
{
    [Fact]
    public void Password_hasher_never_returns_plaintext_and_verifies_password()
    {
        var hasher = new PasswordHasher<User>();
        var user = new User(Guid.NewGuid(), "a@example.test", "pending", "A", "User", UserRole.Employee, DateTime.UtcNow);
        var hash = hasher.HashPassword(user, "Secret!123");
        Assert.NotEqual("Secret!123", hash);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(user, hash, "Secret!123"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(user, hash, "wrong"));
    }
}
