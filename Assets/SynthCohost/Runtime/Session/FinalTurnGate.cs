using System;

namespace SynthCohost.Runtime.Session
{
    public sealed class FinalTurnGate
    {
        private readonly object gate = new object();
        private long sequence;
        private long activeToken;

        public event Action<bool> ActiveChanged;

        public bool IsActive
        {
            get
            {
                lock (gate)
                {
                    return activeToken != 0;
                }
            }
        }

        public bool TryBegin(out long token)
        {
            lock (gate)
            {
                if (activeToken != 0)
                {
                    token = 0;
                    return false;
                }

                sequence = sequence == long.MaxValue ? 1 : sequence + 1;
                activeToken = sequence;
                token = activeToken;
            }

            ActiveChanged?.Invoke(true);
            return true;
        }

        public bool Complete(long token)
        {
            if (token == 0)
            {
                return false;
            }

            lock (gate)
            {
                if (activeToken != token)
                {
                    return false;
                }

                activeToken = 0;
            }

            ActiveChanged?.Invoke(false);
            return true;
        }

        public bool CompleteActive()
        {
            long token;
            lock (gate)
            {
                token = activeToken;
            }

            return Complete(token);
        }

        public void Reset()
        {
            var changed = false;
            lock (gate)
            {
                changed = activeToken != 0;
                activeToken = 0;
            }

            if (changed)
            {
                ActiveChanged?.Invoke(false);
            }
        }
    }
}
