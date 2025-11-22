using System.Collections.Concurrent;

namespace RabbitMQConsumer.Services
{
    public enum CircuitBreakerState
    {
        Closed,    // Normal operation
        Open,      // Circuit is open, requests are blocked
        HalfOpen   // Testing if service is back up
    }

    public class CircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly TimeSpan _timeout;
        private readonly object _lock = new object();
        
        private CircuitBreakerState _state = CircuitBreakerState.Closed;
        private int _failureCount = 0;
        private DateTime _lastFailureTime = DateTime.MinValue;

        public CircuitBreaker(int failureThreshold, TimeSpan timeout)
        {
            _failureThreshold = failureThreshold;
            _timeout = timeout;
        }

        public CircuitBreakerState State => _state;

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
        {
            lock (_lock)
            {
                if (_state == CircuitBreakerState.Open)
                {
                    if (DateTime.UtcNow - _lastFailureTime > _timeout)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                    }
                    else
                    {
                        throw new InvalidOperationException("Circuit breaker is open");
                    }
                }
            }

            try
            {
                var result = await operation();
                
                lock (_lock)
                {
                    _failureCount = 0;
                    _state = CircuitBreakerState.Closed;
                }
                
                return result;
            }
            catch (Exception)
            {
                lock (_lock)
                {
                    _failureCount++;
                    _lastFailureTime = DateTime.UtcNow;
                    
                    if (_failureCount >= _failureThreshold)
                    {
                        _state = CircuitBreakerState.Open;
                    }
                }
                
                throw;
            }
        }

        public bool IsCircuitOpen()
        {
            lock (_lock)
            {
                if (_state == CircuitBreakerState.Open)
                {
                    if (DateTime.UtcNow - _lastFailureTime > _timeout)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                        return false;
                    }
                    return true;
                }
                return false;
            }
        }
    }
}
