using System.Collections.Concurrent;

namespace RabbitMQConsumer.Services
{
    public class RateLimiter
    {
        private readonly ConcurrentQueue<DateTime> _requestTimes;
        private readonly int _maxRequestsPerMinute;
        private readonly object _lock = new object();

        public RateLimiter(int maxRequestsPerMinute)
        {
            _maxRequestsPerMinute = maxRequestsPerMinute;
            _requestTimes = new ConcurrentQueue<DateTime>();
        }

        public async Task<bool> TryAcquireAsync()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var oneMinuteAgo = now.AddMinutes(-1);

                // Remove old request times
                while (_requestTimes.TryPeek(out var oldestTime) && oldestTime < oneMinuteAgo)
                {
                    _requestTimes.TryDequeue(out _);
                }

                // Check if we can make a new request
                if (_requestTimes.Count < _maxRequestsPerMinute)
                {
                    _requestTimes.Enqueue(now);
                    return true;
                }

                return false;
            }
        }

        public async Task WaitForAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            while (!await TryAcquireAsync())
            {
                await Task.Delay(1000, cancellationToken); // Wait 1 second before checking again
            }
        }
    }
}
