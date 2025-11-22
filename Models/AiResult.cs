namespace GMentor.Models
{
    public sealed record AiResult(
            string Game,
            string Text,
            bool UsedWebSearch,
            TimeSpan Latency,
            string? YouTubeQuery
        );


}
