using System;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Optional Host settings. Empty strings mean unset. JsonUtility ignores unknown fields.
    /// </summary>
    [Serializable]
    public sealed class DashboardConfigPatch
    {
        public string captions;
        public string lockEyes;
        public string tts;
    }
}
