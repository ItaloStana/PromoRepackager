# PromoRepackager ⚡

Microsserviço em .NET 8 projetado para capturar ofertas brutas de grupos de achadinhos no WhatsApp, higienizar o conteúdo removendo dados de terceiros, validar o preço real no e-commerce, renderizar artes para Stories em memória e entregar tudo pronto no Telegram com link de afiliado.

---

## 🎯 O que o pipeline faz na prática

1. **Ingestão**: Escuta mensagens de grupos no WhatsApp via Webhook da Evolution API v2 (ou endpoint local de simulação).
2. **Scraping & Validação**: Segue redirecionamentos, extrai URLs canônicas limpas (Amazon e Mercado Livre), baixa a foto do produto em alta resolução e verifica o preço real na página.
3. **Reconciliação de Preço**: Se a promoção expirou ou o valor subiu no site, o preço em tempo real prevalece automaticamente para não divulgar valores falsos.
4. **Curadoria com IA**: O **Google Gemini 3.1 Flash Lite** (com JSON Schema estrito) limpa telefones, assinaturas de concorrentes e formata copies persuasivas para WhatsApp e Instagram Stories com travas anti-alucinação.
5. **Renderização Gráfica 9:16**: Gera imagens prontas para Stories (1080x1920) direto na memória usando **SkiaSharp**, desenhando cards, sombras, gradientes, tags de preço e marcação para link/sticker respeitando as *safe zones*.
6. **Entrega no Telegram**: Despacha a imagem renderizada e o texto da copy formatado em bloco monoespaçado (`<pre><code>`), permitindo cópia com 1 toque no celular.
7. **Controle de Fila & Deduplicação**: Gerenciado via **Hangfire** com persistência em **SQLite**, bloqueando produtos duplicados nas últimas 48 horas e limitando a cota diária (20 a 25 ofertas).

---

## 🛠️ Stack Tecnológica

* **Runtime & Backend**: C# / .NET 8 (Minimal APIs)
* **Motor Gráfico**: SkiaSharp (`SkiaSharp.NativeAssets.Linux.NoDependencies`)
* **Fila & Jobs em Background**: Hangfire + SQLite (`deals.db`) — *sem necessidade de Redis*
* **Inteligência Artificial**: Google Gemini API (`gemini-3.1-flash-lite`) com saída estruturada
* **Web Scraping & Parsing**: HtmlAgilityPack + HttpClient nativo com headers de crawler social
* **Mensageria**: Telegram.Bot API & Evolution API v2
* **Infraestrutura**: VPS Linux (Coolify/Docker) exposta via Cloudflare Tunnels

---

## 💡 Decisões de Engenharia

* **Zero Headless Browsers**: Renderização 2D matemática direto na memória com SkiaSharp nativo em C++, reduzindo o consumo de RAM de ~800MB (Chromium/Puppeteer) para menos de 60MB por arte gerada.
* **Zero Redis**: Todo o gerenciamento de filas, retentativas e histórico roda em um arquivo SQLite leve com transações ACID.
* **Segurança de Credenciais**: Arquitetura desacoplada de segredos usando o .NET User Secrets em desenvolvimento local e variáveis de ambiente em produção.

---

## ⚙️ Configuração Local

### 1. Clonar o repositório e restaurar dependências

    git clone https://github.com/seu-usuario/PromoRepackager.git
    cd PromoRepackager/PromoRepackager.Api
    dotnet restore

### 2. Configurar Chaves Locais (User Secrets)
Nunca versione credenciais sensíveis no Git. Registre suas chaves no cofre local do .NET:

    dotnet user-secrets init
    dotnet user-secrets set "PromoRepackager:GeminiApiKey" "SUA_API_KEY_DO_GEMINI"
    dotnet user-secrets set "PromoRepackager:TelegramBotToken" "SEU_BOT_TOKEN_TELEGRAM"
    dotnet user-secrets set "PromoRepackager:TelegramAdminChatId" "SEU_CHAT_ID_TELEGRAM"

### 3. Template do appsettings.json
O arquivo versionado atua como contrato estrutural sem credenciais ativas:

    {
      "Logging": {
        "LogLevel": {
          "Default": "Information",
          "Microsoft.AspNetCore": "Warning",
          "Hangfire": "Information"
        }
      },
      "ConnectionStrings": {
        "SQLite": "Data Source=Data/deals.db;Cache=Shared"
      },
      "PromoRepackager": {
        "GeminiApiKey": "SEU_GEMINI_API_KEY_AQUI",
        "TelegramBotToken": "SEU_TELEGRAM_BOT_TOKEN_AQUI",
        "TelegramAdminChatId": "SEU_CHAT_ID_AQUI",
        "AmazonAffiliateTag": "seutag-20",
        "MonitoredGroupJid": "",
        "DailyLimit": 25
      },
      "AllowedHosts": "*"
    }

---

## 🚀 Execução e Testes

Inicie a aplicação:

    dotnet run

### Simulação Manual de Oferta
Teste todo o pipeline (Scraper -> Gemini -> SkiaSharp -> Telegram) via PowerShell sem precisar conectar o WhatsApp:

    $payload = "🔥 MÁSCARA L'ORÉAL ELSEVE R$ 24,90 https://www.mercadolivre.com.br/dp/MLB123456"
    $encoded = [Uri]::EscapeDataString($payload)
    Invoke-RestMethod -Uri "http://localhost:5291/api/test/simulate?text=$encoded" -Method Post

### Monitoramento de Jobs
Acesse o painel do Hangfire para acompanhar o processamento das filas e retentativas:

http://localhost:5291/hangfire

    http://localhost:5291/hangfire
    http://localhost:5291/hangfire
