namespace GMentor.Core
{
    public sealed record LlmRequest(
        string Provider,
        string Model,
        string Game,
        string Category,   // Quest | GunMods | KeysLoot
        string PromptText,
        byte[] ImageBytes
    );

    public sealed record LlmResponse(
        string Text,
        bool UsedWebSearch,
        TimeSpan Latency
    );
}
