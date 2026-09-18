using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Maps Microsoft SAPI viseme ids (SpeechVisemeType 0–21) onto CC phoneme shapes.
    /// Phoneme shapes already drop the jaw on this mesh — extra <c>mouth_open_M</c>
    /// is only a light support, not a second open.
    /// </summary>
    internal static class LipSyncVisemeMap
    {
        public const float MaxJaw = 12f;
        public const float MaxPhoneme = 40f;

        public static LipSyncPose ToPose(int visemeId)
        {
            switch (Mathf.Clamp(visemeId, 0, 21))
            {
                case 0: // silence
                    return LipSyncPose.Rest;
                case 1: // ax / ah
                    return Pose(jaw: 6f, ahh: 28f, schwa: 12f);
                case 2: // aa
                    return Pose(jaw: 10f, aaa: 36f, wide: 8f);
                case 3: // ao
                    return Pose(jaw: 8f, ahh: 12f, ohh: 30f);
                case 4: // ey / eh / uh
                    return Pose(jaw: 6f, eh: 32f, iee: 8f, wide: 10f);
                case 5: // er
                    return Pose(jaw: 4f, rrr: 32f, schwa: 10f);
                case 6: // y / iy / ih
                    return Pose(jaw: 4f, iee: 36f, wide: 16f);
                case 7: // w / uw
                    return Pose(jaw: 4f, uuu: 28f, www: 32f, narrow: 18f);
                case 8: // ow
                    return Pose(jaw: 7f, ohh: 36f, uuu: 8f, narrow: 12f);
                case 9: // aw
                    return Pose(jaw: 9f, aaa: 18f, ohh: 20f);
                case 10: // oy
                    return Pose(jaw: 6f, ohh: 20f, iee: 16f);
                case 11: // ay
                    return Pose(jaw: 7f, aaa: 18f, iee: 20f, wide: 8f);
                case 12: // h
                    return Pose(jaw: 4f, ahh: 12f, schwa: 8f);
                case 13: // r
                    return Pose(jaw: 3f, rrr: 34f);
                case 14: // l
                    return Pose(jaw: 4f, lntd: 30f);
                case 15: // s / z
                    return Pose(jaw: 2f, sss: 32f, iee: 8f, wide: 6f);
                case 16: // sh / ch / jh
                    return Pose(jaw: 3f, ssh: 34f, narrow: 12f);
                case 17: // th / dh
                    return Pose(jaw: 3f, tth: 34f);
                case 18: // f / v
                    return Pose(jaw: 2f, fff: 34f, close: 8f);
                case 19: // d / t / n
                    return Pose(jaw: 3f, lntd: 32f);
                case 20: // k / g / ng
                    return Pose(jaw: 3f, gk: 32f);
                case 21: // p / b / m
                    return Pose(jaw: 0f, mbp: 36f, close: 24f);
                default:
                    return LipSyncPose.Rest;
            }
        }

        public static LipSyncPose Lerp(LipSyncPose a, LipSyncPose b, float t)
        {
            t = Mathf.Clamp01(t);
            return new LipSyncPose
            {
                Jaw = Mathf.Lerp(a.Jaw, b.Jaw, t),
                Aaa = Mathf.Lerp(a.Aaa, b.Aaa, t),
                Ahh = Mathf.Lerp(a.Ahh, b.Ahh, t),
                Eh = Mathf.Lerp(a.Eh, b.Eh, t),
                Ohh = Mathf.Lerp(a.Ohh, b.Ohh, t),
                Uuu = Mathf.Lerp(a.Uuu, b.Uuu, t),
                Iee = Mathf.Lerp(a.Iee, b.Iee, t),
                Rrr = Mathf.Lerp(a.Rrr, b.Rrr, t),
                Www = Mathf.Lerp(a.Www, b.Www, t),
                Sss = Mathf.Lerp(a.Sss, b.Sss, t),
                Fff = Mathf.Lerp(a.Fff, b.Fff, t),
                Tth = Mathf.Lerp(a.Tth, b.Tth, t),
                Mbp = Mathf.Lerp(a.Mbp, b.Mbp, t),
                Ssh = Mathf.Lerp(a.Ssh, b.Ssh, t),
                Schwa = Mathf.Lerp(a.Schwa, b.Schwa, t),
                Gk = Mathf.Lerp(a.Gk, b.Gk, t),
                Lntd = Mathf.Lerp(a.Lntd, b.Lntd, t),
                Wide = Mathf.Lerp(a.Wide, b.Wide, t),
                Narrow = Mathf.Lerp(a.Narrow, b.Narrow, t),
                Close = Mathf.Lerp(a.Close, b.Close, t)
            };
        }

        private static LipSyncPose Pose(
            float jaw = 0f,
            float aaa = 0f,
            float ahh = 0f,
            float eh = 0f,
            float ohh = 0f,
            float uuu = 0f,
            float iee = 0f,
            float rrr = 0f,
            float www = 0f,
            float sss = 0f,
            float fff = 0f,
            float tth = 0f,
            float mbp = 0f,
            float ssh = 0f,
            float schwa = 0f,
            float gk = 0f,
            float lntd = 0f,
            float wide = 0f,
            float narrow = 0f,
            float close = 0f)
        {
            return new LipSyncPose
            {
                Jaw = Mathf.Min(jaw, MaxJaw),
                Aaa = Mathf.Min(aaa, MaxPhoneme),
                Ahh = Mathf.Min(ahh, MaxPhoneme),
                Eh = Mathf.Min(eh, MaxPhoneme),
                Ohh = Mathf.Min(ohh, MaxPhoneme),
                Uuu = Mathf.Min(uuu, MaxPhoneme),
                Iee = Mathf.Min(iee, MaxPhoneme),
                Rrr = Mathf.Min(rrr, MaxPhoneme),
                Www = Mathf.Min(www, MaxPhoneme),
                Sss = Mathf.Min(sss, MaxPhoneme),
                Fff = Mathf.Min(fff, MaxPhoneme),
                Tth = Mathf.Min(tth, MaxPhoneme),
                Mbp = Mathf.Min(mbp, MaxPhoneme),
                Ssh = Mathf.Min(ssh, MaxPhoneme),
                Schwa = Mathf.Min(schwa, MaxPhoneme),
                Gk = Mathf.Min(gk, MaxPhoneme),
                Lntd = Mathf.Min(lntd, MaxPhoneme),
                Wide = Mathf.Min(wide, MaxPhoneme),
                Narrow = Mathf.Min(narrow, MaxPhoneme),
                Close = Mathf.Min(close, MaxPhoneme)
            };
        }
    }
}
