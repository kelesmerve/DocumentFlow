using DocumentFlow.Domain;

namespace DocumentFlow.Domain.Tests;

public sealed class UserTests
{
    [Fact]
    public void New_user_is_active_and_timestamp_is_utc()
    {
        var user = new User(Guid.NewGuid(), "a@example.test", "hashed-value", "A", "User", UserRole.Employee, DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified));
        Assert.True(user.IsActive);
        Assert.Equal(DateTimeKind.Utc, user.CreatedAtUtc.Kind);
        Assert.Equal(UserRole.Employee, user.Role);
    }
}
