using DocumentFlow.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DocumentFlow.Infrastructure.Persistence;

public sealed class DevelopmentUserSeeder(DocumentFlowDbContext db, IPasswordHasher<User> hasher)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seeds = new[]
        {
            ("employee@documentflow.local", "Employee", "User", UserRole.Employee, "DevEmployee!2026"),
            ("manager@documentflow.local", "Manager", "User", UserRole.Manager, "DevManager!2026"),
            ("admin@documentflow.local", "Admin", "User", UserRole.Admin, "DevAdmin!2026")
        };
        foreach (var (email, first, last, role, password) in seeds)
        {
            if (await db.Users.AnyAsync(x => x.Email == email, cancellationToken)) continue;
            var user = new User(Guid.NewGuid(), email, "pending", first, last, role, DateTime.UtcNow);
            db.Users.Add(new User(user.Id, email, hasher.HashPassword(user, password), first, last, role, user.CreatedAtUtc));
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

