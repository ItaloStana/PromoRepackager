using System.Net.Http.Json;
using System.Text.Json;
using PromoRepackager.Api.Models;

namespace PromoRepackager.Api.Services;

public interface IGeminiCuratorService
{
    Task<OfferCuratedResult?> CurateOfferAsync(string rawText, decimal? livePrice = null, CancellationToken cancellationToken = default);
}

public class GeminiCuratorService : IGeminiCuratorService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    private const string SystemInstruction = """
        Você é um assistente especializado em curadoria de ofertas de e-commerce e copywriting para grupos de achadinhos (focado em Amazon e Mercado Livre).
        Sua função é receber textos brutos de promoções capturados de grupos de terceiros, higienizar o conteúdo, extrair dados essenciais e reescrever as publicações para WhatsApp e Instagram Stories.

        Regras de Processamento e Higienização:
        1. Remoção Obrigatória: Exclua qualquer menção a nomes de grupos, canais, administradores, telefones, arrobas (@) ou assinaturas de terceiros. Ignore links originais.
        2. Reconciliação de Preço (REGRA CRÍTICA):
           - Se for informado um [PREÇO_VERIFICADO_NO_SITE], compare-o com o preço anunciado no texto bruto da mensagem.
           - Se forem iguais: mantenha esse preço normalmente.
           - Se forem diferentes: adote OBRIGATORIAMENTE o [PREÇO_VERIFICADO_NO_SITE] em todos os campos (produto.preco, whatsapp.copy_pronta e instagram_stories.texto_pill_preco). Isso ocorre porque a promoção original pode ter expirado ou subido de valor no e-commerce.
        3. Copywriting: Tom direto, dinâmico e atrativo, sem soar robótico. Destaque o benefício principal. Formatação escaneável com emojis estratégicos (🔥, ⚡, 🎟️, 💥).
        """;

    public GeminiCuratorService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient("GeminiClient");
        _apiKey = configuration["PromoRepackager:GeminiApiKey"]
            ?? throw new InvalidOperationException("PromoRepackager:GeminiApiKey não configurada.");
    }

    public async Task<OfferCuratedResult?> CurateOfferAsync(string rawText, decimal? livePrice = null, CancellationToken cancellationToken = default)
    {
        var userPrompt = rawText;
        if (livePrice.HasValue && livePrice.Value > 0)
        {
            userPrompt = $"[PREÇO_VERIFICADO_NO_SITE: R$ {livePrice.Value:F2}]\n\n{rawText}";
        }

        var requestPayload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = SystemInstruction } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                response_mime_type = "application/json",
                response_schema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        produto = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                titulo_curto = new { type = "STRING" },
                                preco = new { type = "STRING" },
                                condicao = new { type = "STRING" },
                                cupom = new { type = "STRING", nullable = true },
                                destaque = new { type = "STRING" }
                            },
                            required = new[] { "titulo_curto", "preco", "condicao", "destaque" }
                        },
                        whatsapp = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                copy_pronta = new { type = "STRING" }
                            },
                            required = new[] { "copy_pronta" }
                        },
                        instagram_stories = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                texto_pill_preco = new { type = "STRING" },
                                texto_pill_cupom = new { type = "STRING", nullable = true },
                                sticker_link_label = new { type = "STRING" }
                            },
                            required = new[] { "texto_pill_preco", "sticker_link_label" }
                        }
                    },
                    required = new[] { "produto", "whatsapp", "instagram_stories" }
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "v1beta/models/gemini-3.1-flash-lite:generateContent"
        );
        httpRequest.Headers.Add("x-goog-api-key", _apiKey);
        httpRequest.Content = JsonContent.Create(requestPayload);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Gemini API retornou erro HTTP {response.StatusCode}: {responseBody}");
        }

        using var jsonDoc = JsonDocument.Parse(responseBody);
        var candidateText = jsonDoc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(candidateText))
            return null;

        return JsonSerializer.Deserialize<OfferCuratedResult>(candidateText);
    }
}