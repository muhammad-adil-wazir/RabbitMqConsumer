# RabbitMQ .NET Core Consumer Application

This .NET Core application consumes messages from RabbitMQ and processes order data.

## Prerequisites

- .NET 8.0 SDK installed
- RabbitMQ running (use the Docker setup from this project)
- Visual Studio Code or Visual Studio

## Project Structure

```
RabbitMQConsumer/
├── Models/
│   └── OrderModels.cs          # Data models for orders
├── Services/
│   └── RabbitMQConsumerService.cs  # Main consumer service
├── Program.cs                   # Application entry point
├── RabbitMQConsumer.csproj     # Project file
└── appsettings.json           # Configuration
```

## Features

- **Background Service**: Runs continuously to consume messages
- **Automatic Reconnection**: Handles RabbitMQ connection failures
- **Error Handling**: Proper message acknowledgment and error recovery
- **JSON Parsing**: Deserializes order messages from JSON
- **Logging**: Comprehensive logging for monitoring and debugging
- **Configuration**: Flexible configuration via appsettings.json

## Quick Start

### 1. Build and Run
```bash
# Restore packages
dotnet restore

# Build the application
dotnet build

# Run the consumer
dotnet run
```

### 2. Test Message Publishing
Use the PowerShell script or cURL commands from earlier to publish messages:

```powershell
# PowerShell example
$headers = @{
    "Content-Type" = "application/json"
    "Authorization" = "Basic YWRtaW46YWRtaW4xMjM="
}

$messageBody = @{
    properties = @{
        content_type = "application/json"
    }
    routing_key = "periodic-screening"
    payload = '{"orderId": "ORD-001", "customer": "John Doe", "email": "john@example.com", "items": [{"product": "Laptop", "quantity": 1, "price": 999.99}], "total": 999.99, "status": "pending"}'
    payload_encoding = "string"
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri "http://localhost:15672/api/exchanges/%2F/amq.default/publish" -Method 'POST' -Headers $headers -Body $messageBody
```

### 3. Monitor Output
The application will display:
- Connection status
- Received messages
- Processing details
- Error messages (if any)

## Configuration

Edit `appsettings.json` to customize:

```json
{
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "admin",
    "Password": "admin123",
    "VirtualHost": "/",
    "QueueName": "periodic-screening",
    "ExchangeName": "amq.default",
    "RoutingKey": "periodic-screening"
  }
}
```

## Message Processing

The application processes messages with this structure:

```json
{
  "entityIds": [1001, 1002, 1003],
  "entityTypeId": 5,
  "configId": 10,
  "systemType": 1
}
```

### Message Fields:
- **entityIds**: Array of entity IDs to process
- **entityTypeId**: Type identifier for the entities
- **configId**: Configuration ID to apply
- **systemType**: System type identifier

## Error Handling

- **Invalid JSON**: Messages are rejected and not requeued
- **Processing Errors**: Messages are requeued for retry
- **Connection Issues**: Automatic reconnection with 10-second intervals
- **Acknowledgment**: Proper message acknowledgment to prevent duplicates

## Logging

The application logs:
- Connection status
- Message reception
- Processing details
- Errors and exceptions
- Order information

## Development

### Adding New Message Types
1. Create new models in `Models/OrderModels.cs`
2. Update the deserialization logic in `RabbitMQConsumerService.cs`
3. Add processing logic for the new message type

### Customizing Processing Logic
Modify the `ProcessOrderMessage` method in `RabbitMQConsumerService.cs` to:
- Save to database
- Send emails
- Update inventory
- Process payments
- Generate reports

## Troubleshooting

### Connection Issues
1. Verify RabbitMQ is running: `docker ps`
2. Check connection settings in `appsettings.json`
3. Verify credentials: admin/admin123

### Message Processing Issues
1. Check message format matches expected JSON structure
2. Review logs for deserialization errors
3. Verify queue name matches configuration

### Performance Issues
1. Adjust `BasicQos` settings for prefetch count
2. Implement message batching if needed
3. Add database connection pooling

## Next Steps

1. **Database Integration**: Add Entity Framework for data persistence
2. **Email Service**: Integrate with email service for notifications
3. **API Endpoints**: Add REST API for monitoring and management
4. **Docker Support**: Containerize the application
5. **Health Checks**: Add health check endpoints
6. **Metrics**: Integrate with monitoring tools like Prometheus

## Example Output

```
Starting RabbitMQ Consumer Application...
==========================================
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      Starting RabbitMQ Consumer Service...
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      Connected to RabbitMQ. Listening on queue: periodic-screening
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      Consumer started successfully. Waiting for messages...
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      Received message with routing key: periodic-screening
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      === Processing Entity Message ===
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      Entity Type ID: 5
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      Config ID: 10
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      System Type: 1
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      Entity IDs Count: 3
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      Entity IDs:
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
        - Entity ID: 1001
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
        - Entity ID: 1002
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
        - Entity ID: 1003
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      Entity message processed successfully for 3 entities!
info: RabbitMQConsumer.Services.RabbitMQConsumerService[0]
      === End Processing ===
```