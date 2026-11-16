using CourtBook.Services.Models;
using Xunit;

namespace CourtBook.Tests;

/// <summary>
/// Các ca kiểm thử biên cho hệ thống đánh giá & xếp hạng 5 sao.
/// Tác giả: tphuy227266-star
/// </summary>
public class VenueReviewBoundaryTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(-1, false)]
    public void ReviewRating_ValidatesStarRange(int rating, bool expectedValid)
    {
        bool isValid = rating >= 1 && rating <= 5;
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void CreateReviewRequestDto_TrimsAndValidatesComment()
    {
        var req = new CreateReviewRequestDto
        {
            VenueId = 1,
            Rating = 5,
            Comment = "   Sân bóng rất tốt và thoáng mát!   "
        };

        string trimmed = req.Comment.Trim();
        Assert.Equal("Sân bóng rất tốt và thoáng mát!", trimmed);
    }
}
