using CourtBook.BusinessObjects.Entities;

namespace CourtBook.Repositories;

public interface IUserRepository
{
    Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<List<AppUser>> GetAllAsync(CancellationToken ct = default);
    Task<AppUser> AddAsync(AppUser user, CancellationToken ct = default);
    Task UpdateAsync(AppUser user, CancellationToken ct = default);
}
