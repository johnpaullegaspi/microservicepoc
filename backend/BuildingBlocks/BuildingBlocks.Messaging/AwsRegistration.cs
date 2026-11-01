using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

public static class AwsRegistration
{
    public static IServiceCollection AddAwsMessaging(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AwsOptions>(config.GetSection(AwsOptions.Section));
        var o = config.GetSection(AwsOptions.Section).Get<AwsOptions>() ?? new AwsOptions();
        var local = !string.IsNullOrWhiteSpace(o.ServiceUrl);

        services.AddSingleton<IAmazonSQS>(_ => local
            ? new AmazonSQSClient(new BasicAWSCredentials(o.AccessKey, o.SecretKey),
                new AmazonSQSConfig { ServiceURL = o.ServiceUrl, AuthenticationRegion = o.Region })
            : new AmazonSQSClient(RegionEndpoint.GetBySystemName(o.Region)));

        services.AddSingleton<IAmazonSimpleNotificationService>(_ => local
            ? new AmazonSimpleNotificationServiceClient(new BasicAWSCredentials(o.AccessKey, o.SecretKey),
                new AmazonSimpleNotificationServiceConfig { ServiceURL = o.ServiceUrl, AuthenticationRegion = o.Region })
            : new AmazonSimpleNotificationServiceClient(RegionEndpoint.GetBySystemName(o.Region)));

        services.AddSingleton<AwsResourceResolver>();
        services.AddSingleton<IMessagePublisher, SqsSnsMessagePublisher>();
        return services;
    }
}
