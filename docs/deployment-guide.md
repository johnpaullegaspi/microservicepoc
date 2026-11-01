# Deployment guide (AWS)

1. **Messaging:** `aws cloudformation deploy --template-file infrastructure/aws/messaging.cfn.yaml --stack-name microservices-poc-messaging`.
2. **Databases:** RDS SQL Server (OrderService) and RDS PostgreSQL (PaymentService); store connection strings in Secrets Manager / SSM and inject as `ConnectionStrings__OrderDb`, `ConnectionStrings__PaymentDb`.
3. **Images:** build the five Dockerfiles (context = repo root; frontend context = `frontend/nike-store`) and push to ECR.
4. **Run** on ECS Fargate (one service per image). Task role = IAM policy from `infrastructure/aws/iam-policy-example.json`. Environment: `Aws__ServiceUrl` empty (default credential chain), `Aws__Region`, `Aws__AutoProvision=false`, `Internal__ApiKey` from a secret, `PaymentGateway__Mode`.
5. **Logging volume:** mount EFS at `/var/log/microservices` for LoggingService (and NotificationWorker), or switch to CloudWatch by replacing `ILogStore`.
6. **Front door:** ALB → frontend (nginx) and gateway; set `GATEWAY_URL` for nginx and the CORS origins on the gateway.
7. Scale OrderService/PaymentService horizontally – SQS consumers are competing consumers and the inbox/idempotency design tolerates duplicates. Run **one** LoggingService task per log directory (file writes are serialised by an in-process lock).
