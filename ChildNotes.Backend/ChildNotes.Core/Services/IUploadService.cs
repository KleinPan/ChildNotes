using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IUploadService
{
    Task<UploadResponse> UploadAsync(Stream stream, string fileName, CancellationToken ct = default);
}
