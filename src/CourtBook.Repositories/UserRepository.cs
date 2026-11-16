using CourtBook.BusinessObjects.Entities;
using CourtBook.DataAccessObjects;

namespace CourtBook.Repositories;

public class UserRepository : IUserRepository
{
    private readonly UserDao _dao;

    public UserRepository(UserDao dao)
    {
        _dao = dao;
    }

    public Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default)
        => _dao.GetByIdAsync(id, ct);

    public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default)
        => _dao.GetByEmailAsync(email, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        => _dao.EmailExistsAsync(email, ct);

    public Task<List<AppUser>> GetAllAsync(CancellationToken ct = default)
        => _dao.GetAllAsync(ct);

    public Task<AppUser> AddAsync(AppUser user, CancellationToken ct = default)
        => _dao.AddAsync(user, ct);

    public Task UpdateAsync(AppUser user, CancellationToken ct = default)
        => _dao.UpdateAsync(user, ct);
}
