using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SWARNABHOOMI.Api.LiveData;

public static class LiveDataEndpoints
{
    private const string OpenMeteoSource = "https://open-meteo.com/en/docs";
    private const string OpenMeteoAttribution = "Weather data by Open-Meteo.com";
    private const string CbslReportPage = "https://www.cbsl.gov.lk/en/statistics/economic-indicators/price-report";
    private static readonly Regex CbslReportLink = new(
        @"href\s*=\s*[""'](?<url>(?:https?://www\.cbsl\.gov\.lk)?/sites/default/files/cbslweb_documents/statistics/pricerpt/price_report_(?<date>\d{8})_e\.pdf)[""']",
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
        ILogger<LiveDataEndpointLoggerCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("cbsl-prices");
            using var response = await client.GetAsync(CbslReportPage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("CBSL returned HTTP {StatusCode} for the daily price report index.", (int)response.StatusCode);
                return Results.Problem(
                    "The latest CBSL price report is temporarily unavailable.",
                    statusCode: StatusCodes.Status502BadGateway);
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
                        ? new { Date = date, Url = match.Groups["url"].Value }
                        : null;
                })
                .Where(report => report is not null)
                .OrderByDescending(report => report!.Date)
                .FirstOrDefault();

            if (latestReport is null)
            {
                logger.LogError("No CBSL daily price report links were found on the official report page.");
                return Results.Problem(
                    "No current CBSL daily price report was found.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var pdfUri = new Uri(new Uri("https://www.cbsl.gov.lk"), latestReport.Url);
            return Results.Ok(new LatestMarketPriceReport(
                latestReport.Date,
                pdfUri.AbsoluteUri,
                CbslReportPage,
                "Central Bank of Sri Lanka",
                "Daily report for selected markets; prices are published observations, not live trading quotes.",
                DateTimeOffset.UtcNow));
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Unable to retrieve the latest CBSL price report.");
            return Results.Problem(
                "The latest CBSL price report is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The CBSL price report request timed out.");
            return Results.Problem(
                "The CBSL price report request timed out. Try again shortly.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }

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

public sealed record LatestMarketPriceReport(
    DateOnly ReportDate,
    string PdfUrl,
    string SourceUrl,
    string SourceName,
    string UpdateNote,
    DateTimeOffset CheckedAtUtc);
