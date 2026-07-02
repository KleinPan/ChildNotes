using ChildNotes.Core.Dtos;
using ChildNotes.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChildNotes.Api.Controllers;

[Route("api/upload")]
public class UploadController : AppBaseController
{
    private readonly IUploadService _upload;
    public UploadController(IUploadService upload) => _upload = upload;

    [HttpPost]
    public async Task<UploadResponse> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new Core.Exceptions.BusinessException("文件不能为空");
        using var stream = file.OpenReadStream();
        return await _upload.UploadAsync(stream, file.FileName, file.ContentType, ct);
    }
}
