namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Character Creator mouth weights for one instant of speech.
    /// Prefer CC phoneme shapes over ARKit funnel/pucker (those tear this chin).
    /// </summary>
    public struct LipSyncPose
    {
        public float Jaw;
        public float Aaa;
        public float Ahh;
        public float Eh;
        public float Ohh;
        public float Uuu;
        public float Iee;
        public float Rrr;
        public float Www;
        public float Sss;
        public float Fff;
        public float Tth;
        public float Mbp;
        public float Ssh;
        public float Schwa;
        public float Gk;
        public float Lntd;
        public float Wide;
        public float Narrow;
        public float Close;

        public static LipSyncPose Rest => default;
    }
}
