using System;
using System.IO;
using System.Text;

namespace SynthCohost.Transport
{
    /// <summary>
    /// Reassembles WebSocket text fragments and performs strict UTF-8 decoding
    /// only after the final fragment has arrived.
    /// </summary>
    internal sealed class TextMessageAccumulator : IDisposable
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        private readonly int _maximumBytes;
        private readonly MemoryStream _buffer;

        public TextMessageAccumulator(int maximumBytes)
        {
            if (maximumBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }

            _maximumBytes = maximumBytes;
            _buffer = new MemoryStream(Math.Min(maximumBytes, 8 * 1024));
        }

        public int BufferedByteCount => checked((int)_buffer.Length);

        /// <summary>
        /// Appends one fragment. Returns true and sets <paramref name="message"/>
        /// only when the fragment completes the current message.
        /// </summary>
        public bool Append(
            byte[] source,
            int offset,
            int count,
            bool endOfMessage,
            out string message)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (offset < 0 || count < 0 || offset > source.Length - count)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (_buffer.Length > _maximumBytes - count)
            {
                throw new TransportMessageTooLargeException(_maximumBytes);
            }

            _buffer.Write(source, offset, count);

            if (!endOfMessage)
            {
                message = null;
                return false;
            }

            try
            {
                message = StrictUtf8.GetString(
                    _buffer.GetBuffer(),
                    0,
                    checked((int)_buffer.Length));
                return true;
            }
            finally
            {
                _buffer.SetLength(0);
                _buffer.Position = 0;
            }
        }

        public void Reset()
        {
            _buffer.SetLength(0);
            _buffer.Position = 0;
        }

        public void Dispose()
        {
            _buffer.Dispose();
        }
    }
}
