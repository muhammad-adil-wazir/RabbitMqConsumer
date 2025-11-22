using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQConsumer.Models;
using System.Text;
using Newtonsoft.Json;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace RabbitMQConsumer.Services
{
    public class RabbitMQConsumerService : BackgroundService
    {
        private readonly ILogger<RabbitMQConsumerService> _logger;
        private readonly RabbitMQSettings _settings;
        private readonly PeriodicScreeningAPISettings _apiSettings;
        private readonly ClientAPIBaseUrlSettings _clientApiBaseUrls;
        private IConnection? _connection;
        private IModel? _channel;
        
        // Flow control components
        private readonly RateLimiter _rateLimiter;
        private readonly CircuitBreaker _circuitBreaker;
        private readonly ConcurrencyLimiter _concurrencyLimiter;
        private readonly ConcurrentQueue<(EntityMessage message, ulong deliveryTag)> _messageBatch;
        private readonly Timer _batchTimer;

        public RabbitMQConsumerService(ILogger<RabbitMQConsumerService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _settings = configuration.GetSection("RabbitMQ").Get<RabbitMQSettings>() ?? new RabbitMQSettings();
            _apiSettings = configuration.GetSection("PeriodicScreeningAPI").Get<PeriodicScreeningAPISettings>() ?? new PeriodicScreeningAPISettings();
            _clientApiBaseUrls = configuration.GetSection("ClientAPIBaseUrl").Get<ClientAPIBaseUrlSettings>() ?? new ClientAPIBaseUrlSettings();
            
            // Initialize flow control components
            _rateLimiter = new RateLimiter(_apiSettings.RateLimitPerMinute);
            _circuitBreaker = new CircuitBreaker(
                _apiSettings.CircuitBreakerFailureThreshold, 
                TimeSpan.FromSeconds(_apiSettings.CircuitBreakerTimeoutSeconds));
            _concurrencyLimiter = new ConcurrencyLimiter(_apiSettings.MaxConcurrentRequests);
            _messageBatch = new ConcurrentQueue<(EntityMessage message, ulong deliveryTag)>();
            
            // Initialize batch timer if batching is enabled
            if (_apiSettings.EnableBatching)
            {
                _batchTimer = new Timer(ProcessBatch, null, 
                    TimeSpan.FromSeconds(_apiSettings.BatchTimeoutSeconds), 
                    TimeSpan.FromSeconds(_apiSettings.BatchTimeoutSeconds));
            }
            else
            {
                _batchTimer = null!;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await StartConsuming(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while consuming messages");
            }
        }

        private async Task StartConsuming(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting RabbitMQ Consumer Service...");

            // Create connection
            var factory = new ConnectionFactory
            {
                HostName = _settings.HostName,
                Port = _settings.Port,
                UserName = _settings.UserName,
                Password = _settings.Password,
                VirtualHost = _settings.VirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            try
            {
                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();

                // Declare queue (create if doesn't exist)
                _channel.QueueDeclare(
                    queue: _settings.QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                // Set QoS (Quality of Service) to control message flow
                _channel.BasicQos(
                    prefetchSize: 0,
                    prefetchCount: _settings.PrefetchCount,
                    global: _settings.PrefetchGlobal);

                _logger.LogInformation($"Connected to RabbitMQ. Listening on queue: {_settings.QueueName}");
                _logger.LogInformation($"QoS Settings - PrefetchCount: {_settings.PrefetchCount}, PrefetchGlobal: {_settings.PrefetchGlobal}");
                _logger.LogInformation($"Flow Control Settings - MaxConcurrentRequests: {_apiSettings.MaxConcurrentRequests}, RateLimitPerMinute: {_apiSettings.RateLimitPerMinute}");

                // Set up consumer
                var consumer = new EventingBasicConsumer(_channel);
                consumer.Received += async (model, ea) =>
                {
                    await ProcessMessage(ea);
                };

                // Start consuming
                _channel.BasicConsume(
                    queue: _settings.QueueName,
                    autoAck: false,
                    consumer: consumer);

                _logger.LogInformation("Consumer started successfully. Waiting for messages...");

                // Keep the service running
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start consuming messages");
                throw;
            }
        }

        private async Task ProcessMessage(BasicDeliverEventArgs ea)
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var routingKey = ea.RoutingKey;

            try
            {
                _logger.LogInformation($"Received message with routing key: {routingKey}");
                _logger.LogInformation($"Message content: {message}");

                // Parse the JSON message
                var entityMessage = JsonConvert.DeserializeObject<EntityMessage>(message);
                
                if (entityMessage != null)
                {
                    // Check if batching is enabled
                    if (_apiSettings.EnableBatching)
                    {
                        await ProcessMessageWithBatching(entityMessage, ea);
                    }
                    else
                    {
                        await ProcessMessageWithFlowControl(entityMessage, ea);
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to deserialize message as EntityMessage");
                    
                    // Reject the message and don't requeue
                    try
                    {
                        _channel?.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                    }
                    catch (Exception nackEx)
                    {
                        _logger.LogError(nackEx, $"Failed to reject message with delivery tag {ea.DeliveryTag}");
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse JSON message: {Message}", message);
                
                // Reject the message and don't requeue
                try
                {
                    _channel?.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                }
                catch (Exception nackEx)
                {
                    _logger.LogError(nackEx, $"Failed to reject message with delivery tag {ea.DeliveryTag}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message: {Message}", message);
                
                // Reject the message and requeue for retry
                try
                {
                    _channel?.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                }
                catch (Exception nackEx)
                {
                    _logger.LogError(nackEx, $"Failed to reject message with delivery tag {ea.DeliveryTag}");
                }
            }
        }

        private async Task ProcessMessageWithBatching(EntityMessage entityMessage, BasicDeliverEventArgs ea)
        {
            // Add message to batch with delivery tag
            _messageBatch.Enqueue((entityMessage, ea.DeliveryTag));
            
            _logger.LogInformation($"Added message to batch. Current batch size: {_messageBatch.Count}");
            
            // If batch is full, process it immediately
            if (_messageBatch.Count >= _apiSettings.BatchSize)
            {
                await ProcessBatchImmediate();
            }
            
            // Note: Messages are acknowledged after successful batch processing
        }

        private async Task ProcessMessageWithFlowControl(EntityMessage entityMessage, BasicDeliverEventArgs ea)
        {
            try
            {
                // Apply concurrency limiting
                await _concurrencyLimiter.ExecuteAsync(async () =>
                {
                    // Check circuit breaker
                    if (_apiSettings.EnableCircuitBreaker && _circuitBreaker.IsCircuitOpen())
                    {
                        _logger.LogWarning("Circuit breaker is open, rejecting message");
                        throw new InvalidOperationException("Circuit breaker is open");
                    }

                    // Apply rate limiting
                    if (_apiSettings.EnableRateLimiting)
                    {
                        await _rateLimiter.WaitForAvailabilityAsync();
                    }

                    // Call the API endpoint with circuit breaker protection
                    if (_apiSettings.EnableCircuitBreaker)
                    {
                        await _circuitBreaker.ExecuteAsync<bool>(async () =>
                        {
                            await CallPeriodicScreeningAPI(entityMessage);
                            return true; // Return a dummy value since we don't need the result
                        });
                    }
                    else
                    {
                        await CallPeriodicScreeningAPI(entityMessage);
                    }
                });
                
                // Acknowledge the message
                try
                {
                    _channel?.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                }
                catch (Exception ackEx)
                {
                    _logger.LogError(ackEx, $"Failed to acknowledge message with delivery tag {ea.DeliveryTag}");
                }
                
                _logger.LogInformation($"Successfully processed entity message with {entityMessage.EntityIds.Count} entities");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Circuit breaker"))
            {
                _logger.LogWarning("Circuit breaker is open, requeuing message");
                try
                {
                    _channel?.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                }
                catch (Exception nackEx)
                {
                    _logger.LogError(nackEx, $"Failed to reject message with delivery tag {ea.DeliveryTag}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message with flow control");
                try
                {
                    _channel?.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                }
                catch (Exception nackEx)
                {
                    _logger.LogError(nackEx, $"Failed to reject message with delivery tag {ea.DeliveryTag}");
                }
            }
        }

        private async Task ProcessBatchImmediate()
        {
            await ProcessBatchInternal();
        }

        private async void ProcessBatch(object? state)
        {
            await ProcessBatchInternal();
        }

        private async Task ProcessBatchInternal()
        {
            if (_messageBatch.IsEmpty) return;

            var batch = new List<(EntityMessage message, ulong deliveryTag)>();
            while (_messageBatch.TryDequeue(out var item))
            {
                batch.Add(item);
            }

            if (batch.Count == 0) return;

            _logger.LogInformation($"Processing batch of {batch.Count} messages");

            try
            {
                // Apply concurrency limiting for batch processing
                await _concurrencyLimiter.ExecuteAsync(async () =>
                {
                    // Check circuit breaker
                    if (_apiSettings.EnableCircuitBreaker && _circuitBreaker.IsCircuitOpen())
                    {
                        _logger.LogWarning("Circuit breaker is open, rejecting batch");
                        throw new InvalidOperationException("Circuit breaker is open");
                    }

                    // Apply rate limiting
                    if (_apiSettings.EnableRateLimiting)
                    {
                        await _rateLimiter.WaitForAvailabilityAsync();
                    }

                    // Process batch
                    if (_apiSettings.EnableCircuitBreaker)
                    {
                        await _circuitBreaker.ExecuteAsync<bool>(async () =>
                        {
                            await CallPeriodicScreeningAPIBatch(batch.Select(x => x.message).ToList());
                            return true; // Return a dummy value since we don't need the result
                        });
                    }
                    else
                    {
                        await CallPeriodicScreeningAPIBatch(batch.Select(x => x.message).ToList());
                    }
                });

                // Acknowledge all messages in the batch after successful processing
                foreach (var (_, deliveryTag) in batch)
                {
                    try
                    {
                        _channel?.BasicAck(deliveryTag: deliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to acknowledge message with delivery tag {deliveryTag}");
                    }
                }

                _logger.LogInformation($"Successfully processed and acknowledged batch of {batch.Count} messages");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing batch");
                
                // Reject all messages in the batch
                foreach (var (_, deliveryTag) in batch)
                {
                    try
                    {
                        _channel?.BasicNack(deliveryTag: deliveryTag, multiple: false, requeue: true);
                    }
                    catch (Exception nackEx)
                    {
                        _logger.LogError(nackEx, $"Failed to reject message with delivery tag {deliveryTag}");
                    }
                }
            }
        }

        private string GetClientBaseUrl(string client)
        {
            return client.ToLowerInvariant() switch
            {
                "test" => _clientApiBaseUrls.Test,
                "demo" => _clientApiBaseUrls.Demo,
                "production" => _clientApiBaseUrls.Production,
                _ => _clientApiBaseUrls.Test // Default to Test if client is not recognized
            };
        }

        private async Task CallPeriodicScreeningAPI(EntityMessage entityMessage)
        {
            var maxRetries = _apiSettings.MaxRetries;
            var baseRetryDelay = TimeSpan.FromSeconds(_apiSettings.RetryDelaySeconds);
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation($"=== Calling Periodic Screening API (Attempt {attempt}/{maxRetries}) ===");
                    
                    using var httpClient = new HttpClient();
                    
                    // Set timeout
                    httpClient.Timeout = TimeSpan.FromSeconds(_apiSettings.TimeoutSeconds);
                    
                    // Get client-specific base URL
                    var clientBaseUrl = GetClientBaseUrl(entityMessage.Client);
                    
                    // Build the API URL with query parameters
                    var apiUrl = $"{clientBaseUrl}{_apiSettings.Endpoint}?entityTypeId={entityMessage.EntityTypeId}&configId={entityMessage.ConfigId}&systemType={entityMessage.SystemType}";
                    
                    _logger.LogInformation($"API URL: {apiUrl}");
                    _logger.LogInformation($"Entity IDs to send: [{string.Join(", ", entityMessage.EntityIds)}]");
                    _logger.LogInformation($"Timeout: {_apiSettings.TimeoutSeconds} seconds");
                    
                    // Convert entity IDs to JSON array
                    var jsonPayload = JsonConvert.SerializeObject(entityMessage.EntityIds);
                    
                    // Create HTTP content
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    
                    // Make the API call with cancellation token
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_apiSettings.TimeoutSeconds));
                    var response = await httpClient.PostAsync(apiUrl, content, cts.Token);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation($"✅ API call successful! Status: {response.StatusCode}");
                        _logger.LogInformation($"API Response: {responseContent}");
                        _logger.LogInformation("=== End API Call ===");
                        return; // Success - exit retry loop
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning($"⚠️ API call failed! Status: {response.StatusCode}");
                        _logger.LogWarning($"Error Response: {errorContent}");
                        
                        if (attempt < maxRetries)
                        {
                            var retryDelay = CalculateRetryDelay(attempt, baseRetryDelay);
                            _logger.LogInformation($"Retrying in {retryDelay.TotalSeconds} seconds...");
                            await Task.Delay(retryDelay);
                        }
                    }
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    _logger.LogError($"⏰ API call timed out after {_apiSettings.TimeoutSeconds} seconds (Attempt {attempt}/{maxRetries})");
                    
                    if (attempt < maxRetries)
                    {
                        var retryDelay = CalculateRetryDelay(attempt, baseRetryDelay);
                        _logger.LogInformation($"Retrying in {retryDelay.TotalSeconds} seconds...");
                        await Task.Delay(retryDelay);
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError($"🌐 HTTP request failed (Attempt {attempt}/{maxRetries}): {ex.Message}");
                    
                    if (attempt < maxRetries)
                    {
                        var retryDelay = CalculateRetryDelay(attempt, baseRetryDelay);
                        _logger.LogInformation($"Retrying in {retryDelay.TotalSeconds} seconds...");
                        await Task.Delay(retryDelay);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Unexpected error calling API (Attempt {attempt}/{maxRetries})");
                    
                    if (attempt < maxRetries)
                    {
                        var retryDelay = CalculateRetryDelay(attempt, baseRetryDelay);
                        _logger.LogInformation($"Retrying in {retryDelay.TotalSeconds} seconds...");
                        await Task.Delay(retryDelay);
                    }
                }
            }
            
            // If we get here, all retries failed
            _logger.LogError($"❌ All {maxRetries} attempts failed. Throwing exception to requeue message.");
            throw new Exception($"API call failed after {maxRetries} attempts");
        }

        private TimeSpan CalculateRetryDelay(int attempt, TimeSpan baseDelay)
        {
            if (!_apiSettings.UseExponentialBackoff)
            {
                return baseDelay;
            }

            // Calculate exponential backoff: baseDelay * (multiplier ^ (attempt - 1))
            var exponentialDelay = TimeSpan.FromMilliseconds(
                baseDelay.TotalMilliseconds * Math.Pow(_apiSettings.BackoffMultiplier, attempt - 1));

            // Cap the delay at the maximum backoff time
            var maxDelay = TimeSpan.FromSeconds(_apiSettings.MaxBackoffSeconds);
            
            return exponentialDelay > maxDelay ? maxDelay : exponentialDelay;
        }

        private async Task CallPeriodicScreeningAPIBatch(List<EntityMessage> batch)
        {
            _logger.LogInformation($"=== Calling Periodic Screening API for Batch of {batch.Count} messages ===");
            
            // Combine all entity IDs from the batch
            var allEntityIds = batch.SelectMany(m => m.EntityIds).ToList();
            
            // Use the first message's parameters (assuming they're the same for all messages in batch)
            var firstMessage = batch.First();
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(_apiSettings.TimeoutSeconds);
            
            // Get client-specific base URL
            var clientBaseUrl = GetClientBaseUrl(firstMessage.Client);
            
            var apiUrl = $"{clientBaseUrl}{_apiSettings.Endpoint}?entityTypeId={firstMessage.EntityTypeId}&configId={firstMessage.ConfigId}&systemType={firstMessage.SystemType}";
            
            _logger.LogInformation($"Batch API URL: {apiUrl}");
            _logger.LogInformation($"Total Entity IDs in batch: {allEntityIds.Count}");
            
            var jsonPayload = JsonConvert.SerializeObject(allEntityIds);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_apiSettings.TimeoutSeconds));
            var response = await httpClient.PostAsync(apiUrl, content, cts.Token);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"✅ Batch API call successful! Status: {response.StatusCode}");
                _logger.LogInformation($"Batch API Response: {responseContent}");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"❌ Batch API call failed! Status: {response.StatusCode}");
                _logger.LogError($"Error Response: {errorContent}");
                throw new Exception($"Batch API call failed with status: {response.StatusCode}");
            }
        }

        public override void Dispose()
        {
            _batchTimer?.Dispose();
            _channel?.Close();
            _connection?.Close();
            base.Dispose();
        }
    }
}
