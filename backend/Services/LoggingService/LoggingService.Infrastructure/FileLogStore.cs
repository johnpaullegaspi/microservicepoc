using BuildingBlocks.Contracts;
using LoggingService.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LoggingService.Infrastructure;

public class FileLogOptions
{
    public const string Section = "FileLogging";
    public string Directory { get; set; } = "logs";
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // size-based rotation
}

/// <summary>
/// Appends formatted entries to {Directory}/{ServiceName}-{yyyyMMdd}[.n].log
/// Date-based rotation (new file per UTC day) + size-based rotation (.1, .2 ... when MaxFileSizeBytes exceeded).
/// </summary>
public class FileLogStore(IOptions<FileLogOptions> options) : ILogStore
{
    private readonly FileLogOptions _o = options.Value;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task AppendAsync(ApplicationLogEvent log, CancellationToken ct)
    {
        var text = LogFormatter.Format(log);
        var bytes = System.Text.Encoding.UTF8.GetByteCount(text);
        var service = string.Concat(log.ServiceName.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')); // path-safe
        if (service.Length == 0) service = "unknown";

        await Gate.WaitAsync(ct);
        try
        {
            System.IO.Directory.CreateDirectory(_o.Directory);
            var path = ResolvePath(service, log.Timestamp.ToUniversalTime(), bytes);
            await File.AppendAllTextAsync(path, text, ct);
        }
        finally { Gate.Release(); }
    }

    private string ResolvePath(string service, DateTime ts, int incomingBytes)
    {
        var baseName = $"{service}-{ts:yyyyMMdd}";
        for (var i = 0; ; i++)
        {
            var path = Path.Combine(_o.Directory, i == 0 ? $"{baseName}.log" : $"{baseName}.{i}.log");
            if (!File.Exists(path) || new FileInfo(path).Length + incomingBytes <= _o.MaxFileSizeBytes) return path;
        }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddLoggingInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<FileLogOptions>(config.GetSection(FileLogOptions.Section));
        services.AddSingleton<ILogStore, FileLogStore>();
        return services;
    }
}
