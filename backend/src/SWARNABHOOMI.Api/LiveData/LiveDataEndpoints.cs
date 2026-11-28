using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Api.LiveData;

public static class LiveDataEndpoints
{
    private const string OpenMeteoSource = "https://open-meteo.com/en/docs";
    private const string OpenMeteoAttribution = "Weather data by Open-Meteo.com";
    private const string CbslBaseUrl = "https://www.cbsl.gov.lk";
    private const string CbslReportPage = "https://www.cbsl.gov.lk/en/statistics/economic-indicators/price-report";
    private const string CbslPdfPrefix = "https://www.cbsl.gov.lk/sites/default/files/cbslweb_documents/statistics/pricerpt";
    private const string CbslSourceName = "Central Bank of Sri Lanka";
    private const string CbslUpdateNote = "Daily report for selected markets; prices are published observations, not live trading quotes.";
    private const string CbslLiveCacheKey = "cbsl:market-price-report:live";
    private const string CbslFailureCacheKey = "cbsl:market-price-report:probe-failed";
    private static readonly TimeSpan LiveReportCacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailedProbeCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DirectProbeTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex CbslReportLink = new(
        @"href\s*=\s*[""'](?<url>(?:https?://www\.cbsl\.gov\.lk)?/sites/default/files/cbslweb_documents/statistics/pricerpt/price_report_(?<date>\d{8})(?:_e)?\.pdf)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly DistrictCenter[] Districts =
    [
        new("Ampara", 7.2917, 81.6724),
        new("Anuradhapura", 8.3114, 80.4037),
        new("Badulla", 6.9934, 81.0550),
        new("Batticaloa", 7.7170, 81.7000),
        new("Colombo", 6.9271, 79.8612),
        new("Galle", 6.0535, 80.2210),
        new("Gampaha", 7.0873, 80.0144),
        new("Hambantota", 6.1241, 81.1185),
        new("Jaffna", 9.6615, 80.0255),
        new("Kalutara", 6.5854, 79.9607),
        new("Kandy", 7.2906, 80.6337),
        new("Kegalle", 7.2513, 80.3464),
        new("Kilinochchi", 9.3803, 80.3770),
        new("Kurunegala", 7.4818, 80.3609),
        new("Mannar", 8.9810, 79.9044),
        new("Matale", 7.4675, 80.6234),
        new("Matara", 5.9549, 80.5550),
        new("Monaragala", 6.8728, 81.3507),
        new("Mullaitivu", 9.2671, 80.8142),
        new("Nuwara Eliya", 6.9497, 80.7891),
        new("Polonnaruwa", 7.9403, 81.0188),
        new("Puttalam", 8.0362, 79.8283),
        new("Ratnapura", 6.7056, 80.3847),
        new("Trincomalee", 8.5874, 81.2152),
        new("Vavuniya", 8.7514, 80.4971)
    ];

    public static RouteGroupBuilder MapLiveDataEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/weather-observations", GetCurrentWeather);
        api.MapGet("/market-prices", GetLatestMarketPriceReport);
        return api;
    }

