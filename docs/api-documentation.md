# REST API
All through the gateway (`http://localhost:8080`). Errors are `application/problem+json` `{title,status,errors,correlationId}`. Every response carries `X-Correlation-Id`.

## OrderService
| Method | Path | Notes |
|---|---|---|
| GET | /api/products | list products |
| GET | /api/products/{id} | |
| POST | /api/orders | body `{customerName, customerEmail, contactNumber, deliveryAddress, paymentMethod: CreditCard|DebitCard|CashOnDelivery, items:[{productId, quantity 1-20}]}` → 201 order (Pending). 400 validation, 404 unknown product, 409 insufficient stock |
| GET | /api/orders/{id} | |
| GET | /api/orders/number/{orderNumber} | |
| GET | /api/orders/customer/{email} | newest first |
| POST | /api/orders/{id}/cancel | body `{reason?}`; only Pending/PaymentFailed, else 409 |

## PaymentService (internal – not routed by the gateway; Swagger at :5102 in dev)
| Method | Path | Notes |
|---|---|---|
| POST | /api/payments/process | needs `X-Internal-Api-Key`; idempotent on `idempotencyKey` |
| GET | /api/payments/{id} | |
| GET | /api/payments/order/{orderId} | |
| POST | /api/payments/{id}/refund | needs `X-Internal-Api-Key`; mock refund |

## LoggingService (internal)
`POST /api/logging` (body `{serviceName,eventType,logLevel,message,correlationId?,orderId?,transactionId?,traceId?,timestamp?,exceptionDetails?}` → 202) · `GET /api/logging/health`.

## Payment simulator
`PaymentGateway__Mode`: `Rules` (default; e-mail containing `fail` is declined), `AlwaysSucceed`, `AlwaysFail`, `Random` (`FailureRate`). `DelayMs` simulates latency.
