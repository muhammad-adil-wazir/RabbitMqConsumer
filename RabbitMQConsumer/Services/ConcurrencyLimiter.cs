namespace RabbitMQConsumer.Services
{
    public class ConcurrencyLimiter
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxConcurrency;

        public ConcurrencyLimiter(int maxConcurrency)
        {
            _maxConcurrency = maxConcurrency;
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
        {
            await _semaphore.WaitAsync();
            try
            {
                return await operation();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task ExecuteAsync(Func<Task> operation)
        {
            await _semaphore.WaitAsync();
            try
            {
                await operation();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public int AvailableSlots => _semaphore.CurrentCount;
        public int MaxConcurrency => _maxConcurrency;
    }
}
