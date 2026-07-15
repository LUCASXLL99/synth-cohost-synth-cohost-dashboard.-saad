using SynthCohost.Runtime.Session;

namespace SynthCohost.Runtime.Features.Stt
{
    /// <summary>
    /// Sends already-produced transcript text. It intentionally does not capture audio or perform speech recognition.
    /// </summary>
    public interface ITranscriptClient : ICohostOutboundSession
    {
    }
}
