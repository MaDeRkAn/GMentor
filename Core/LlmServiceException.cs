namespace GMentor.Core
{
    /// <summary>
    /// Strongly-typed error from an LLM provider with HTTP code + provider status.
    /// </summary>
    public sealed class LlmServiceException : Exception
    {
        public int HttpCode { get; }
        public string? ApiStatus { get; }
        public string? ApiMessage { get; }

        public LlmServiceException(int httpCode, string? apiStatus, string? apiMessage)
            : base(apiMessage ?? apiStatus ?? $"LLM error {httpCode}")
        {
            HttpCode = httpCode;
            ApiStatus = apiStatus;
            ApiMessage = apiMessage;
        }
    }
}
