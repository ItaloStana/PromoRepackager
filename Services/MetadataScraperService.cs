using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using SkiaSharp;

namespace PromoRepackager.Api.Services;

public record ScrapedMetadata(
    string ResolvedUrl,
    string? ImageUrl,
    byte[]? ImageBytes,
    decimal? LivePrice
);

public interface IMetadataScraperService
{
    Task<ScrapedMetadata> ScrapeAsync(string rawText, CancellationToken cancellationToken = default);
}

public partial class MetadataScraperService : IMetadataScraperService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MetadataScraperService> _logger;

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"""price""\s*:\s*""?([0-9]+(?:\.[0-9]{1,2})?)""?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex JsonLdPriceRegex();

    public MetadataScraperService(IHttpClientFactory httpClientFactory, ILogger<MetadataScraperService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Scraper");
        _logger = logger;
    }

    public async Task<ScrapedMetadata> ScrapeAsync(string rawText, CancellationToken cancellationToken = default)
    {
        var match = UrlRegex().Match(rawText);
        if (!match.Success)
            throw new InvalidOperationException("Nenhum link detectado na mensagem.");

        var targetUrl = match.Value;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", "facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "pt-BR,pt;q=0.9,en-US;q=0.8");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? targetUrl;

            if (finalUrl.Contains("account-verification") && finalUrl.Contains("go="))
            {
                var uri = new Uri(finalUrl);
                var query = HttpUtility.ParseQueryString(uri.Query);
                var realUrl = query["go"];
                if (!string.IsNullOrWhiteSpace(realUrl))
                    finalUrl = HttpUtility.UrlDecode(realUrl);
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var ogNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")
                      ?? doc.DocumentNode.SelectSingleNode("//meta[@name='og:image']")
                      ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image:src']")
                      ?? doc.DocumentNode.SelectSingleNode("//meta[@name='twitter:image']");

            var imageUrl = ogNode?.GetAttributeValue("content", null);
            if (!string.IsNullOrWhiteSpace(imageUrl) && imageUrl.StartsWith("//"))
                imageUrl = "https:" + imageUrl;

            byte[]? imageBytes = null;
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                imageBytes = await DownloadAndValidateImageAsync(imageUrl, cancellationToken);
            }

            var livePrice = ExtractPriceFromHtml(doc, html);
            if (livePrice.HasValue)
            {
                _logger.LogInformation("[Scraper] Preço capturado no site: R$ {Preco:F2}", livePrice.Value);
            }
            else
            {
                _logger.LogWarning("[Scraper] Preço não identificado no HTML de {Url}. Usará valor do texto.", finalUrl);
            }

            return new ScrapedMetadata(finalUrl, imageUrl, imageBytes, livePrice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no scraping de {Url}", targetUrl);
            return new ScrapedMetadata(targetUrl, null, null, null);
        }
    }

    private static decimal? ExtractPriceFromHtml(HtmlDocument doc, string rawHtml)
    {
        var priceMeta = doc.DocumentNode.SelectSingleNode("//meta[@itemprop='price']")
                     ?? doc.DocumentNode.SelectSingleNode("//meta[@property='product:price:amount']");

        if (priceMeta != null)
        {
            var content = priceMeta.GetAttributeValue("content", "");
            if (decimal.TryParse(content, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                return price;
        }

        var jsonLdNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (jsonLdNodes != null)
        {
            foreach (var node in jsonLdNodes)
            {
                var json = node.InnerText;
                if (json.Contains("\"price\""))
                {
                    var priceMatch = JsonLdPriceRegex().Match(json);
                    if (priceMatch.Success && decimal.TryParse(priceMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                    {
                        return p;
                    }
                }
            }
        }

        var fractionNode = doc.DocumentNode.SelectSingleNode("//span[contains(@class, 'andes-money-amount__fraction')]");
        if (fractionNode != null)
        {
            var fractionStr = Regex.Replace(fractionNode.InnerText, @"[^\d]", "");
            var centsNode = doc.DocumentNode.SelectSingleNode("//span[contains(@class, 'andes-money-amount__cents')]");
            var centsStr = centsNode != null ? Regex.Replace(centsNode.InnerText, @"[^\d]", "") : "00";

            if (decimal.TryParse($"{fractionStr}.{centsStr}", NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVisualPrice))
            {
                return parsedVisualPrice;
            }
        }

        return null;
    }

    private async Task<byte[]?> DownloadAndValidateImageAsync(string imageUrl, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            using var res = await _httpClient.SendAsync(req, ct);

            if (!res.IsSuccessStatusCode) return null;

            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length < 3072) return null;

            using var codec = SKCodec.Create(new MemoryStream(bytes));
            if (codec == null || codec.Info.Width < 80 || codec.Info.Height < 80)
                return null;

            return bytes;
        }
        catch
        {
            return null;
        }
    }
}