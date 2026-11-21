using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Api.Assistant;

public static class AssistantEndpoints
{
    private const string ProtectorPurpose = "SWARNABHOOMI.AiAssistant.ApiKey.v1";
    private const int MaximumMessages = 16;
    private const int MaximumMessageCharacters = 3000;
    private const int MaximumConversationCharacters = 12000;
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private static readonly TimeSpan PerModelTimeout = TimeSpan.FromSeconds(20);

    private const string AssistantSystemPrompt =
        "You are the SWARNABHOOMI rice production information assistant. Be clear and concise. " +
        "Do not invent local agricultural rates, yields, official policies, or market guarantees. " +
        "If a question requires authorized local guidance that is not provided in the conversation, " +
        "say that it is not configured and recommend consulting an Agrarian Officer. " +
        "Do not ask users to share passwords, API keys, or unnecessary personal information.";

    private static readonly string[] GeminiRestrictedModelMarkers =
    [
        "gemini-2.5", "gemini-2.0", "gemini-1.5",
        "image", "imagen", "veo", "audio", "tts", "live", "transcribe",
        "embedding", "aqa", "deep-research", "robotics", "computer-use"
    ];

    private static readonly string[] GeminiModelPriority =
    [
        "gemini-flash-lite-latest",
        "gemini-flash-latest",
        "gemini-3.8-flash",
        "gemini-3.5-flash-lite",
        "gemini-3.1-flash-lite",
        "gemini-3-flash-preview",
        "gemini-pro-latest",
        "gemma-4-26b-a4b-it",
        "gemma-4-31b-it"
    ];

    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/assistant/chat", Chat)
            .RequireAuthorization()
            .RequireRateLimiting("ai-assistant");

        var configuration = endpoints.MapGroup("/api/admin/assistant/config")
            .RequireAuthorization("SuperAdminOnly");
        configuration.MapGet("/", GetConfiguration);
        configuration.MapPut("/", UpdateConfiguration);

