using System;

namespace SynthCohost.Runtime.Session
{
    public enum CohostSendStatus
    {
        Sent,
        NotReady,
        TurnAlreadyInFlight,
        RateLimited,
        ValidationFailed,
        Cancelled,
        TransportFailed
    }

    public readonly struct CohostSendResult
    {
        private CohostSendResult(CohostSendStatus status, string message, TimeSpan retryAfter)
        {
            Status = status;
            Message = message ?? string.Empty;
            RetryAfter = retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter;
        }

        public CohostSendStatus Status { get; }
        public string Message { get; }
        public TimeSpan RetryAfter { get; }
        public bool Succeeded => Status == CohostSendStatus.Sent;

        public static CohostSendResult Sent() =>
            new CohostSendResult(CohostSendStatus.Sent, string.Empty, TimeSpan.Zero);

        public static CohostSendResult RateLimited(TimeSpan retryAfter) =>
            new CohostSendResult(
                CohostSendStatus.RateLimited,
                "The outbound message rate limit has been reached.",
                retryAfter);

        public static CohostSendResult Failure(CohostSendStatus status, string message) =>
            new CohostSendResult(status, message, TimeSpan.Zero);
    }
}
