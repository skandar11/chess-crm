using ChessCrm.Bot.Configuration;
using ChessCrm.Bot.Handlers;
using ChessCrm.Bot.Services;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

// Ищем .env рядом с проектом, независимо от рабочей директории
var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
if (!File.Exists(envPath))
    envPath = Path.Combine(Directory.GetCurrentDirectory(), "ChessCrm.Bot", ".env");
if (!File.Exists(envPath))
    envPath = ".env";
Env.Load(envPath);

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
        services.AddSingleton<PendingActionService>();

        services.AddHttpClient<AiService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddTransient<DatabaseService>();

        services.AddHttpClient<IntentService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddTransient<GoogleSheetsService>();
        services.AddTransient<AddToGroupHandler>();
        services.AddTransient<SubscriptionHandler>();
        services.AddTransient<NewStudentHandler>();
        services.AddTransient<EditStudentHandler>();
        services.AddTransient<RemoveFromGroupHandler>();
        services.AddTransient<TransferGroupHandler>();
        services.AddTransient<InviteHandler>();
        services.AddTransient<ParentOnboardingHandler>();
        services.AddTransient<ParentCommandsHandler>();
        services.AddHostedService<TelegramBotService>();
    })
    .Build();

await host.RunAsync();