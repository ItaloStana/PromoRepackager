using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using PromoRepackager.Api.Models;

namespace PromoRepackager.Api.Services;

public interface ITelegramNotifierService
{
    Task NotifyAdminAsync(
        OfferCuratedResult curatedOffer,
        byte[] storyImageBytes,
        string affiliateUrl,
        CancellationToken cancellationToken = default
    );
}

public class TelegramNotifierService : ITelegramNotifierService
{
    private readonly TelegramBotClient _botClient;
    private readonly long _adminChatId;
    private readonly ILogger<TelegramNotifierService> _logger;

    public TelegramNotifierService(IConfiguration configuration, ILogger<TelegramNotifierService> logger)
    {
        _logger = logger;
        var token = configuration["PromoRepackager:TelegramBotToken"]
            ?? throw new InvalidOperationException("TelegramBotToken não configurado.");

        var chatIdStr = configuration["PromoRepackager:TelegramAdminChatId"]
            ?? throw new InvalidOperationException("TelegramAdminChatId não configurado.");

        _adminChatId = long.Parse(chatIdStr);
        _botClient = new TelegramBotClient(token);
    }

    public async Task NotifyAdminAsync(
        OfferCuratedResult curatedOffer,
        byte[] storyImageBytes,
        string affiliateUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var imageStream = new MemoryStream(storyImageBytes);
            var photoInput = InputFile.FromStream(imageStream, "story.jpg");

            var caption = $"🎯 <b>{System.Net.WebUtility.HtmlEncode(curatedOffer.Produto.TituloCurto)}</b>\n" +
                          $"💰 {curatedOffer.Produto.Preco} ({curatedOffer.Produto.Condicao})\n" +
                          $"🔗 Link: {affiliateUrl}";

            await _botClient.SendPhotoAsync(
                chatId: _adminChatId,
                photo: photoInput,
                caption: caption,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken
            );

            var completeWhatsAppText = $"{curatedOffer.Whatsapp.CopyPronta}\n\n👉 Compre aqui: {affiliateUrl}";
            var escapedWhatsAppText = System.Net.WebUtility.HtmlEncode(completeWhatsAppText);

            var messageText = $"📋 <b>Copy pronta para WhatsApp (toque para copiar):</b>\n\n" +
                              $"<pre><code>{escapedWhatsAppText}</code></pre>";

            await _botClient.SendTextMessageAsync(
                chatId: _adminChatId,
                text: messageText,
                parseMode: ParseMode.Html,
                disableWebPagePreview: true,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao despachar notificação para o Telegram.");
            throw;
        }
    }
}