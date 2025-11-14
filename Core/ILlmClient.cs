namespace GMentor.Core
{
    public interface ILlmClient
    {
        Task<LlmResponse> AnalyzeAsync(LlmRequest request, CancellationToken ct);
    }
}
