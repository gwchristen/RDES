using System;
using System.Threading;
using System.Threading.Tasks;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class ExponentialBackoffPolicy
    {
        private readonly RetryPolicyConfig _config;
        private readonly Random _random;

        public RetryPolicyConfig Config => _config;

        public ExponentialBackoffPolicy(RetryPolicyConfig? config = null)
        {
            _config = config ?? new RetryPolicyConfig();
            _random = new Random();
        }

        public int CalculateDelay(int attempt)
        {
            if (attempt <= 0) attempt = 1;
            
            double exp = Math.Pow(2, attempt - 1);
            double baseDelay = Math.Min(_config.MaxDelayMs, _config.BaseDelayMs * exp);

            // Jitter calculation: [-JitterFactor * baseDelay, +JitterFactor * baseDelay]
            double jitterRange = baseDelay * _config.JitterFactor;
            double jitter = (_random.NextDouble() * 2.0 - 1.0) * jitterRange;

            int finalDelay = (int)Math.Round(baseDelay + jitter);
            return Math.Max(0, Math.Min(_config.MaxDelayMs, finalDelay));
        }

        public async Task<T> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<T>> action,
            Func<Exception, int, Task>? onRetry = null,
            CancellationToken parentToken = default)
        {
            int attempt = 0;
            while (true)
            {
                attempt++;
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
                cts.CancelAfter(_config.CommandTimeoutMs);

                try
                {
                    return await action(cts.Token);
                }
                catch (Exception ex) when (attempt <= _config.MaxRetries && !parentToken.IsCancellationRequested)
                {
                    if (onRetry != null)
                    {
                        await onRetry(ex, attempt);
                    }

                    int delayMs = CalculateDelay(attempt);
                    await Task.Delay(delayMs, parentToken);
                }
            }
        }

        public async Task ExecuteWithRetryAsync(
            Func<CancellationToken, Task> action,
            Func<Exception, int, Task>? onRetry = null,
            CancellationToken parentToken = default)
        {
            await ExecuteWithRetryAsync<bool>(async ct =>
            {
                await action(ct);
                return true;
            }, onRetry, parentToken);
        }
    }
}
