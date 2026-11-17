using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

        if (request.IsEnabled &&
            string.IsNullOrWhiteSpace(encryptedKey) &&
            string.IsNullOrWhiteSpace(applicationConfiguration["AiAssistant:ApiKey"]))
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

        var baseUrl = configuration?.BaseUrl ?? applicationConfiguration["AiAssistant:BaseUrl"] ??
            "https://generativelanguage.googleapis.com/v1beta/openai/";
        var model = configuration?.Model ?? applicationConfiguration["AiAssistant:Model"] ?? "gemini-4-hydrogen";
        var encryptedKey = configuration?.EncryptedApiKey;
        var apiKey = applicationConfiguration["AiAssistant:ApiKey"];

        if (!string.IsNullOrWhiteSpace(encryptedKey))
        {
            try
            {
                apiKey = protectionProvider
                    .CreateProtector(ProtectorPurpose)
                    .Unprotect(encryptedKey);
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
            string.IsNullOrWhiteSpace(model) ||
            !TryValidateBaseUrl(baseUrl, environment.IsDevelopment(), out var baseUri))
        {
            await WriteErrorAsync(
                httpContext,
                StatusCodes.Status503ServiceUnavailable,
                "The AI assistant is not currently configured.",
                cancellationToken);
            return;
        }

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
        providerMessages.AddRange(request.Messages.Select(message => new ProviderMessage(message.Role, message.Content)));

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

        try
        {
            var client = httpClientFactory.CreateClient("ai-assistant");
            using var response = await client.SendAsync(
                providerRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("The AI assistant provider returned HTTP {StatusCode}.", (int)response.StatusCode);
                await WriteErrorAsync(
                    httpContext,
                    StatusCodes.Status502BadGateway,
                    $"The AI assistant provider could not complete the request (HTTP {(int)response.StatusCode}).",
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
                    await WriteAssistantEventAsync(
                        httpContext.Response,
                        new { content = content.GetString() },
                        cancellationToken);
                }
            }
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
        var baseUrl = configuration?.BaseUrl ?? applicationConfiguration["AiAssistant:BaseUrl"] ??
            "https://generativelanguage.googleapis.com/v1beta/openai/";
        var model = configuration?.Model ?? applicationConfiguration["AiAssistant:Model"] ?? "gemini-4-hydrogen";

        return new AssistantConfigurationResponse(
            IsConfigured: !string.IsNullOrWhiteSpace(model) && (hasStoredApiKey || hasEnvironmentApiKey),
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
        string BaseUrl,
        string Model,
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

    private sealed class AssistantEndpointLoggerCategory
    {
    }
}
