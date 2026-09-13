using System.Text.Json;
using Hangfire;
using PromoRepackager.Api.Models;
using PromoRepackager.Api.Services;

namespace PromoRepackager.Api.Jobs;

public class ProcessOfferJob
{
    private readonly IMetadataScraperService _scraperService;
    private readonly IGeminiCuratorService _geminiService;
    private readonly IStoryImageGenerator _imageGenerator;
    private readonly ITelegramNotifierService _notifierService;
    private readonly IOfferRepository _offerRepository;
    private readonly ILogger<ProcessOfferJob> _logger;

    public ProcessOfferJob(
        IMetadataScraperService scraperService,
        IGeminiCuratorService geminiService,
        IStoryImageGenerator imageGenerator,
        ITelegramNotifierService notifierService,
        IOfferRepository offerRepository,
        ILogger<ProcessOfferJob> logger)
    {
        _scraperService = scraperService;
        _geminiService = geminiService;
        _imageGenerator = imageGenerator;
        _notifierService = notifierService;
        _offerRepository = offerRepository;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 5, 15 })]
    public async Task ExecuteAsync(string rawWebhookPayload, CancellationToken cancellationToken, bool isSimulation = false)
    {
        var payload = JsonSerializer.Deserialize<EvolutionWebhookDto>(rawWebhookPayload);
        if (payload?.Data?.Message == null)
        {
            _logger.LogWarning("Abortado: payload sem nó 'data.message'.");
            return;
        }

        if (payload.Data.Key?.FromMe == true)
        {
            _logger.LogInformation("Ignorado: mensagem enviada pelo próprio número monitorado.");
            return;
        }

        var messageText = payload.Data.Message.ExtractText();
        if (string.IsNullOrWhiteSpace(messageText))
        {
            _logger.LogWarning("Abortado: mensagem recebida sem texto legível.");
            return;
        }

        _logger.LogInformation("[1/4] Extraindo metadados da oferta...");
        var metadata = await _scraperService.ScrapeAsync(messageText, cancellationToken);

        if (metadata.LivePrice.HasValue)
        {
            _logger.LogInformation("[Scraper] Preço verificado diretamente na página: R$ {Preco:F2}", metadata.LivePrice.Value);
        }

        _logger.LogInformation("[2/4] Curadoria via Gemini 3.1 Flash Lite...");
        var curatedOffer = await _geminiService.CurateOfferAsync(messageText, metadata.LivePrice, cancellationToken);
        if (curatedOffer == null)
            throw new InvalidOperationException("Gemini retornou payload nulo ou formato JSON inválido.");

        var canProcess = await _offerRepository.CanProcessAsync(
            metadata.ResolvedUrl,
            curatedOffer.Produto.TituloCurto,
            bypassChecks: isSimulation
        );

        if (!canProcess)
        {
            _logger.LogWarning("Oferta descartada pelos filtros de negócio (duplicada nas últimas 48h ou cota diária esgotada).");
            return;
        }

        var finalProductImageBytes = metadata.ImageBytes ?? payload.Data.Message.ExtractThumbnailBytes();

        _logger.LogInformation("[3/4] Renderizando Story 1080x1920 com SkiaSharp...");
        var storyBytes = _imageGenerator.GenerateStoryImage(finalProductImageBytes, curatedOffer);

        _logger.LogInformation("[4/4] Despachando card e copy para o Telegram...");
        await _notifierService.NotifyAdminAsync(
            curatedOffer,
            storyBytes,
            metadata.ResolvedUrl,
            cancellationToken
        );

        if (!isSimulation)
        {
            await _offerRepository.RegisterAsync(metadata.ResolvedUrl, curatedOffer.Produto.TituloCurto);
        }

        _logger.LogInformation("Sucesso! Oferta entregue: {Produto} por {Preco}",
            curatedOffer.Produto.TituloCurto,
            curatedOffer.Produto.Preco);
    }
}