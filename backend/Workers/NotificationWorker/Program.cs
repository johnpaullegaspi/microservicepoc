using BuildingBlocks.Contracts;
using BuildingBlocks.Logging.Client;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using NotificationWorker;
using NotificationWorker.Providers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAwsMessaging(builder.Configuration);
builder.Services.AddBusinessLogging("NotificationWorker");
builder.Services.AddObservability("NotificationWorker", aspNet: false);
builder.Services.Configure<MockNotificationOptions>(builder.Configuration.GetSection(MockNotificationOptions.Section));
builder.Services.AddSingleton<INotificationProvider, MockNotificationProvider>();
builder.Services.AddSqsConsumer<CustomerNotificationEvent, NotificationHandler>(QueueNames.Notification);

await builder.Build().RunAsync();
