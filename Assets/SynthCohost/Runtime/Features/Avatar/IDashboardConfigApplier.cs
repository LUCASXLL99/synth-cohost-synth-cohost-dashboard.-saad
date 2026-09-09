namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Host/dashboard settings hook. Unknown JSON fields are ignored.
    /// No WebSocket event is defined yet; Host may call this directly later.
    /// </summary>
    public interface IDashboardConfigApplier
    {
        void ApplyUnknownSafe(string json);
    }
}
