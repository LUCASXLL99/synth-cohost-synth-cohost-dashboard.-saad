using System;
using SynthCohost.Transport;

namespace SynthCohost.Runtime.Session
{
    public enum CloseDirective
    {
        StayDisconnected,
        Reconnect,
        Fault,
        RequireAuthentication
    }

    public readonly struct ClosePolicyDecision
    {
        public ClosePolicyDecision(CloseDirective directive, TimeSpan minimumDelay)
        {
            Directive = directive;
            MinimumDelay = minimumDelay;
        }

        public CloseDirective Directive { get; }
        public TimeSpan MinimumDelay { get; }
    }

    public interface IClosePolicy
    {
        ClosePolicyDecision Decide(TransportCloseInfo closeInfo, bool stopRequested);
    }

    public sealed class DeployedV2ClosePolicy : IClosePolicy
    {
        public ClosePolicyDecision Decide(TransportCloseInfo closeInfo, bool stopRequested)
        {
            return ClosePolicy.Decide(closeInfo, stopRequested);
        }
    }

    /// <summary>Compatibility facade for pure policy tests and callers that do not need injection.</summary>
    public static class ClosePolicy
    {
        public static ClosePolicyDecision Decide(TransportCloseInfo closeInfo, bool stopRequested)
        {
            if (stopRequested)
            {
                return new ClosePolicyDecision(CloseDirective.StayDisconnected, TimeSpan.Zero);
            }

            var code = closeInfo?.Code;
            if (code == WebSocketCloseCode.NormalClosure)
            {
                return new ClosePolicyDecision(CloseDirective.StayDisconnected, TimeSpan.Zero);
            }

            switch (code)
            {
                case 4000:
                    return new ClosePolicyDecision(CloseDirective.Fault, TimeSpan.Zero);
                case 4001:
                    return new ClosePolicyDecision(CloseDirective.RequireAuthentication, TimeSpan.Zero);
                case 4002:
                case 4003:
                    return new ClosePolicyDecision(CloseDirective.Reconnect, TimeSpan.Zero);
                case 4004:
                    return new ClosePolicyDecision(CloseDirective.Reconnect, TimeSpan.FromSeconds(10));
                default:
                    return new ClosePolicyDecision(CloseDirective.Reconnect, TimeSpan.Zero);
            }
        }
    }
}
