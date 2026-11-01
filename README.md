# Microservices E-Commerce POC (Nike shoe store)

Dockerized .NET 10 microservices + React/Vite storefront. Orders, payments and logging are independent services with their own storage, talking through **Amazon SQS/SNS** (LocalStack locally – no AWS account needed).

| Piece | Tech | Storage |
|---|---|---|
| OrderService | ASP.NET Core, Clean Architecture, CQRS (MediatR), FluentValidation, EF Core | SQL Server |
| PaymentService | same + simulated gateway, idempotency | PostgreSQL |
| LoggingService | ASP.NET Core + SQS consumer | rotating text files (Docker volume) |
| NotificationWorker | .NET Worker Service, mock provider | `notifications.txt` |
| Gateway | YARP reverse proxy (single entry point) | – |
| Frontend | React + TypeScript + Vite (nginx in Docker) | – |
| Messaging | SQS queues + DLQs, SNS topics (LocalStack) | – |

> **Status honesty:** the frontend was built (`tsc` + `vite build`) and the Compose/CFN/JSON/YAML files parsed in the authoring environment, but that environment had **no .NET SDK or NuGet access**, so the C# solution has **not yet been compiled or run**. First step on your machine: build the solution and run the tests (see "Verify the build" below), then fix any compile error you meet – expect a few small ones.

## Run option B – Docker CLI (primary)

```bash
copy .env.example .env        # (cp on macOS/Linux) then edit passwords – .env is git-ignored
docker compose up --build
```
* Store: http://localhost:3000   · Gateway/API: http://localhost:8080
* Swagger (dev override): Order http://localhost:5101/swagger · Payment http://localhost:5102/swagger · Logging http://localhost:5103/swagger
* Stop: `docker compose down` · Stop **and delete all data** (SQL Server, PostgreSQL, logs volumes): `docker compose down -v`
* Logs: `docker compose logs -f orderservice` (also `paymentservice`, `loggingservice`, `notificationworker`, `gateway`, `localstack`)
* Business logs written by LoggingService: `docker compose exec loggingservice ls /var/log/microservices` (and `cat` a file); notifications: `notifications.txt` in the same folder.
* Hot-reload frontend: `docker compose --profile dev up frontend-dev` → http://localhost:5173

Try it: add shoes → checkout → watch the "Processing" indicator turn to **Confirmed**. Use an e-mail containing `fail` (e.g. `bob.fail@example.com`) to simulate a declined payment.

## Run option A – Visual Studio 2022 (17.x) Community or later
1. `copy .env.example .env`, edit passwords.
2. Open `MicroservicesPOC.sln` (22 projects in solution folders + the `docker-compose` project).
3. Set **docker-compose** as startup project → **F5**. VS builds the images and starts all containers; the browser opens the store. Breakpoints in controllers/handlers/consumers work (VS attaches the debugger to .NET containers).
4. If container debugging misbehaves on your VS/SDK combination, use the alternative below.

**Alternative – debug on the host, infra in Docker** (also reliable for debugging background consumers):
```bash
docker compose up -d sqlserver postgres localstack localstack-init
cd backend/Services/OrderService/OrderService.API
dotnet user-secrets set "ConnectionStrings:OrderDb" "Server=localhost,14330;Database=OrderDb;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True"
cd ../../PaymentService/PaymentService.API
dotnet user-secrets set "ConnectionStrings:PaymentDb" "Host=localhost;Port=54320;Database=paymentdb;Username=postgres;Password=<POSTGRES_PASSWORD>"
```
Then in VS choose the startup profile **"All services (local debug)"** (multiple startup projects via `MicroservicesPOC.slnLaunch`) and press F5. Run the frontend with `cd frontend/nike-store && npm install && npm run dev` (proxies `/api` → http://localhost:5100). Local ports: Gateway 5100, Order 5101, Payment 5102, Logging 5103. Log files go to `./logs`.

## Verify the build
```bash
dotnet build MicroservicesPOC.sln
dotnet test  MicroservicesPOC.sln          # unit + messaging tests (no Docker needed)
# full purchase flow against the running compose stack:
set E2E_BASE_URL=http://localhost:8080 && dotnet test backend/Tests/Microservices.IntegrationTests
```
The `docker-compose` (.dcproj) project is built by Visual Studio only; `dotnet build` of the .sln may report it as unsupported – build the individual projects or use VS/`docker compose build`.

### EF Core migrations
Each service starts with `EnsureCreated` when no migrations exist, so everything works out of the box. To switch to code-first migrations (recommended once you evolve the schema):
```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p backend/Services/OrderService/OrderService.Infrastructure -s backend/Services/OrderService/OrderService.API -o Persistence/Migrations
dotnet ef migrations add InitialCreate -p backend/Services/PaymentService/PaymentService.Infrastructure -s backend/Services/PaymentService/PaymentService.API -o Persistence/Migrations
```
After a migration exists the services call `Database.MigrateAsync()` automatically on startup. (Delete the volumes first if the DB was created via `EnsureCreated`.)

## AWS (real) configuration
Set `AWS_SERVICE_URL=` (empty) so SDK clients use the default credential chain / IAM roles, set `AWS_REGION`, `AWS_AUTO_PROVISION=false`, and create the resources with `infrastructure/aws/messaging.cfn.yaml` (or `infrastructure/localstack/init-aws.sh` with real credentials and `AWS_ENDPOINT` unset). Example IAM policy: `infrastructure/aws/iam-policy-example.json`. See `docs/deployment-guide.md`.

## Docs
`docs/architecture.md` · `docs/database-schema.md` · `docs/api-documentation.md` · `docs/sequence-diagrams.md` · `docs/deployment-guide.md`

## Troubleshooting
* **`MSSQL_SA_PASSWORD` is required** – you forgot `.env`.
* **SQL Server container unhealthy** – password too weak (needs upper/lower/digit/symbol, 8+ chars) or < 2 GB RAM for Docker.
* **Orders stay "Pending"** – check `docker compose logs paymentservice localstack-init`; `localstack-init` must have exited with code 0 (queues + SNS subscription created).
* **Order says Processing forever** – `docker compose logs orderservice` for the payment-result consumer; failed messages retry with exponential backoff then land in `payment-result-queue-dlq`: `docker compose exec localstack awslocal sqs receive-message --queue-url http://localhost:4566/000000000000/payment-result-queue-dlq`.
* **LocalStack image** is pinned to 3.8.1 because newer images require an auth token.
* **CRLF line endings in `init-aws.sh`** break the init job – `.gitattributes` forces LF; re-checkout if needed.
* **Port already in use** – edit the host ports in `docker-compose.override.yml`.
