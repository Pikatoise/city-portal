# Services and Diagnostics

| Address                                  | Purpose                                               |
| ---------------------------------------- | ----------------------------------------------------- |
| `https://localhost:8443/`                | Angular application through the HTTPS proxy.          |
| `https://localhost:8443/api/system/info` | Public foundation metadata.                           |
| `https://localhost:8443/health/live`     | API process liveness.                                 |
| `https://localhost:8443/health/ready`    | API dependency readiness.                             |
| `https://localhost:8443/openapi/v1.json` | Development OpenAPI contract.                         |
| `https://localhost:8443/scalar/v1`       | Development API explorer.                             |
| `http://localhost:5080/`                 | Direct loopback API for host tooling.                 |
| `http://localhost:5081/health/ready`     | Worker dependency readiness.                          |
| `http://localhost:8025/`                 | Mailpit captured Email interface.                     |
| `localhost:1025`                         | Mailpit SMTP listener for local confirmation Email.   |
| `http://localhost:15672/`                | RabbitMQ management UI with `.env` credentials.       |
| `localhost:5432`                         | PostgreSQL database `cityportal`, user `cityportal`.  |
| `localhost:5672`                         | RabbitMQ AMQP.                                        |
| `http://localhost:9000/`                 | Private RustFS S3 endpoint; credentials required.      |

All published ports bind to loopback. PostgreSQL, RabbitMQ, Mailpit, RustFS, and Caddy
retain their state in named volumes across `docker compose down` and restart.
Never remove volumes as a routine recovery step.

```powershell
docker compose logs --tail 100 api worker
docker compose stop
docker compose start
docker compose down
```

## References

- [PostgreSQL official image and volume layout](https://hub.docker.com/_/postgres).
- [RabbitMQ official image](https://hub.docker.com/_/rabbitmq).
