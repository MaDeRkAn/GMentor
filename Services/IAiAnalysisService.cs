using GMentor.Models;

namespace GMentor.Services
{
    public interface IAiAnalysisService
    {
        Task<AiResult> AnalyzeAsync(
            string uiCategory,
            string? stickyGameTitle,
            string trustedTitle,
            byte[] image,
            CancellationToken cancellationToken = default);
    }

}
