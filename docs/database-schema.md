# Database schema

## OrderService – SQL Server (`OrderDb`)
**Products**: Id (PK, guid), ProductName(200), Description(1000), SKU(50, unique), Price decimal(18,2), StockQuantity (CHECK ≥ 0), ImageUrl(500), CreatedAt, UpdatedAt, RowVersion (concurrency). Seeded: Nike Air Max, Air Force 1, Dunk Low, Pegasus.

**Orders**: Id (PK), OrderNumber(40, unique), CustomerName, CustomerEmail (indexed), ContactNumber, DeliveryAddress, PaymentMethod, TotalAmount decimal(18,2), OrderStatus (Pending|Processing|Confirmed|PaymentFailed|Cancelled), PaymentStatus (Pending|Processing|Successful|Failed|Refunded), TransactionReference, FailureReason, CreatedAt, UpdatedAt.

**OrderItems**: Id (PK), OrderId (FK → Orders, cascade), ProductId (FK → Products, restrict), ProductName, Quantity, UnitPrice decimal(18,2), Subtotal decimal(18,2).

**OutboxMessages**: Id, EventType, DestinationKind (Queue/Topic), Destination, Payload (json envelope), CorrelationId, CreatedAt, ProcessedAt, Attempts, LastError. **InboxMessages**: (EventId, Consumer) PK, ProcessedAt.

## PaymentService – PostgreSQL (`paymentdb`)
**Payments**: Id (PK), OrderId (indexed), OrderNumber (indexed), CustomerEmail, Amount numeric(18,2) CHECK > 0, PaymentMethod, TransactionReference, IdempotencyKey (**unique**), PaymentStatus, FailureReason, CreatedAt, UpdatedAt. Plus OutboxMessages / InboxMessages as above.

## LoggingService – files
`{dir}/{ServiceName}-{yyyyMMdd}[.n].log` – new file per UTC day, `.n` suffix when `MaxFileSizeBytes` (10 MB) is exceeded.
