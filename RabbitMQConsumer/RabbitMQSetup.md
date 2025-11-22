# RabbitMQ Setup

## 1. Install Docker Desktop

- Download and install **Docker Desktop for Windows**.
- Ensure the **WSL2 backend** is enabled (preferred).

After installation, verify Docker is working by running:

---

## 2. Run RabbitMQ with Management Plugin

Run RabbitMQ using the following command:


- **5672**: RabbitMQ message broker port (applications connect here)
- **15672**: Management Web UI
- **rabbitmq:3-management**: Official RabbitMQ image with management plugin pre-enabled

---

## 3. Access RabbitMQ

- Open your browser and navigate to: [http://localhost:15672](http://localhost:15672)

**Login credentials:**

- **Username:** `guest`
- **Password:** `guest`


