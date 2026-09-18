using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Protocol;

namespace SynthCohost.Runtime.Features.Speech
{
    public interface ISpeechAudioSink
    {
        Task HandleAudioAsync(SpeechAudioPayload payload, CancellationToken cancellationToken);

        Task HandleFailedAsync(SpeechFailedPayload payload, CancellationToken cancellationToken);
    }
}
