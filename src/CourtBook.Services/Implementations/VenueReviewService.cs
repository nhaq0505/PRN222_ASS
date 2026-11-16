using CourtBook.BusinessObjects.Entities;
using CourtBook.BusinessObjects.Enums;
using CourtBook.Repositories;
using CourtBook.Services.Helpers;
using CourtBook.Services.Interfaces;
using CourtBook.Services.Models;

namespace CourtBook.Services.Implementations;

public class VenueReviewService : IVenueReviewService
{
    private readonly IVenueReviewRepository _reviewRepo;
    private readonly IVenueRepository _venueRepo;
    private readonly IUserRepository _userRepo;
    private readonly TimeProvider _timeProvider;

    public VenueReviewService(
        IVenueReviewRepository reviewRepo,
        IVenueRepository venueRepo,
        IUserRepository userRepo,
        TimeProvider? timeProvider = null)
    {
        _reviewRepo = reviewRepo;
        _venueRepo = venueRepo;
        _userRepo = userRepo;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
            CreatedAtUtc = AsUtc(r.CreatedAtUtc)
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

        AppUser? user = await ActorGuard.GetActiveActorAsync(_userRepo, userId, ct);
        if (user is null)
        {
            return ServiceResult<VenueReviewDto>.Fail(ActorGuard.InactiveOrMissingMessage);
        }

        if (user.Role == UserRole.Admin)
        {
            return ServiceResult<VenueReviewDto>.Fail("Tài khoản Quản trị sàn không tham gia đánh giá sân.");
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

        if (venue.Status != VenueStatus.Active)
        {
            return ServiceResult<VenueReviewDto>.Fail("Cụm sân hiện chưa hoạt động, không thể đánh giá.");
        }

        if (venue.OwnerId == userId)
        {
            return ServiceResult<VenueReviewDto>.Fail("Chủ sân không thể tự đánh giá cụm sân của chính mình.");
        }

        var review = new VenueReview
        {
            VenueId = req.VenueId,
            UserId = userId,
            Rating = req.Rating,
            Comment = comment,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
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
            CreatedAtUtc = AsUtc(created.CreatedAtUtc)
        });
    }

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
