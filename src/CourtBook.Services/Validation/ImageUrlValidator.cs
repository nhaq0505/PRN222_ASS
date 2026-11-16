namespace CourtBook.Services.Validation;

/// <summary>
/// Validates and normalizes venue/court image URLs so only local /uploads/ paths or https:// URLs are persisted.
/// </summary>
public static class ImageUrlValidator
{
    private const string UploadsPrefix = "/uploads/";

    public static string? ValidateSingleUrl(
        string? rawUrl,
        out string normalizedUrl,
        int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            normalizedUrl = string.Empty;
            return null;
        }

        string trimmed = rawUrl.Trim();
        if (trimmed.Length > maxLength)
        {
            normalizedUrl = string.Empty;
            return $"Đường dẫn ảnh tối đa {maxLength} ký tự.";
        }

        if (HasUnsafeCharacters(trimmed))
        {
            normalizedUrl = string.Empty;
            return "Đường dẫn ảnh không hợp lệ.";
        }

        if (trimmed.StartsWith(UploadsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string relativePart = trimmed[UploadsPrefix.Length..];
            if (relativePart.Length == 0 ||
                trimmed.Contains("//", StringComparison.Ordinal) ||
                trimmed.EndsWith('/'))
            {
                normalizedUrl = string.Empty;
                return "Đường dẫn ảnh tải lên không hợp lệ.";
            }

            normalizedUrl = UploadsPrefix + relativePart;
            return null;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            normalizedUrl = trimmed;
            return null;
        }

        normalizedUrl = string.Empty;
        return "Đường dẫn ảnh chỉ chấp nhận tệp tải lên (/uploads/...) hoặc liên kết https:// hợp lệ.";
    }

    public static string? ValidateGalleryUrls(
        string? rawGalleryUrls,
        out string normalizedGalleryUrls,
        int maxTotalLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(rawGalleryUrls))
        {
            normalizedGalleryUrls = string.Empty;
            return null;
        }

        string[] parts = rawGalleryUrls.Split(
            [';', '|'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var distinct = new List<string>(parts.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string part in parts)
        {
            string? itemError = ValidateSingleUrl(part, out string normalizedPart);
            if (itemError is not null)
            {
                normalizedGalleryUrls = string.Empty;
                return itemError;
            }

            if (!string.IsNullOrEmpty(normalizedPart) && seen.Add(normalizedPart))
            {
                distinct.Add(normalizedPart);
            }
        }

        string joined = string.Join(';', distinct);
        if (joined.Length > maxTotalLength)
        {
            normalizedGalleryUrls = string.Empty;
            return $"Danh sách ảnh thư viện tối đa {maxTotalLength} ký tự.";
        }

        normalizedGalleryUrls = joined;
        return null;
    }

    private static bool HasUnsafeCharacters(string value)
    {
        if (value.Contains("..", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (char ch in value)
        {
            if (char.IsWhiteSpace(ch) ||
                char.IsControl(ch) ||
                ch is '<' or '>' or '"' or '\'' or '\\')
            {
                return true;
            }
        }

        return false;
    }
}
