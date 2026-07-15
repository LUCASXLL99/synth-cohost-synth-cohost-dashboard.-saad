using System;

namespace SynthCohost.Runtime.Session
{
    public sealed class SessionStateMachine
    {
        private readonly object gate = new object();
        private SessionState state = SessionState.Disconnected;

        public event Action<SessionState, SessionState> StateChanged;

        public SessionState State
        {
            get
            {
                lock (gate)
                {
                    return state;
                }
            }
        }

        public bool TryTransition(SessionState next)
        {
            SessionState previous;
            lock (gate)
            {
                previous = state;
                if (previous == next)
                {
                    return true;
                }

                if (!CanTransition(previous, next))
                {
                    return false;
                }

                state = next;
            }

            StateChanged?.Invoke(previous, next);
            return true;
        }

        public void Transition(SessionState next)
        {
            if (!TryTransition(next))
            {
                throw new InvalidOperationException($"Invalid session transition: {State} -> {next}.");
            }
        }

        private static bool CanTransition(SessionState from, SessionState to)
        {
            if (to == SessionState.Stopping)
            {
                return from != SessionState.Disconnected && from != SessionState.Stopping;
            }

            return from switch
            {
                SessionState.Disconnected => to == SessionState.Connecting,
                SessionState.Connecting => to == SessionState.Authenticating ||
                                           to == SessionState.Reconnecting ||
                                           to == SessionState.Disconnected ||
                                           to == SessionState.Faulted ||
                                           to == SessionState.AuthRequired,
                SessionState.Authenticating => to == SessionState.Ready ||
                                               to == SessionState.Reconnecting ||
                                               to == SessionState.Disconnected ||
                                               to == SessionState.Faulted ||
                                               to == SessionState.AuthRequired,
                SessionState.Ready => to == SessionState.Reconnecting ||
                                      to == SessionState.Disconnected ||
                                      to == SessionState.Faulted ||
                                      to == SessionState.AuthRequired,
                SessionState.Reconnecting => to == SessionState.Connecting ||
                                             to == SessionState.Disconnected ||
                                             to == SessionState.Faulted ||
                                             to == SessionState.AuthRequired,
                SessionState.Stopping => to == SessionState.Disconnected,
                SessionState.Faulted => to == SessionState.Connecting || to == SessionState.Disconnected,
                SessionState.AuthRequired => to == SessionState.Connecting || to == SessionState.Disconnected,
                _ => false
            };
        }
    }
}
