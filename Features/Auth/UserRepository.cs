using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Auth;
public sealed class UserRepository(IDbContextFactory<AppDbContext> dbf)
{
    public async Task<User?> FindByUsernameAsync(string username)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<bool> ExistsAsync(string username)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Users.AnyAsync(u => u.Username == username);
    }

    public async Task<long> CreateAsync(string username, string passwordHash)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var user = new User(0, username, passwordHash);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}


