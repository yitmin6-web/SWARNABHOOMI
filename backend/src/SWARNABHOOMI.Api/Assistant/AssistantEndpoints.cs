using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Api.Assistant;

public static class AssistantEndpoints
{
    private const string ProtectorPurpose = "SWARNABHOOMI.AiAssistant.ApiKey.v1";
    private const int MaximumMessages = 16;
    private const int MaximumMessageCharacters = 3000;
    private const int MaximumConversationCharacters = 12000;

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

    private static async Task<IResult> GetConfiguration(SWARNABHOOMIContext db)
    {
        var configuration = await db.AiAssistantConfigurations
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync();

        return Results.Ok(new AssistantConfigurationResponse(
            configuration is { IsEnabled: true, EncryptedApiKey.Length: > 0 },
            configuration?.BaseUrl ?? "https://api.openai.com/v1",
            configuration?.Model ?? "gpt-4o-mini",
            configuration is { EncryptedApiKey.Length: > 0 },
            configuration?.IsEnabled ?? false));
    }

    private static async Task<IResult> UpdateConfiguration(
        AssistantConfigurationRequest request,
        ClaimsPrincipal principal,
        SWARNABHOOMIContext db,
        IDataProtectionProvider protectionProvider,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUrl) ||
            request.BaseUrl.Length > 500 ||
            !TryValidateBaseUrl(request.BaseUrl, environment.IsDevelopment(), out var baseUri) ||
            string.IsNullOrWhiteSpace(request.Model) ||
            request.Model.Trim().Length > 200 ||
            (request.ApiKey?.Length ?? 0) > 4000)
        {
            return Results.BadRequest(new
            {
                error = "Provide a valid HTTPS OpenAI-compatible base URL, a model name, and an API key no longer than 4000 characters."
            });
        }

        var configuration = await db.AiAssistantConfigurations
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var encryptedKey = configuration?.EncryptedApiKey;
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            encryptedKey = protectionProvider
                .CreateProtector(ProtectorPurpose)
                .Protect(request.ApiKey.Trim());
        }

        if (request.IsEnabled && string.IsNullOrWhiteSpace(encryptedKey))
        {
            return Results.BadRequest(new { error = "Enter an API key before enabling the assistant." });
        }

        if (configuration is null)
        {
            configuration = new AiAssistantConfiguration();
            db.AiAssistantConfigurations.Add(configuration);
        }

        configuration.BaseUrl = baseUri.ToString().TrimEnd('/');
        configuration.Model = request.Model.Trim();
        configuration.EncryptedApiKey = encryptedKey ?? string.Empty;
        configuration.IsEnabled = request.IsEnabled;
        configuration.UpdatedAt = DateTime.UtcNow;
        configuration.UpdatedBy = principal.FindFirstValue(ClaimTypes.NameIdentifier);
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

        return Results.Ok(new AssistantConfigurationResponse(
            configuration.IsEnabled && configuration.EncryptedApiKey.Length > 0,
            configuration.BaseUrl,
            configuration.Model,
            configuration.EncryptedApiKey.Length > 0,
            configuration.IsEnabled));
    }

    private static async Task<IResult> Chat(
        AssistantChatRequest request,
        ClaimsPrincipal principal,
        SWARNABHOOMIContext db,
        IDataProtectionProvider protectionProvider,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var messages = request.Messages;
        if (messages is null ||
            messages.Count is 0 or > MaximumMessages ||
            messages[^1].Role != "user" ||
            messages.Any(message =>
                message.Role is not ("user" or "assistant") ||
                string.IsNullOrWhiteSpace(message.Content) ||
                message.Content.Length > MaximumMessageCharacters) ||
            messages.Sum(message => (long)message.Content.Length) > MaximumConversationCharacters)
        {
            return Results.BadRequest(new
            {
                error = $"Send 1 to {MaximumMessages} user/assistant messages; each message is limited to {MaximumMessageCharacters} characters."
            });
        }

        var configuration = await db.AiAssistantConfigurations
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (configuration is not { IsEnabled: true } || string.IsNullOrWhiteSpace(configuration.EncryptedApiKey))
        {
            return Results.Problem(
                title: "AI assistant is not configured.",
                detail: "Ask a Super Administrator to configure the assistant.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        string apiKey;
        try
        {
            apiKey = protectionProvider
                .CreateProtector(ProtectorPurpose)
                .Unprotect(configuration.EncryptedApiKey);
        }
        catch (System.Security.Cryptography.CryptographicException exception)
        {
            loggerFactory.CreateLogger("Assistant").LogError(
                exception,
                "The stored assistant key cannot be unprotected.");
            return Results.Problem(
                title: "AI assistant configuration is unavailable.",
                detail: "A Super Administrator must configure the assistant again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var endpoint = new Uri($"{configuration.BaseUrl.TrimEnd('/')}/chat/completions");
        var providerMessages = new List<ProviderMessage>
        {
            new(
                "system",
                "You are the SWARNABHOOMI rice production information assistant. Be clear and concise. " +
                "Do not invent local agricultural rates, yields, official policies, or market guarantees. " +
                "If a question requires authorized local guidance that is not provided in the conversation, " +
                "say that it is not configured and recommend consulting an Agrarian Officer. " +
                "Do not ask users to share passwords, API keys, or unnecessary personal information.")
        };
        providerMessages.AddRange(messages.Select(message => new ProviderMessage(message.Role, message.Content)));

        using var providerRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                model = configuration.Model,
                messages = providerMessages,
                max_tokens = 800,
                temperature = 0.2
            })
        };
        providerRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            var client = httpClientFactory.CreateClient("ai-assistant");
            using var providerResponse = await client.SendAsync(
                providerRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!providerResponse.IsSuccessStatusCode)
            {
                loggerFactory.CreateLogger("Assistant").LogWarning(
                    "AI provider returned HTTP {StatusCode} for user {UserId}.",
                    (int)providerResponse.StatusCode,
                    principal.FindFirstValue(ClaimTypes.NameIdentifier));
                return Results.Problem(
                    title: "The AI provider could not complete the request.",
                    detail: "Check the assistant configuration or try again later.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            await using var responseStream = await providerResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                responseStream,
                new JsonDocumentOptions { MaxDepth = 32 },
                cancellationToken);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0 ||
                !choices[0].TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(content.GetString()))
            {
                loggerFactory.CreateLogger("Assistant").LogWarning(
                    "AI provider returned an unsupported response shape for user {UserId}.",
                    principal.FindFirstValue(ClaimTypes.NameIdentifier));
                return Results.Problem(
                    title: "The AI provider returned an unsupported response.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.Ok(new AssistantChatResponse(content.GetString()!));
        }
        catch (HttpRequestException exception)
        {
            loggerFactory.CreateLogger("Assistant").LogWarning(
                exception,
                "AI provider request failed for user {UserId}.",
                principal.FindFirstValue(ClaimTypes.NameIdentifier));
            return Results.Problem(
                title: "The AI provider is unreachable.",
                detail: "Check the assistant endpoint and try again.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem(
                title: "The AI provider timed out.",
                detail: "Try again later.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (JsonException exception)
        {
            loggerFactory.CreateLogger("Assistant").LogWarning(
                exception,
                "AI provider response could not be parsed for user {UserId}.",
                principal.FindFirstValue(ClaimTypes.NameIdentifier));
            return Results.Problem(
                title: "The AI provider returned an invalid response.",
                statusCode: StatusCodes.Status502BadGateway);
        }
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
}

public sealed record AssistantConfigurationRequest(
    string BaseUrl,
    string Model,
    string? ApiKey,
    bool IsEnabled);

public sealed record AssistantConfigurationResponse(
    bool IsConfigured,
    string BaseUrl,
    string Model,
    bool HasApiKey,
    bool IsEnabled);

public sealed record AssistantChatMessage(string Role, string Content);

public sealed record AssistantChatRequest(IReadOnlyList<AssistantChatMessage>? Messages);

public sealed record AssistantChatResponse(string Reply);

public sealed record ProviderMessage(string Role, string Content);
