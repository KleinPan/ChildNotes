using ChildNotes.Core.Dtos;

namespace ChildNotes.Core.Services;

public interface IPointsService
{
    Task<PointsDashboardResponse> GetDashboardAsync(CancellationToken ct = default);
}
