using CourtBook.BusinessObjects.Entities;
using CourtBook.Repositories;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;

namespace CourtBook.Services.Implementations;

public class VenueReviewService : IVenueReviewService
{
    private readonly IVenueReviewRepository _reviewRepo;
    private readonly IVenueRepository _venueRepo;
    private readonly IUserRepository _userRepo;

    public VenueReviewService(
        IVenueReviewRepository reviewRepo,
        IVenueRepository venueRepo,
        IUserRepository userRepo)
    {
        _reviewRepo = reviewRepo;
        _venueRepo = venueRepo;
        _userRepo = userRepo;
    }

    public async Task<IReadOnlyList<VenueReviewDto>> GetVenueReviewsAsync(int venueId, CancellationToken ct = default)
    {
        List<VenueReview> reviews = await _reviewRepo.GetByVenueIdAsync(venueId, ct);
        return reviews.Select(r => new VenueReviewDto
        {
            Id = r.Id,
            VenueId = r.VenueId,
            UserId = r.UserId,
            UserFullName = r.User?.FullName ?? "Người chơi",
            Rating = r.Rating,
            Comment = r.Comment,
            CreatedAtUtc = r.CreatedAtUtc
        }).ToList();
    }

    public async Task<ServiceResult<VenueReviewDto>> AddReviewAsync(
        int userId,
        CreateReviewRequestDto req,
        CancellationToken ct = default)
    {
        if (req is null)
        {
            return ServiceResult<VenueReviewDto>.Fail("Dữ liệu đánh giá không hợp lệ.");
        }

        if (req.Rating < 1 || req.Rating > 5)
        {
            return ServiceResult<VenueReviewDto>.Fail("Số sao đánh giá phải từ 1 đến 5.");
        }

        string comment = req.Comment?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(comment))
        {
            return ServiceResult<VenueReviewDto>.Fail("Vui lòng nhập nội dung đánh giá.");
        }

        if (comment.Length > 500)
        {
            comment = comment[..500];
        }

        Venue? venue = await _venueRepo.GetByIdAsync(req.VenueId, ct);
        if (venue is null)
        {
            return ServiceResult<VenueReviewDto>.Fail("Cụm sân không tồn tại.");
        }

        AppUser? user = await _userRepo.GetByIdAsync(userId, ct);
        if (user is null)
        {
            return ServiceResult<VenueReviewDto>.Fail("Tài khoản người dùng không hợp lệ.");
        }

        var review = new VenueReview
        {
            VenueId = req.VenueId,
            UserId = userId,
            Rating = req.Rating,
            Comment = comment,
            CreatedAtUtc = DateTime.UtcNow
        };

        VenueReview created = await _reviewRepo.AddAsync(review, ct);

        return ServiceResult<VenueReviewDto>.Ok(new VenueReviewDto
        {
            Id = created.Id,
            VenueId = created.VenueId,
            UserId = created.UserId,
            UserFullName = user.FullName,
            Rating = created.Rating,
            Comment = created.Comment,
            CreatedAtUtc = created.CreatedAtUtc
        });
    }
}