    private static async Task<IResult> GetCurrentWeather(
        IHttpClientFactory httpClientFactory,
        ILogger<LiveDataEndpointLoggerCategory> logger,
        CancellationToken cancellationToken)
    {
        var latitude = string.Join(",", Districts.Select(district => district.Latitude.ToString(CultureInfo.InvariantCulture)));
        var longitude = string.Join(",", Districts.Select(district => district.Longitude.ToString(CultureInfo.InvariantCulture)));
        var requestUri = "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={latitude}&longitude={longitude}" +
            "&current=temperature_2m%2Crelative_humidity_2m%2Cprecipitation%2Cweather_code" +
            "&timezone=Asia%2FColombo";

        try
        {
            var client = httpClientFactory.CreateClient("open-meteo");
            using var response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Open-Meteo returned HTTP {StatusCode}.", (int)response.StatusCode);
                return Results.Problem(
                    "Current district weather is temporarily unavailable.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                logger.LogError("Open-Meteo returned an unexpected response shape for multiple locations.");
                return Results.Problem(
                    "The weather provider returned an unexpected response.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var responses = document.RootElement.EnumerateArray().ToArray();
            if (responses.Length != Districts.Length)
            {
                logger.LogError(
                    "Open-Meteo returned {ResponseCount} locations; expected {ExpectedCount}.",
                    responses.Length,
                    Districts.Length);
                return Results.Problem(
                    "The weather provider returned incomplete district data.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var retrievedAtUtc = DateTimeOffset.UtcNow;
            var observations = new List<CurrentDistrictWeather>(Districts.Length);
            for (var index = 0; index < Districts.Length; index++)
            {
                var location = responses[index];
                if (!location.TryGetProperty("current", out var current) ||
                    !current.TryGetProperty("time", out var time) ||
                    !current.TryGetProperty("temperature_2m", out var temperature) ||
                    !current.TryGetProperty("relative_humidity_2m", out var humidity) ||
                    !current.TryGetProperty("precipitation", out var precipitation) ||
                    !current.TryGetProperty("weather_code", out var weatherCode) ||
                    time.ValueKind != JsonValueKind.String ||
                    !temperature.TryGetDouble(out var temperatureC) ||
                    !humidity.TryGetDouble(out var humidityPercent) ||
                    !precipitation.TryGetDouble(out var precipitationMm) ||
                    !weatherCode.TryGetInt32(out var code))
                {
                    logger.LogError("Open-Meteo returned incomplete current conditions for district index {Index}.", index);
                    return Results.Problem(
                        "The weather provider returned incomplete current conditions.",
                        statusCode: StatusCodes.Status502BadGateway);
                }

                var district = Districts[index];
                observations.Add(new CurrentDistrictWeather(
                    district.Name,
                    district.Latitude,
                    district.Longitude,
                    time.GetString()!,
                    temperatureC,
                    humidityPercent,
                    precipitationMm,
                    code,
                    retrievedAtUtc,
                    OpenMeteoAttribution,
                    OpenMeteoSource));
            }

            return Results.Ok(observations);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Unable to retrieve current district weather from Open-Meteo.");
            return Results.Problem(
                "Current district weather is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The Open-Meteo request timed out.");
            return Results.Problem(
                "The weather provider timed out. Try again shortly.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Open-Meteo returned invalid JSON.");
            return Results.Problem(
                "The weather provider returned invalid data.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> GetLatestMarketPriceReport(
        IHttpClientFactory httpClientFactory,
        SWARNABHOOMIContext db,
        IMemoryCache cache,
        ILogger<LiveDataEndpointLoggerCategory> logger,
        CancellationToken cancellationToken,
        [FromQuery] bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            if (cache.TryGetValue(CbslLiveCacheKey, out LatestMarketPriceReport? cachedLive) &&
                cachedLive is not null)
            {
                return Results.Ok(cachedLive);
            }

            if (cache.TryGetValue(CbslFailureCacheKey, out string? cachedFailureReason) &&
                !string.IsNullOrWhiteSpace(cachedFailureReason))
            {
                return await BuildDatabaseFallbackResultAsync(db, logger, cachedFailureReason, cancellationToken);
            }
        }

        var client = httpClientFactory.CreateClient("cbsl-prices");
        int? lastBlockedStatus = null;

        // Stage 1: Scrape the CBSL daily price report index page with browser fetch headers.
        var stage1 = await TryFetchFromIndexPageAsync(client, logger, cancellationToken);
        if (stage1.Report is not null)
        {
            var observations = await LoadLatestObservationsAsync(db, cancellationToken);
            var report = BuildLiveReport(stage1.Report.Date, stage1.Report.PdfUrl, observations);
            cache.Set(CbslLiveCacheKey, report, LiveReportCacheDuration);
            cache.Remove(CbslFailureCacheKey);
            return Results.Ok(report);
        }

        lastBlockedStatus = stage1.StatusCode;

        // Stage 2: Probe deterministic static CBSL PDF URLs backward up to 7 calendar days (SLT).
        var stage2 = await TryProbeDeterministicPdfUrlsAsync(client, logger, cancellationToken);
        if (stage2.Report is not null)
        {
            var observations = await LoadLatestObservationsAsync(db, cancellationToken);
            var report = BuildLiveReport(stage2.Report.Date, stage2.Report.PdfUrl, observations);
            cache.Set(CbslLiveCacheKey, report, LiveReportCacheDuration);
            cache.Remove(CbslFailureCacheKey);
            return Results.Ok(report);
        }

        lastBlockedStatus ??= stage2.StatusCode;

        // Stage 3: Cache the failed live probe briefly and fall back to persisted database observations.
        var failureReason = lastBlockedStatus switch
        {
            StatusCodes.Status403Forbidden => "Live CBSL feed returned HTTP 403; displaying latest verified market price observations.",
            int statusCode => $"Live CBSL feed returned HTTP {statusCode}; displaying latest verified market price observations.",
            _ => "Live CBSL feed is temporarily unreachable; displaying latest verified market price observations."
        };

        cache.Set(CbslFailureCacheKey, failureReason, FailedProbeCooldown);
        return await BuildDatabaseFallbackResultAsync(db, logger, failureReason, cancellationToken);
    }

    private static async Task<(CbslPdfMatch? Report, int? StatusCode)> TryFetchFromIndexPageAsync(
        HttpClient client,
        ILogger<LiveDataEndpointLoggerCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, CbslReportPage);
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                logger.LogWarning(
                    "CBSL returned HTTP {StatusCode} for the daily price report index; falling back to direct PDF probing.",
                    statusCode);
                return (null, statusCode);
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var latestReport = CbslReportLink.Matches(html)
                .Select(match =>
                {
                    var dateText = match.Groups["date"].Value;
                    return DateOnly.TryParseExact(
                        dateText,
                        "yyyyMMdd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date)
                        ? new
                        {
                            Date = date,
                            Url = match.Groups["url"].Value,
                            IsEnglish = match.Groups["url"].Value.EndsWith("_e.pdf", StringComparison.OrdinalIgnoreCase)
                        }
                        : null;
                })
                .Where(report => report is not null)
                .OrderByDescending(report => report!.Date)
                .ThenByDescending(report => report!.IsEnglish)
                .FirstOrDefault();

            if (latestReport is null)
            {
                logger.LogWarning(
                    "No CBSL daily price report links matched on the official report page; falling back to direct PDF probing.");
                return (null, null);
            }

            var pdfUri = new Uri(new Uri(CbslBaseUrl), latestReport.Url);
            return (new CbslPdfMatch(latestReport.Date, pdfUri.AbsoluteUri), StatusCodes.Status200OK);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "CBSL index request failed; falling back to direct PDF probing.");
            return (null, null);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The CBSL index request timed out; falling back to direct PDF probing.");
            return (null, StatusCodes.Status504GatewayTimeout);
        }
    }

    private static async Task<(CbslPdfMatch? Report, int? StatusCode)> TryProbeDeterministicPdfUrlsAsync(
        HttpClient client,
        ILogger<LiveDataEndpointLoggerCategory> logger,
        CancellationToken cancellationToken)
    {
        var todaySlt = GetSriLankaToday();
        int? lastBlockedStatus = null;
        var consecutiveTimeouts = 0;

        for (var offset = 0; offset < 7; offset++)
        {
            var candidateDate = todaySlt.AddDays(-offset);
            if (candidateDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var dateStamp = candidateDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string[] candidates =
            [
                $"{CbslPdfPrefix}/price_report_{dateStamp}_e.pdf",
                $"{CbslPdfPrefix}/price_report_{dateStamp}.pdf"
            ];

            foreach (var candidateUrl in candidates)
            {
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                probeCts.CancelAfter(DirectProbeTimeout);

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, candidateUrl);
                    request.Headers.TryAddWithoutValidation("Accept", "application/pdf,*/*;q=0.8");
                    request.Headers.Referrer = new Uri(CbslReportPage);
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
                    request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

                    using var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        probeCts.Token);

                    consecutiveTimeouts = 0;

                    if (response.IsSuccessStatusCode && await IsValidPdfResponseAsync(response, probeCts.Token))
                    {
                        logger.LogInformation(
                            "Resolved CBSL daily price report via direct PDF probe for {ReportDate}.",
                            candidateDate);
                        return (new CbslPdfMatch(candidateDate, candidateUrl), StatusCodes.Status200OK);
                    }

                    var statusCode = (int)response.StatusCode;
                    if (statusCode is StatusCodes.Status403Forbidden or StatusCodes.Status429TooManyRequests or StatusCodes.Status503ServiceUnavailable)
                    {
                        lastBlockedStatus ??= statusCode;
                    }
                }
                catch (HttpRequestException)
                {
                    // Continue probing remaining deterministic candidate URLs.
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    consecutiveTimeouts++;
                    lastBlockedStatus ??= StatusCodes.Status504GatewayTimeout;
                    if (consecutiveTimeouts >= 3)
                    {
                        logger.LogWarning("CBSL direct PDF probing timed out repeatedly; proceeding to database fallback.");
                        return (null, lastBlockedStatus);
                    }
                }
            }
        }

        if (lastBlockedStatus is not null)
        {
            logger.LogWarning(
                "CBSL direct PDF probing was blocked or timed out (HTTP {StatusCode}).",
                lastBlockedStatus);
        }

        return (null, lastBlockedStatus);
    }

    private static async Task<bool> IsValidPdfResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var hasPdfMediaType = string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var signature = new byte[4];
        var totalRead = 0;

        while (totalRead < signature.Length)
        {
            var bytesRead = await stream.ReadAsync(signature.AsMemory(totalRead, signature.Length - totalRead), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }
            totalRead += bytesRead;
        }

        var startsWithPdfMagic = totalRead == 4 &&
            signature[0] == (byte)'%' &&
            signature[1] == (byte)'P' &&
            signature[2] == (byte)'D' &&
            signature[3] == (byte)'F';

        return startsWithPdfMagic || (hasPdfMediaType && totalRead > 0);
    }

