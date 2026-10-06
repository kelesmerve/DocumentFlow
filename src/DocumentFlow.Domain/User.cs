namespace DocumentFlow.Domain;

public sealed class User
{
    private User() { }
    public User(Guid id, string email, string passwordHash, string firstName, string lastName, UserRole role, DateTime createdAtUtc)
    {
        Id = id; Email = email; PasswordHash = passwordHash; FirstName = firstName; LastName = lastName; Role = role; IsActive = true; CreatedAtUtc = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
    }
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
