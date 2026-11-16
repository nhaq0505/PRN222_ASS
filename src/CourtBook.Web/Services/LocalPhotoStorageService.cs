using CourtBook.Services.Models;

namespace CourtBook.Web.Services;

public class LocalPhotoStorageService : IPhotoStorageService
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedSubfolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "venues",
        "courts"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private readonly IWebHostEnvironment _environment;

    public LocalPhotoStorageService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<ServiceResult<string>> SavePhotoAsync(
        IFormFile file,
        string subfolder,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subfolder) || !AllowedSubfolders.Contains(subfolder.Trim()))
        {
            return ServiceResult<string>.Fail("Thư mục lưu trữ ảnh không hợp lệ.");
        }

        string normalizedSubfolder = subfolder.Trim().ToLowerInvariant();

        if (file is null || file.Length <= 0)
        {
            return ServiceResult<string>.Fail("Vui lòng chọn tệp ảnh hợp lệ (tệp rỗng).");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return ServiceResult<string>.Fail("Ảnh tải lên không được vượt quá 5MB.");
        }

        string ext = Path.GetExtension(file.FileName)?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AllowedExtensions.Contains(ext))
        {
            return ServiceResult<string>.Fail("Chỉ hỗ trợ định dạng ảnh JPG, JPEG, PNG hoặc WEBP.");
        }

        await using Stream inputStream = file.OpenReadStream();
        byte[] header = new byte[12];
        int headerRead = await ReadHeaderBytesAsync(inputStream, header, ct);

        if (!MatchesMagicBytes(ext, header, headerRead))
        {
            return ServiceResult<string>.Fail("Nội dung tệp không khớp định dạng ảnh hợp lệ (.jpg, .png, .webp).");
        }

        string targetDirectory = Path.Combine(ResolveWebRootPath(), "uploads", normalizedSubfolder);
        Directory.CreateDirectory(targetDirectory);

        string fileName = $"{Guid.NewGuid():N}{ext}";
        string fullPath = Path.Combine(targetDirectory, fileName);

        await using (var outputStream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true))
        {
            await outputStream.WriteAsync(header.AsMemory(0, headerRead), ct);
            await inputStream.CopyToAsync(outputStream, ct);
        }

        return ServiceResult<string>.Ok($"/uploads/{normalizedSubfolder}/{fileName}");
    }

    public async Task<ServiceResult<List<string>>> SavePhotosAsync(
        IEnumerable<IFormFile> files,
        string subfolder,
        int maxCount = 6,
        CancellationToken ct = default)
    {
        if (files is null)
        {
            return ServiceResult<List<string>>.Ok(new List<string>());
        }

        List<IFormFile> fileList = files.Where(f => f is not null).ToList();
        if (fileList.Count == 0)
        {
            return ServiceResult<List<string>>.Ok(new List<string>());
        }

        int effectiveMax = Math.Max(1, maxCount);
        if (fileList.Count > effectiveMax)
        {
            return ServiceResult<List<string>>.Fail($"Chỉ được tải lên tối đa {effectiveMax} ảnh mỗi lần.");
        }

        var savedUrls = new List<string>(fileList.Count);
        foreach (IFormFile file in fileList)
        {
            ServiceResult<string> singleResult = await SavePhotoAsync(file, subfolder, ct);
            if (!singleResult.Succeeded || string.IsNullOrEmpty(singleResult.Data))
            {
                CleanupSavedFiles(savedUrls);
                return ServiceResult<List<string>>.Fail(
                    singleResult.ErrorMessage ?? "Không thể lưu ảnh tải lên.");
            }

            savedUrls.Add(singleResult.Data);
        }

        return ServiceResult<List<string>>.Ok(savedUrls);
    }

    private string ResolveWebRootPath()
        => string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? Path.Combine(_environment.ContentRootPath, "wwwroot")
            : _environment.WebRootPath;

    private void CleanupSavedFiles(IEnumerable<string> relativeUrls)
    {
        string webRoot = ResolveWebRootPath();
        foreach (string url in relativeUrls)
        {
            string relativePath = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(webRoot, relativePath);
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch
            {
                // Best-effort rollback of partial batch upload
            }
        }
    }

    private static async Task<int> ReadHeaderBytesAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private static bool MatchesMagicBytes(string extension, byte[] header, int length)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" => length >= 3
                && header[0] == 0xFF
                && header[1] == 0xD8
                && header[2] == 0xFF,

            ".png" => length >= 4
                && header[0] == 0x89
                && header[1] == 0x50
                && header[2] == 0x4E
                && header[3] == 0x47,

            ".webp" => length >= 12
                && header[0] == 0x52 // R
                && header[1] == 0x49 // I
                && header[2] == 0x46 // F
                && header[3] == 0x46 // F
                && header[8] == 0x57 // W
                && header[9] == 0x45 // E
                && header[10] == 0x42 // B
                && header[11] == 0x50, // P

            _ => false
        };
    }
}
