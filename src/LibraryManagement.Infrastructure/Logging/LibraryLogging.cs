using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace LibraryManagement.Infrastructure.Logging;

public static class LibraryLogging
{
    public static IHostBuilder UseLibraryLogging(this IHostBuilder host) => host.UseSerilog((context, services, log) => log
        .ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services)
        .Enrich.FromLogContext().WriteTo.Console()
        .WriteTo.File("logs/library-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

    public static IApplicationBuilder UseLibraryRequestLogging(this IApplicationBuilder app) => app.UseSerilogRequestLogging(options =>
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms");
}
