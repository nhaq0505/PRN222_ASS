using CourtBook.BusinessObjects.Security;
using CourtBook.Services.Validation;
using Xunit;

namespace CourtBook.Tests;

/// <summary>
/// Các ca kiểm thử tự động cho cơ chế băm mật khẩu PBKDF2 và kiểm tra an toàn URL ảnh.
/// Tác giả: tphuy227266-star
/// </summary>
public class SecurityValidationTests
{
    [Fact]
    public void PasswordHelper_HashAndVerify_MatchesCorrectPassword()
    {
        string rawPassword = "TestPassword@2026!";
        string hash = PasswordHelper.HashPassword(rawPassword);

        Assert.NotNull(hash);
        Assert.Contains(":", hash); // Format: {salt}:{subkey}

        bool isValid = PasswordHelper.VerifyPassword(rawPassword, hash);
        Assert.True(isValid);

        bool isInvalid = PasswordHelper.VerifyPassword("WrongPassword123!", hash);
        Assert.False(isInvalid);
    }

    [Theory]
    [InlineData("/uploads/venues/sample.jpg", true)]
    [InlineData("https://example.com/photo.png", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/svg+xml;base64,123", false)]
    [InlineData("http://insecure.com/photo.jpg", false)]
    public void ImageUrlValidator_ValidatesUrlSecurity(string url, bool expectedValid)
    {
        string? error = ImageUrlValidator.ValidateSingleUrl(url, out string normalized);
        bool isValid = error == null;
        Assert.Equal(expectedValid, isValid);
    }
}
