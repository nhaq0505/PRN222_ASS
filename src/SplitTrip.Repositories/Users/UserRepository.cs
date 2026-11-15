using SplitTrip.BusinessObjects.Entities;
using SplitTrip.DataAccessObjects.Daos;

namespace SplitTrip.Repositories.Users;

public sealed class UserRepository(UserDao userDao) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => userDao.GetByIdAsync(id, ct);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default)
        => userDao.GetByEmailAsync(normalizedEmail, ct);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default)
        => userDao.EmailExistsAsync(normalizedEmail, ct);

    public Task<int> GetCountAsync(CancellationToken ct = default)
        => userDao.GetCountAsync(ct);

    public Task AddAsync(User user, CancellationToken ct = default)
        => userDao.AddAsync(user, ct);

    public Task UpdateAsync(User user, CancellationToken ct = default)
        => userDao.UpdateAsync(user, ct);
}
