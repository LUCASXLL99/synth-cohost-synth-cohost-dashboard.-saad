using System;

namespace SynthCohost.Transport
{
    /// <summary>Transport-neutral details about an asynchronous socket failure.</summary>
    public sealed class TransportErrorInfo
    {
        public TransportErrorInfo(
            TransportOperation operation,
            string message,
            Exception exception,
            bool isCancellation = false)
        {
            Operation = operation;
            Message = message ?? string.Empty;
            Exception = exception;
            IsCancellation = isCancellation;
        }

        public TransportOperation Operation { get; }

        public string Message { get; }

        /// <summary>
        /// Original exception, when one exists. Presentation code should not expose
        /// raw exception text directly to end users.
        /// </summary>
        public Exception Exception { get; }

        public bool IsCancellation { get; }
    }
}
