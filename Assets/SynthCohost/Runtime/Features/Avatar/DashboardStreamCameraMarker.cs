using UnityEngine;

namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Marks the camera LiveKit (or Host capture) should publish later.
    /// Does not add a LiveKit package.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Synth Cohost/Dashboard Stream Camera")]
    public sealed class DashboardStreamCameraMarker : MonoBehaviour
    {
        public const string DefaultObjectName = "DashboardStreamCamera";
    }
}