        return endpoints;
    }

    private static async Task<IResult> GetConfiguration(
        SWARNABHOOMIContext db,
        IConfiguration applicationConfiguration)
    {
        var configuration = await db.AiAssistantConfigurations
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync();

        return Results.Ok(CreateConfigurationResponse(configuration, applicationConfiguration));
    }

    private static async Task<IResult> UpdateConfiguration(
        AssistantConfigurationRequest request,
        ClaimsPrincipal principal,
        SWARNABHOOMIContext db,
        IDataProtectionProvider protectionProvider,
        IConfiguration applicationConfiguration,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var configuration = await db.AiAssistantConfigurations
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var configuredBaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl)
            ? GeminiBaseUrl
            : request.BaseUrl.Trim();

        var configuredModel = string.IsNullOrWhiteSpace(request.Model)
            ? string.Empty
            : request.Model.Trim();

        if (configuredBaseUrl.Length > 500 ||
            !TryValidateBaseUrl(configuredBaseUrl, environment.IsDevelopment(), out var baseUri) ||
            configuredModel.Length > 200 ||
            (request.ApiKey?.Length ?? 0) > 4000)
        {
            return Results.BadRequest(new
            {
                error = "Provide a valid HTTPS base URL and an API key no longer than 4000 characters."
            });
        }

        var normalizedApiKey = NormalizeApiKey(request.ApiKey);
        var encryptedKey = configuration?.EncryptedApiKey;
        if (!string.IsNullOrWhiteSpace(normalizedApiKey))
        {
            encryptedKey = protectionProvider
                .CreateProtector(ProtectorPurpose)
                .Protect(normalizedApiKey);
        }

        if (request.IsEnabled &&
            string.IsNullOrWhiteSpace(encryptedKey) &&
            string.IsNullOrWhiteSpace(NormalizeApiKey(applicationConfiguration["AiAssistant:ApiKey"])))
        {
            return Results.BadRequest(new { error = "Enter an API key before enabling the assistant." });
        }

        if (configuration is null)
        {
            configuration = new AiAssistantConfiguration();
            db.AiAssistantConfigurations.Add(configuration);
        }

        configuration.BaseUrl = baseUri.ToString().TrimEnd('/');
        configuration.Model = configuredModel;
        configuration.EncryptedApiKey = encryptedKey ?? string.Empty;
        configuration.IsEnabled = request.IsEnabled;
        configuration.UpdatedAt = DateTime.UtcNow;
        configuration.UpdatedBy = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        configuration.CreatedBy ??= principal.FindFirstValue(ClaimTypes.NameIdentifier);

        db.AuditLogs.Add(new AuditLog
        {
            Actor = configuration.UpdatedBy,
            Action = "AssistantConfigurationUpdated",
            EntityType = nameof(AiAssistantConfiguration),
            EntityId = configuration.Id.ToString(),
            Result = "Success",
            SafeMetadata = JsonSerializer.Serialize(new
            {
                Host = baseUri.Host,
                configuration.Model,
                configuration.IsEnabled
            })
        });

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(CreateConfigurationResponse(configuration, applicationConfiguration));
    }

    private static async Task Chat(
        AssistantChatRequest request,
        HttpContext httpContext,
        SWARNABHOOMIContext db,
        IDataProtectionProvider protectionProvider,
        IConfiguration applicationConfiguration,
        IWebHostEnvironment environment,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        ILogger<AssistantEndpointLoggerCategory> logger,
        CancellationToken cancellationToken)
    {
        if (request.Messages is null ||
            request.Messages.Count is 0 or > MaximumMessages ||
            request.Messages.Any(message =>
                message is null ||
                string.IsNullOrWhiteSpace(message.Role) ||
                string.IsNullOrWhiteSpace(message.Content) ||
                message.Content.Length > MaximumMessageCharacters ||
                (message.Role != "user" && message.Role != "assistant")) ||
            request.Messages.Sum(message => message.Content.Length) > MaximumConversationCharacters ||
            request.Messages[^1].Role != "user")
        {
            await WriteErrorAsync(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Provide up to 16 user/assistant messages, ending with a user message; each message is limited to 3,000 characters.",
                cancellationToken);
            return;
        }

        var configuration = await db.AiAssistantConfigurations
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var baseUrl = configuration?.BaseUrl ?? applicationConfiguration["AiAssistant:BaseUrl"] ?? GeminiBaseUrl;
        var model = configuration?.Model ?? applicationConfiguration["AiAssistant:Model"] ?? string.Empty;
        var encryptedKey = configuration?.EncryptedApiKey;
        var apiKey = NormalizeApiKey(applicationConfiguration["AiAssistant:ApiKey"]);

        if (!string.IsNullOrWhiteSpace(encryptedKey))
        {
            try
            {
                apiKey = NormalizeApiKey(
                    protectionProvider
                        .CreateProtector(ProtectorPurpose)
                        .Unprotect(encryptedKey));
            }
            catch (CryptographicException exception)
            {
                logger.LogError(exception, "The configured AI assistant API key could not be decrypted.");
                await WriteErrorAsync(
                    httpContext,
                    StatusCodes.Status500InternalServerError,
                    "The AI assistant configuration is unavailable.",
                    cancellationToken);
                return;
            }
        }

        if (configuration is { IsEnabled: false } ||
            string.IsNullOrWhiteSpace(apiKey) ||
            !TryValidateBaseUrl(baseUrl, environment.IsDevelopment(), out var baseUri))
        {
            await WriteErrorAsync(
                httpContext,
                StatusCodes.Status503ServiceUnavailable,
                "The AI assistant is not currently configured.",
                cancellationToken);
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient("ai-assistant");
            if (IsGoogleAiStudio(baseUri))
            {
                await StreamGeminiResponseAsync(
                    request.Messages,
                    model,
                    apiKey,
                    client,
                    memoryCache,
                    httpContext,
                    logger,
                    cancellationToken);
                return;
            }

            await StreamOpenAiResponseAsync(
                request.Messages,
                model,
                baseUri,
                apiKey,
                client,
                httpContext,
                logger,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "The AI assistant provider request failed.");
            await WriteProviderErrorAsync(
                httpContext,
                "The AI assistant provider is temporarily unavailable.",
                cancellationToken);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The AI assistant provider request timed out.");
            await WriteProviderErrorAsync(
                httpContext,
                "The AI assistant provider timed out.",
                cancellationToken);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "The AI assistant provider returned invalid JSON.");
            await WriteProviderErrorAsync(
                httpContext,
                "The AI assistant provider returned an invalid response.",
                cancellationToken);
        }
    }

    private static async Task StreamGeminiResponseAsync(
        IReadOnlyList<AssistantChatMessage> messages,
        string configuredModel,
        string apiKey,
        HttpClient client,
        IMemoryCache memoryCache,
        HttpContext httpContext,
        ILogger<AssistantEndpointLoggerCategory> logger,
        CancellationToken cancellationToken)
    {
        var modelsResponse = await DiscoverGeminiModelsAsync(client, memoryCache, apiKey, cancellationToken);
        if (!modelsResponse.IsSuccess)
        {
            var statusCode = modelsResponse.StatusCode is HttpStatusCode.BadRequest or
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? (int)modelsResponse.StatusCode
                : StatusCodes.Status502BadGateway;
            await WriteErrorAsync(httpContext, statusCode, modelsResponse.Error!, cancellationToken);
            return;
        }

        var models = modelsResponse.Models!;
        var usableConfiguredModel = IsUsableGeminiModel(configuredModel) ? configuredModel.Trim() : null;
        var candidates = string.IsNullOrWhiteSpace(usableConfiguredModel)
            ? models
            : models.Where(model => model.Equals(usableConfiguredModel, StringComparison.OrdinalIgnoreCase))
                .Concat(models.Where(model => !model.Equals(usableConfiguredModel, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        string? lastError = null;
        var quotaFailures = 0;
        var maxAttempts = Math.Min(6, candidates.Count);

        for (var index = 0; index < maxAttempts; index++)
        {
            var model = candidates[index];
            var payload = BuildGeminiPayload(model, messages);

            using var providerRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{GeminiBaseUrl}/models/{Uri.EscapeDataString(model)}:generateContent")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            providerRequest.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(PerModelTimeout);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(
                    providerRequest,
                    HttpCompletionOption.ResponseContentRead,
                    attemptCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Gemini model {Model} timed out after {Seconds}s; trying next candidate.", model, PerModelTimeout.TotalSeconds);
                lastError = $"Model '{model}' timed out.";
                continue;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    lastError = await ReadProviderErrorAsync(response, attemptCts.Token);

                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        await WriteErrorAsync(
                            httpContext,
                            (int)response.StatusCode,
                            "The Gemini API key is invalid, revoked, or lacks permission for the Gemini API.",
                            cancellationToken);
                        return;
                    }

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        logger.LogWarning("Gemini model {Model} hit quota/rate limit (429); trying next candidate.", model);
                        quotaFailures++;
                        continue;
                    }

                    if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
                    {
                        logger.LogWarning("Gemini model {Model} returned HTTP {StatusCode} ({Detail}); trying next candidate.", model, (int)response.StatusCode, lastError);
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        logger.LogWarning("Gemini model {Model} is overloaded (503); trying next candidate.", model);
                        await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
                        continue;
                    }

                    logger.LogError("The Gemini provider returned HTTP {StatusCode}: {Detail}", (int)response.StatusCode, lastError);
                    continue;
                }

                string responseBody;
                try
                {
                    responseBody = await response.Content.ReadAsStringAsync(attemptCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Reading response from Gemini model {Model} timed out; trying next candidate.", model);
                    lastError = $"Model '{model}' timed out while responding.";
                    continue;
                }

                using var document = JsonDocument.Parse(responseBody);
                var reply = ExtractGeminiText(document);
                if (string.IsNullOrWhiteSpace(reply))
                {
                    logger.LogWarning("Gemini model {Model} returned an empty response; trying next candidate.", model);
                    lastError = $"Model '{model}' returned an empty response.";
                    continue;
                }

                logger.LogInformation("AI assistant answered using Gemini model {Model}.", model);
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = "text/event-stream";
                httpContext.Response.Headers.CacheControl = "no-cache";
                await WriteAssistantEventAsync(httpContext.Response, new { content = reply }, cancellationToken);
                return;
            }
        }

        await WriteErrorAsync(
            httpContext,
            StatusCodes.Status502BadGateway,
            quotaFailures == maxAttempts
                ? "The Google AI Studio project quota or rate limit was reached across available models. Please try again later or use another API key."
                : $"The Gemini provider could not complete the request after trying {maxAttempts} models: {lastError ?? "No available model responded."}",
            cancellationToken);
    }

    private static string BuildGeminiPayload(string model, IReadOnlyList<AssistantChatMessage> messages)
    {
        if (model.StartsWith("gemma", StringComparison.OrdinalIgnoreCase))
        {
            var gemmaContents = messages.Select((message, index) => new
            {
                role = message.Role == "assistant" ? "model" : "user",
                parts = new[]
                {
                    new
                    {
                        text = index == 0
                            ? $"{AssistantSystemPrompt}\n\nUser message:\n{message.Content}"
                            : message.Content
                    }
                }
            });

            return JsonSerializer.Serialize(new
            {
                contents = gemmaContents,
                generationConfig = new { maxOutputTokens = 2048, temperature = 0.2 }
            });
        }

        return JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = AssistantSystemPrompt } } },
            contents = messages.Select(message => new
            {
                role = message.Role == "assistant" ? "model" : "user",
                parts = new[] { new { text = message.Content } }
            }),
            generationConfig = new { maxOutputTokens = 2048, temperature = 0.2 }
        });
    }

    private static string? ExtractGeminiText(JsonDocument document)
    {
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0 ||
            !candidates[0].TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var visibleText = string.Concat(parts.EnumerateArray()
            .Where(part =>
                !(part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) &&
                part.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            .Select(part => part.GetProperty("text").GetString()))
            .Trim();

        if (!string.IsNullOrWhiteSpace(visibleText))
        {
            return visibleText;
        }

        return string.Concat(parts.EnumerateArray()
            .Where(part => part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            .Select(part => part.GetProperty("text").GetString()))
            .Trim();
    }

    private static async Task StreamOpenAiResponseAsync(
        IReadOnlyList<AssistantChatMessage> messages,
        string model,
        Uri baseUri,
        string apiKey,
        HttpClient client,
        HttpContext httpContext,
        ILogger<AssistantEndpointLoggerCategory> logger,
        CancellationToken cancellationToken)
    {
        var providerMessages = new List<ProviderMessage>
        {
            new("system", AssistantSystemPrompt)
        };
        providerMessages.AddRange(messages.Select(message => new ProviderMessage(message.Role, message.Content)));

        using var providerRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUri.AbsoluteUri.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model,
                    messages = providerMessages,
                    stream = true,
                    max_tokens = 800,
                    temperature = 0.2
                }),
                Encoding.UTF8,
                "application/json")
        };
        providerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await client.SendAsync(
            providerRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadProviderErrorAsync(response, cancellationToken);
            logger.LogError("The AI assistant provider returned HTTP {StatusCode}: {Detail}", (int)response.StatusCode, detail);
            await WriteErrorAsync(
                httpContext,
                StatusCodes.Status502BadGateway,
                $"The AI assistant provider could not complete the request (HTTP {(int)response.StatusCode}): {detail}",
                cancellationToken);
            return;
        }

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Append("X-Accel-Buffering", "no");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[5..].Trim();
            if (payload == "[DONE]")
            {
                break;
            }

            using var chunk = JsonDocument.Parse(payload);
            if (chunk.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(content.GetString()))
            {
                await WriteAssistantEventAsync(httpContext.Response, new { content = content.GetString() }, cancellationToken);
            }
        }
    }

    private static async Task<GeminiModelsResponse> DiscoverGeminiModelsAsync(
        HttpClient client,
        IMemoryCache memoryCache,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"gemini-models:v2:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)))}";
        if (memoryCache.TryGetValue(cacheKey, out List<string>? cachedModels) && cachedModels is { Count: > 0 })
        {
            return new GeminiModelsResponse(true, cachedModels, null, null);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{GeminiBaseUrl}/models");
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

        using var discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryCts.CancelAfter(TimeSpan.FromSeconds(15));

        using var response = await client.SendAsync(request, discoveryCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var error = response.StatusCode is HttpStatusCode.BadRequest or
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? "The Gemini API key is invalid, revoked, or lacks permission for the Gemini API."
                : await ReadProviderErrorAsync(response, discoveryCts.Token);
            return new GeminiModelsResponse(false, null, error, response.StatusCode);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(discoveryCts.Token));
        if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return new GeminiModelsResponse(false, null, "The Gemini models response did not contain a model list.", null);
        }

        var discovered = models.EnumerateArray()
            .Where(model => model.TryGetProperty("name", out var _) &&
                model.TryGetProperty("supportedGenerationMethods", out var methods) &&
                methods.ValueKind == JsonValueKind.Array &&
                methods.EnumerateArray().Any(method => method.GetString() == "generateContent"))
            .Select(model => model.GetProperty("name").GetString()!.Replace("models/", string.Empty, StringComparison.Ordinal))
            .ToList();

        var standardTextModels = discovered
            .Where(IsUsableGeminiModel)
            .OrderBy(GetGeminiModelPriority)
            .ThenBy(model => model)
            .ToList();

        if (standardTextModels.Count > 0)
        {
            discovered = standardTextModels;
        }

        if (discovered.Count == 0)
        {
            return new GeminiModelsResponse(false, null, "The Gemini API key has no available models that support generateContent.", null);
        }

        memoryCache.Set(cacheKey, discovered, TimeSpan.FromMinutes(10));
        return new GeminiModelsResponse(true, discovered, null, null);
    }

    private static int GetGeminiModelPriority(string model)
    {
        var exactIndex = Array.FindIndex(GeminiModelPriority, priority =>
            model.Equals(priority, StringComparison.OrdinalIgnoreCase));
        if (exactIndex >= 0)
        {
            return exactIndex;
        }

        var partialIndex = Array.FindIndex(GeminiModelPriority, priority =>
            model.Contains(priority, StringComparison.OrdinalIgnoreCase));
        return partialIndex < 0 ? GeminiModelPriority.Length * 2 : GeminiModelPriority.Length + partialIndex;
    }

    private static bool IsUsableGeminiModel(string model) =>
        !string.IsNullOrWhiteSpace(model) &&
        !GeminiRestrictedModelMarkers.Any(marker =>
            model.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static async Task<string> ReadProviderErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return "No provider error detail was returned.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? error.ToString();
                }

                return error.ToString();
            }
        }
        catch (JsonException)
        {
        }

        return body.Length > 1000 ? body[..1000] : body;
    }

    private static bool IsGoogleAiStudio(Uri baseUri) =>
        baseUri.Host.Equals("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey;
        }

        return apiKey.Trim().Trim('"', '\'').Trim();
    }

    private static async Task WriteErrorAsync(
        HttpContext httpContext,
        int statusCode,
        string detail,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new { detail }, cancellationToken);
    }

    private static async Task WriteProviderErrorAsync(
        HttpContext httpContext,
        string detail,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            await WriteAssistantEventAsync(httpContext.Response, new { error = detail }, cancellationToken);
            return;
        }

        await WriteErrorAsync(httpContext, StatusCodes.Status502BadGateway, detail, cancellationToken);
    }

    private static async Task WriteAssistantEventAsync(
        HttpResponse response,
        object data,
        CancellationToken cancellationToken)
    {
        await response.WriteAsync($"data: {JsonSerializer.Serialize(data)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static AssistantConfigurationResponse CreateConfigurationResponse(
        AiAssistantConfiguration? configuration,
        IConfiguration applicationConfiguration)
    {
        var hasStoredApiKey = !string.IsNullOrWhiteSpace(configuration?.EncryptedApiKey);
        var hasEnvironmentApiKey = !string.IsNullOrWhiteSpace(applicationConfiguration["AiAssistant:ApiKey"]);
        var baseUrl = configuration?.BaseUrl ?? applicationConfiguration["AiAssistant:BaseUrl"] ?? GeminiBaseUrl;
        var model = configuration?.Model ?? applicationConfiguration["AiAssistant:Model"] ?? string.Empty;

        return new AssistantConfigurationResponse(
            IsConfigured: hasStoredApiKey || hasEnvironmentApiKey,
            BaseUrl: baseUrl,
            Model: model,
            HasApiKey: hasStoredApiKey || hasEnvironmentApiKey,
            IsEnabled: configuration?.IsEnabled ?? hasEnvironmentApiKey);
    }

    private static bool TryValidateBaseUrl(string value, bool isDevelopment, out Uri baseUri)
    {
        baseUri = null!;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.UserInfo.Length != 0 ||
            parsed.Query.Length != 0 ||
            parsed.Fragment.Length != 0)
        {
            return false;
        }

        var localDevelopmentHost = isDevelopment &&
            (parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             parsed.Host.Equals("127.0.0.1", StringComparison.Ordinal));
        var hostname = parsed.Host;
        if ((parsed.Scheme != Uri.UriSchemeHttps && !(localDevelopmentHost && parsed.Scheme == Uri.UriSchemeHttp)) ||
            (!localDevelopmentHost &&
                (parsed.HostNameType != UriHostNameType.Dns ||
                 hostname.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                 hostname.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
                 hostname.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
                 hostname.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        baseUri = new Uri(parsed.GetLeftPart(UriPartial.Path).TrimEnd('/'), UriKind.Absolute);
        return true;
    }

    public sealed record AssistantConfigurationRequest(
        string? BaseUrl,
        string? Model,
        string? ApiKey,
        bool IsEnabled);

    public sealed record AssistantChatRequest(IReadOnlyList<AssistantChatMessage>? Messages);

    public sealed record AssistantChatMessage(string Role, string Content);

    private sealed record AssistantConfigurationResponse(
        bool IsConfigured,
        string BaseUrl,
        string Model,
        bool HasApiKey,
        bool IsEnabled);

    private sealed record ProviderMessage(string Role, string Content);

    private sealed record GeminiModelsResponse(
        bool IsSuccess,
        List<string>? Models,
        string? Error,
        HttpStatusCode? StatusCode);

    private sealed class AssistantEndpointLoggerCategory
    {
    }
}