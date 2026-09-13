using System.Text.Json;
using Hangfire;
using Hangfire.Storage.SQLite;
using PromoRepackager.Api.Jobs;
using PromoRepackager.Api.Models;
using PromoRepackager.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("GeminiClient", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("Scraper", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd(
        "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 6,
    PooledConnectionLifetime = TimeSpan.FromMinutes(10)
});

var rawDbConfig = builder.Configuration.GetConnectionString("SQLite") ?? "Data/deals.db";
var dbPath = rawDbConfig.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).TrimEnd(';');

var dbDirectory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage(dbPath));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
    options.ServerName = "PromoRepackager-Worker";
});

builder.Services.AddSingleton<IGeminiCuratorService, GeminiCuratorService>();
builder.Services.AddSingleton<IMetadataScraperService, MetadataScraperService>();
builder.Services.AddSingleton<IStoryImageGenerator, StoryImageGenerator>();
builder.Services.AddSingleton<ITelegramNotifierService, TelegramNotifierService>();
builder.Services.AddSingleton<IOfferRepository, OfferRepository>();
builder.Services.AddTransient<ProcessOfferJob>();

var app = builder.Build();

app.UseHangfireDashboard("/hangfire");

app.MapGet("/health", () => Results.Ok(new
{
    status = "Online",
    runtime = Environment.Version.ToString(),
    timestamp = DateTime.UtcNow
}));

app.MapPost("/api/webhook/evolution", async (
    HttpContext context,
    IBackgroundJobClient jobClient,
    ILogger<Program> logger) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var rawPayload = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(rawPayload))
        return Results.BadRequest(new { error = "Payload vazio." });

    var payload = JsonSerializer.Deserialize<EvolutionWebhookDto>(rawPayload);
    var remoteJid = payload?.Data?.Key?.RemoteJid ?? string.Empty;

    logger.LogInformation("[Webhook Evolution] Mensagem recebida de: {RemoteJid}", remoteJid);

    if (payload?.Data?.Key?.FromMe == true || !remoteJid.EndsWith("@g.us"))
    {
        return Results.Ok(new { status = "Ignorado: Não é mensagem recebida de grupo." });
    }


    jobClient.Enqueue<ProcessOfferJob>(job => job.ExecuteAsync(rawPayload, CancellationToken.None, false));
    return Results.Ok(new { status = "Enqueued", jid = remoteJid });
});

app.MapPost("/api/test/simulate", (
    string text,
    IBackgroundJobClient jobClient) =>
{
    if (string.IsNullOrWhiteSpace(text))
        return Results.BadRequest(new { error = "Texto não pode ser vazio." });

    var fakePayload = JsonSerializer.Serialize(new
    {
        @event = "messages.upsert",
        data = new
        {
            key = new { fromMe = false, id = Guid.NewGuid().ToString() },
            message = new { conversation = text }
        }
    });

    jobClient.Enqueue<ProcessOfferJob>(job => job.ExecuteAsync(fakePayload, CancellationToken.None, true));
    return Results.Ok(new { status = "SimulacaoEnfileirada", bypass = true, preview = text });
});

app.MapPost("/api/test/reset-history", async (IOfferRepository repository) =>
{
    await repository.ClearHistoryAsync();
    return Results.Ok(new { message = "Histórico e limite diário resetados com sucesso." });
});

app.Run();