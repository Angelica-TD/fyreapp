using FyreApp.Dtos;

namespace FyreApp.Services.Clients;

public interface IClientImportService
{
    Task<ClientImportResultDto> ImportAsync(
        IFormFile file,
        bool dryRun,
        Action<ClientImportProgressDto>? reportProgress = null,
        string? importId = null,
        CancellationToken ct = default);
}