    private static async Task<IReadOnlyList<MarketPriceObservationItem>> LoadLatestObservationsAsync(
        SWARNABHOOMIContext db,
        CancellationToken cancellationToken)
    {
        var raw = await db.MarketPriceObservations
            .AsNoTracking()
            .Include(observation => observation.RiceVariety)
            .OrderByDescending(observation => observation.ObservationDate)
            .ThenByDescending(observation => observation.CreatedAt)
            .Select(observation => new
            {
                observation.Id,
                observation.Market,
                observation.RiceVarietyId,
                RiceVarietyName = observation.RiceVariety.Name,
                VarietyCode = observation.RiceVariety.VarietyCode,
                observation.Grade,
                observation.PriceType,
                observation.Value,
                observation.Currency,
                observation.Unit,
                observation.ObservationDate,
                observation.PublicationDate,
                observation.Source
            })
            .Take(50)
            .ToListAsync(cancellationToken);

        return raw.Select(observation => new MarketPriceObservationItem(
            observation.Id,
            observation.Market,
            observation.RiceVarietyId,
            observation.RiceVarietyName,
            observation.VarietyCode,
            observation.Grade,
            observation.PriceType,
            observation.Value,
            observation.Currency,
            observation.Unit,
            observation.ObservationDate,
            observation.PublicationDate,
            observation.Source))
            .ToList();
    }

