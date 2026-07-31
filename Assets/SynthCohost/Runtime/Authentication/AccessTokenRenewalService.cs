using System;
using System.Threading;
using System.Threading.Tasks;
using SynthCohost.Runtime.Configuration;
using SynthCohost.Runtime.Diagnostics;

namespace SynthCohost.Runtime.Authentication
{
    /// <summary>
    /// Proactively refreshes the access token before expiry while a live session is active,
    /// so long streaming connections can renew without waiting for a 401.
    /// </summary>
    public sealed class AccessTokenRenewalService : IDisposable
    {
        private readonly RefreshingCredentialProvider refreshingProvider;
        private readonly RuntimeAuthSession session;
        private readonly TimeSpan refreshSkew;
        private readonly ICohostDiagnostics diagnostics;
        private readonly object gate = new object();

        private CancellationTokenSource loopCancellation;
        private Task loopTask = Task.CompletedTask;
        private bool disposed;

        public AccessTokenRenewalService(
            RefreshingCredentialProvider refreshingProvider,
            RuntimeAuthSession session,
            ICohostDiagnostics diagnostics = null,
            TimeSpan? refreshSkew = null)
        {
            this.refreshingProvider = refreshingProvider
                ?? throw new ArgumentNullException(nameof(refreshingProvider));
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.diagnostics = diagnostics ?? new NullCohostDiagnostics();
            this.refreshSkew = refreshSkew ?? AuthHttpClient.DefaultRefreshSkew;
        }

        public event Action TokensRotated;
        public event Action RefreshFailedRequiresLogin;

        public bool IsRunning
        {
            get
            {
                lock (gate)
                {
                    return loopCancellation != null;
                }
            }
        }

        public void Start()
        {
            ThrowIfDisposed();
            lock (gate)
            {
                if (loopCancellation != null)
                {
                    return;
                }

                loopCancellation = new CancellationTokenSource();
                loopTask = RunLoopAsync(loopCancellation.Token);
            }

            diagnostics.Write(
                DiagnosticLogLevel.Information,
                "Auth",
                "Access-token renewal service started; token values were not logged.");
        }

        public async Task StopAsync()
        {
            CancellationTokenSource source;
            Task task;
            lock (gate)
            {
                source = loopCancellation;
                task = loopTask;
                loopCancellation = null;
                loopTask = Task.CompletedTask;
            }

            if (source == null)
            {
                return;
            }

            source.Cancel();
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                source.Dispose();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
        }

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!session.TryGetAccessToken(out var accessToken) ||
                    !JwtAccessTokenInspector.TryGetRefreshDueUtc(
                        accessToken,
                        refreshSkew,
                        out var refreshDueUtc))
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    continue;
                }

                var delay = refreshDueUtc - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    diagnostics.Write(
                        DiagnosticLogLevel.Verbose,
                        "Auth",
                        $"Access-token renewal scheduled in {Math.Ceiling(delay.TotalSeconds):0}s.");
                    await Task.Delay(delay, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var refreshed = await refreshingProvider.RefreshNowAsync(cancellationToken);
                if (refreshed)
                {
                    diagnostics.Write(
                        DiagnosticLogLevel.Information,
                        "Auth",
                        "Access token renewed in the background; both tokens were rotated.");
                    TokensRotated?.Invoke();
                    continue;
                }

                diagnostics.Write(
                    DiagnosticLogLevel.Warning,
                    "Auth",
                    "Background access-token renewal failed; login is required.");
                RefreshFailedRequiresLogin?.Invoke();
                return;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AccessTokenRenewalService));
            }
        }
    }
}
