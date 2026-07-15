namespace SynthCohost.Transport
{
    /// <summary>Transport-neutral WebSocket close information.</summary>
    public sealed class TransportCloseInfo
    {
        public TransportCloseInfo(
            int? code,
            string reason,
            bool wasClean,
            bool remoteInitiated)
        {
            Code = code;
            Reason = reason ?? string.Empty;
            WasClean = wasClean;
            RemoteInitiated = remoteInitiated;
        }

        /// <summary>
        /// Numeric RFC 6455 close code. This intentionally remains numeric so
        /// application-specific codes such as 4000-4004 are preserved.
        /// Null means the connection ended without a close frame.
        /// </summary>
        public int? Code { get; }

        public string Reason { get; }

        public bool WasClean { get; }

        public bool RemoteInitiated { get; }
    }
}
