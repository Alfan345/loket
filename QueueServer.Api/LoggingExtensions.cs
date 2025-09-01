using Serilog;

namespace QueueServer.Api;
public static class LoggingExtensions
{
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((ctx, lc) =>
            lc.ReadFrom.Configuration(ctx.Configuration)
              .Enrich.FromLogContext()
              .Enrich.WithProperty("App","QueueServer.Api"));
        return builder;
    }
}