    private static async Task<IResult> BuildDatabaseFallbackResultAsync(
        SWARNABHOOMIContext db,
        ILogger<LiveDataEndpointLoggerCategory> logger,
        string failureReason,
        CancellationToken cancellationToken)
    {
        var observations = await LoadLatestObservationsAsync(db, cancellationToken);
        if (observations.Count == 0)
        {
            logger.LogError(
                "CBSL live fetch failed ({Reason}) and no cached MarketPriceObservation records are available.",
                failureReason);
            return Results.Problem(
                "The latest CBSL price report is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        logger.LogWarning(
            "CBSL live fetch failed ({Reason}). Returning {ObservationCount} cached MarketPriceObservation record(s).",
            failureReason,
            observations.Count);

        var latestDate = observations[0].PublicationDate ?? observations[0].ObservationDate;
        return Results.Ok(new LatestMarketPriceReport(
            latestDate,
            null,
            CbslReportPage,
            CbslSourceName,
            CbslUpdateNote,
            DateTimeOffset.UtcNow,
            IsCachedFallback: true,
            Warning: failureReason,
            Observations: observations));
    }

    private static LatestMarketPriceReport BuildLiveReport(
        DateOnly reportDate,
        string pdfUrl,
        IReadOnlyList<MarketPriceObservationItem> observations)
    {
        return new LatestMarketPriceReport(
            reportDate,
            pdfUrl,
            CbslReportPage,
            CbslSourceName,
            CbslUpdateNote,
            DateTimeOffset.UtcNow,
            IsCachedFallback: false,
            Warning: null,
            Observations: observations);
    }

    private static DateOnly GetSriLankaToday()
    {
        TimeZoneInfo slt;
        try
        {
            slt = TimeZoneInfo.FindSystemTimeZoneById("Sri Lanka Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                slt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");
            }
            catch (TimeZoneNotFoundException)
            {
                slt = TimeZoneInfo.CreateCustomTimeZone(
                    "SLT",
                    TimeSpan.FromHours(5.5),
                    "Sri Lanka Standard Time",
                    "Sri Lanka Standard Time");
            }
        }

        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, slt);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private sealed record CbslPdfMatch(DateOnly Date, string PdfUrl);
    private sealed record DistrictCenter(string Name, double Latitude, double Longitude);
}

public sealed class LiveDataEndpointLoggerCategory
{
}

public sealed record CurrentDistrictWeather(
    string District,
    double Latitude,
    double Longitude,
    string ObservationTime,
    double TemperatureC,
    double HumidityPercent,
    double PrecipitationMm,
    int WeatherCode,
    DateTimeOffset RetrievedAtUtc,
    string Source,
    string SourceUrl);

public sealed record MarketPriceObservationItem(
    Guid Id,
    string Market,
    Guid RiceVarietyId,
    string RiceVarietyName,
    string VarietyCode,
    string Grade,
    string PriceType,
    decimal Value,
    string Currency,
    string Unit,
    DateOnly ObservationDate,
    DateOnly? PublicationDate,
    string? Source);

public sealed record LatestMarketPriceReport(
    DateOnly ReportDate,
    string? PdfUrl,
    string SourceUrl,
    string SourceName,
    string UpdateNote,
    DateTimeOffset CheckedAtUtc,
    bool IsCachedFallback = false,
    string? Warning = null,
    IReadOnlyList<MarketPriceObservationItem>? Observations = null);
