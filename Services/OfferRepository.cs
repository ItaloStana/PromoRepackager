using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PromoRepackager.Api.Services;

public record CuratedOfferRecord(
    string ProductHash,
    string Title,
    string Url,
    DateTime CreatedAtUtc
);

public interface IOfferRepository
{
    Task<bool> CanProcessAsync(string resolvedUrl, string productTitle, bool bypassChecks = false);
    Task RegisterAsync(string resolvedUrl, string productTitle);
    Task ClearHistoryAsync();
}

public class OfferRepository : IOfferRepository
{
    private readonly string _filePath;
    private readonly int _dailyLimit;
    private readonly ILogger<OfferRepository> _logger;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public OfferRepository(IConfiguration configuration, ILogger<OfferRepository> logger)
    {
        _logger = logger;
        _filePath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "history.json");
        _dailyLimit = configuration.GetValue<int>("PromoRepackager:DailyLimit", 25);

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task<bool> CanProcessAsync(string resolvedUrl, string productTitle, bool bypassChecks = false)
    {
        if (bypassChecks)
        {
            _logger.LogInformation("[Filtro] Bypass ativado: ignorando limite e duplicidade para teste.");
            return true;
        }

        await _lock.WaitAsync();
        try
        {
            var records = await LoadRecordsAsync();
            var today = DateTime.UtcNow.Date;

            var totalToday = records.Count(x => x.CreatedAtUtc >= today);
            if (totalToday >= _dailyLimit)
            {
                _logger.LogWarning("[Filtro] Teto diário atingido ({Total}/{Limit}).", totalToday, _dailyLimit);
                return false;
            }

            var cutoff = DateTime.UtcNow.AddHours(-48);
            var hash = GenerateHash(resolvedUrl, productTitle);

            var existing = records.FirstOrDefault(x => x.ProductHash == hash && x.CreatedAtUtc >= cutoff);
            if (existing != null)
            {
                _logger.LogWarning("[Filtro] Oferta duplicada ignorada: '{Titulo}' postada em {Data}.", existing.Title, existing.CreatedAtUtc);
                return false;
            }

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RegisterAsync(string resolvedUrl, string productTitle)
    {
        await _lock.WaitAsync();
        try
        {
            var records = await LoadRecordsAsync();
            records.Add(new CuratedOfferRecord(
                GenerateHash(resolvedUrl, productTitle),
                productTitle,
                resolvedUrl,
                DateTime.UtcNow
            ));

            var purgeCutoff = DateTime.UtcNow.AddDays(-30);
            var cleanRecords = records.Where(x => x.CreatedAtUtc >= purgeCutoff).ToList();

            var json = JsonSerializer.Serialize(cleanRecords, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearHistoryAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
            _logger.LogInformation("[Historico] Arquivo de histórico limpo com sucesso.");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<CuratedOfferRecord>> LoadRecordsAsync()
    {
        if (!File.Exists(_filePath))
            return new List<CuratedOfferRecord>();

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<List<CuratedOfferRecord>>(json) ?? new List<CuratedOfferRecord>();
        }
        catch
        {
            return new List<CuratedOfferRecord>();
        }
    }

    private static string GenerateHash(string url, string title)
    {
        var input = $"{url.Trim().ToLowerInvariant()}:{title.Trim().ToLowerInvariant()}";
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}