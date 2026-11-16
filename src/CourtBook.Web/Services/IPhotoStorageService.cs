using CourtBook.Services.Models;

namespace CourtBook.Web.Services;

/// <summary>
/// Handles safe multipart image uploads for venues and courts.
/// </summary>
public interface IPhotoStorageService
{
    Task<ServiceResult<string>> SavePhotoAsync(
        IFormFile file,
        string subfolder,
        CancellationToken ct = default);

    Task<ServiceResult<List<string>>> SavePhotosAsync(
        IEnumerable<IFormFile> files,
        string subfolder,
        int maxCount = 6,
        CancellationToken ct = default);
}
