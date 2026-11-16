using CourtBook.BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace CourtBook.DataAccessObjects;

public class UserDao
{
    private readonly CourtBookDbContext _context;

    public UserDao(CourtBookDbContext context)
    {
        _context = context;
    }

    public Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default)
        => _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<AppUser?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        string normalized = email.Trim().ToLowerInvariant();
        return _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalized, ct);
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
    {
        string normalized = email.Trim().ToLowerInvariant();
        return _context.Users.AnyAsync(u => u.Email.ToLower() == normalized, ct);
    }

    public Task<List<AppUser>> GetAllAsync(CancellationToken ct = default)
        => _context.Users
            .AsNoTracking()
            .Include(u => u.OwnedVenues)
            .OrderBy(u => u.Id)
            .ToListAsync(ct);

    public async Task<AppUser> AddAsync(AppUser user, CancellationToken ct = default)
    {
        user.Email = user.Email.Trim().ToLowerInvariant();
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(AppUser user, CancellationToken ct = default)
    {
        user.Email = user.Email.Trim().ToLowerInvariant();
        if (_context.Entry(user).State == EntityState.Detached)
        {
            _context.Users.Update(user);
        }
        await _context.SaveChangesAsync(ct);
    }
}
