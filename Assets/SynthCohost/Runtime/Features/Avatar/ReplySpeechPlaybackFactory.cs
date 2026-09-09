namespace SynthCohost.Runtime.Features.Avatar
{
    internal static class ReplySpeechPlaybackFactory
    {
        internal static IReplySpeechPlayback CreatePlatformDefault()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return new WindowsSapiReplySpeechPlayback();
#else
            return new EstimatedReplySpeechPlayback();
#endif
        }
    }
}
