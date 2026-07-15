using System;

namespace SynthCohost.Transport
{
    internal sealed class TransportMessageTooLargeException : Exception
    {
        public TransportMessageTooLargeException(int maximumBytes)
            : base($"The WebSocket text message exceeded the {maximumBytes}-byte receive limit.")
        {
            MaximumBytes = maximumBytes;
        }

        public int MaximumBytes { get; }
    }
}
