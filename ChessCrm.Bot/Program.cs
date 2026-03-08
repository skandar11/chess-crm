using ChessCrm.Bot.Configuration;
using ChessCrm.Bot.Services;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

Env.Load();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}{NewLine}")
    .WriteTo.File("logs/bot-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog()
    .ConfigureServices((_, services) =>
    {
        services.AddSingleton<AppConfig>();

        services.AddHttpClient<AiService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddTransient<DatabaseService>();
        services.AddHostedService<TelegramBotService>();
    })
    .Build();

await host.RunAsync();