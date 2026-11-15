using Microsoft.EntityFrameworkCore;
using SplitTrip.BusinessObjects.Entities;

namespace SplitTrip.DataAccessObjects.Daos;

public sealed class UserDao(SplitTripDbContext dbContext)
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default)
        => dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default)
        => dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, ct);

    public Task<int> GetCountAsync(CancellationToken ct = default)
        => dbContext.Users.CountAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        dbContext.Users.Update(user);
        await dbContext.SaveChangesAsync(ct);
    }
}
