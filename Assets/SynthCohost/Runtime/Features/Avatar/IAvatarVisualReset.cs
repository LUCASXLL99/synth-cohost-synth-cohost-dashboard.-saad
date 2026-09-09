namespace SynthCohost.Runtime.Features.Avatar
{
    /// <summary>
    /// Optional presentation reset when the WebSocket session leaves Ready.
    /// Must not send <c>state.ack</c> — the server did not request this pose.
    /// </summary>
    public interface IAvatarVisualReset
    {
        void ResetToLivingIdle();
    }
}
