namespace RabbitMQConsumer.Models
{
    public class EntityMessage
    {
        public List<int> EntityIds { get; set; } = new();
        public int EntityTypeId { get; set; }
        public int ConfigId { get; set; }
        public int SystemType { get; set; }
        public string Client { get; set; } = string.Empty;
    }

    public class RabbitMQSettings
    {
        public string HostName { get; set; } = string.Empty;
        public int Port { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string VirtualHost { get; set; } = string.Empty;
        public string QueueName { get; set; } = string.Empty;
        public string ExchangeName { get; set; } = string.Empty;
        public string RoutingKey { get; set; } = string.Empty;
        
        // QoS Settings for flow control
        public ushort PrefetchCount { get; set; } = 1; // Limit concurrent unacknowledged messages
        public bool PrefetchGlobal { get; set; } = false; // Apply prefetch per consumer vs globally
    }

    public class ClientAPIBaseUrlSettings
    {
        public string Test { get; set; } = string.Empty;
        public string Demo { get; set; } = string.Empty;
        public string Production { get; set; } = string.Empty;
    }

    public class PeriodicScreeningAPISettings
    {
        public string Endpoint { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 300;
        public int MaxRetries { get; set; } = 3;
        public int RetryDelaySeconds { get; set; } = 5;
        
        // Rate limiting and flow control
        public int MaxConcurrentRequests { get; set; } = 5; // Max concurrent API calls
        public int RateLimitPerMinute { get; set; } = 60; // Max requests per minute
        public int CircuitBreakerFailureThreshold { get; set; } = 5; // Failures before circuit opens
        public int CircuitBreakerTimeoutSeconds { get; set; } = 30; // Circuit breaker timeout
        public bool EnableCircuitBreaker { get; set; } = true;
        public bool EnableRateLimiting { get; set; } = true;
        
        // Exponential backoff settings
        public bool UseExponentialBackoff { get; set; } = true;
        public double BackoffMultiplier { get; set; } = 2.0;
        public int MaxBackoffSeconds { get; set; } = 60;
        
        // Message batching
        public bool EnableBatching { get; set; } = false;
        public int BatchSize { get; set; } = 10;
        public int BatchTimeoutSeconds { get; set; } = 5;
    }
}
