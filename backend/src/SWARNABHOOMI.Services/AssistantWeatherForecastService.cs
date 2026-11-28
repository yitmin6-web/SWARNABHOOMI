using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Services;

public sealed record DailyTaskVerdict(
    string Task,
    string Rating,
    string Reason);

public sealed record DailyWeatherForecastItem(
    DateOnly Date,
    string DayLabel,
    string DayOfWeek,
    string ConditionText,
    double MaxTempC,
    double MinTempC,
    int PrecipitationProbabilityPercent,
    double PrecipitationMm,
    int ThunderstormProbabilityPercent,
    int RelativeHumidityPercent,
    double WindSpeedKmh,
    double WindGustKmh,
    DailyTaskVerdict WeedingSafety,
    DailyTaskVerdict SprayingSafety,
    DailyTaskVerdict FertilizingSafety,
    DailyTaskVerdict HarvestingSafety);

public sealed record TaskBestWindow(
    string Task,
    string BestDayLabel,
    string BestRating,
    string Summary);

public sealed record WeatherForecastBriefing(
    string LocationLabel,
    double Latitude,
    double Longitude,
    string Source,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<DailyWeatherForecastItem> Days,
    IReadOnlyList<TaskBestWindow> BestWindows);

public interface IAssistantWeatherForecastService
{
    Task<WeatherForecastBriefing> Get7DayForecastAsync(
        double? latitude,
        double? longitude,
        string? district,
        CancellationToken cancellationToken = default);

    bool TryComputeGeoJsonCentroid(
        string? boundaryGeoJson,
        out double latitude,
        out double longitude);

    string? DetectDistrictInText(string? text);
}

public sealed class AssistantWeatherForecastService(
    SWARNABHOOMIContext db,
    IMemoryCache memoryCache,
    IConfiguration configuration,
    ILogger<AssistantWeatherForecastService> logger,
    HttpClient httpClient) : IAssistantWeatherForecastService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(5);

    private static readonly Dictionary<string, (double Lat, double Lng)> DistrictCoordinates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ampara"] = (7.2917, 81.6724),
            ["Anuradhapura"] = (8.3114, 80.4037),
            ["Badulla"] = (6.9934, 81.0550),
            ["Batticaloa"] = (7.7170, 81.7000),
            ["Colombo"] = (6.9271, 79.8612),
            ["Galle"] = (6.0535, 80.2210),
            ["Gampaha"] = (7.0873, 80.0144),
            ["Hambantota"] = (6.1241, 81.1185),
            ["Jaffna"] = (9.6615, 80.0255),
            ["Kalutara"] = (6.5854, 79.9607),
            ["Kandy"] = (7.2906, 80.6337),
            ["Kegalle"] = (7.2513, 80.3464),
            ["Kilinochchi"] = (9.3803, 80.3770),
            ["Kurunegala"] = (7.4818, 80.3609),
            ["Mannar"] = (8.9810, 79.9044),
            ["Matale"] = (7.4675, 80.6234),
            ["Matara"] = (5.9549, 80.5550),
            ["Monaragala"] = (6.8728, 81.3507),
            ["Mullaitivu"] = (9.2671, 80.8142),
            ["Nuwara Eliya"] = (6.9497, 80.7891),
            ["Polonnaruwa"] = (7.9403, 81.0188),
            ["Puttalam"] = (8.0362, 79.8283),
            ["Ratnapura"] = (6.7056, 80.3847),
            ["Trincomalee"] = (8.5874, 81.2152),
            ["Vavuniya"] = (8.7514, 80.4971)
        };

    private sealed record RawForecastDay(
        DateOnly Date,
        string ConditionText,
        double MaxTempC,
        double MinTempC,
        int PrecipitationProbabilityPercent,
        double PrecipitationMm,
        int ThunderstormProbabilityPercent,
        int RelativeHumidityPercent,
        double WindSpeedKmh,
        double WindGustKmh);

    public async Task<WeatherForecastBriefing> Get7DayForecastAsync(
        double? latitude,
        double? longitude,
        string? district,
        CancellationToken cancellationToken = default)
    {
        var (resolvedLat, resolvedLng, locationLabel) = ResolveCoordinates(latitude, longitude, district);
        var cacheKey = FormattableString.Invariant(
            $"assistant:weather:7d:{Math.Round(resolvedLat, 2):F2}:{Math.Round(resolvedLng, 2):F2}:{locationLabel}");

        if (memoryCache.TryGetValue(cacheKey, out WeatherForecastBriefing? cached) && cached is not null)
        {
            return cached;
        }

        var todaySlt = GetSriLankaToday();
        List<RawForecastDay>? rawDays = null;
        var source = "Cached Local Weather Observation";

        var googleApiKey = ResolveGoogleWeatherApiKey();
        if (!string.IsNullOrWhiteSpace(googleApiKey))
        {
            rawDays = await TryFetchGoogleWeatherAsync(resolvedLat, resolvedLng, googleApiKey, cancellationToken);
            if (rawDays is { Count: > 0 })
            {
                source = "Google Weather API (7-Day Forecast)";
            }
        }

        if (rawDays is null || rawDays.Count == 0)
        {
            rawDays = await TryFetchOpenMeteoAsync(resolvedLat, resolvedLng, cancellationToken);
            if (rawDays is { Count: > 0 })
            {
                source = "Open-Meteo 7-Day Forecast (Fallback)";
            }
        }

        if (rawDays is null || rawDays.Count == 0)
        {
            rawDays = await BuildDatabaseFallbackDaysAsync(district, todaySlt, cancellationToken);
            source = "Cached Local Weather Observation";
        }

        var evaluatedDays = rawDays
            .OrderBy(d => d.Date)
            .Take(7)
            .Select((day, index) => EvaluateDayTasks(day, todaySlt, index))
            .ToList();

        var bestWindows = ComputeBestWindows(evaluatedDays);
        var briefing = new WeatherForecastBriefing(
            locationLabel,
            Math.Round(resolvedLat, 4),
            Math.Round(resolvedLng, 4),
            source,
            DateTimeOffset.UtcNow,
            evaluatedDays,
            bestWindows);

        memoryCache.Set(cacheKey, briefing, CacheDuration);
        return briefing;
    }

    public bool TryComputeGeoJsonCentroid(
        string? boundaryGeoJson,
        out double latitude,
        out double longitude)
    {
        latitude = 0;
        longitude = 0;

        if (string.IsNullOrWhiteSpace(boundaryGeoJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(boundaryGeoJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("coordinates", out var coordinates) ||
                coordinates.ValueKind != JsonValueKind.Array ||
                coordinates.GetArrayLength() == 0)
            {
                return false;
            }

            var outerRing = coordinates[0];
            if (outerRing.ValueKind != JsonValueKind.Array || outerRing.GetArrayLength() < 3)
            {
                return false;
            }

            var points = new List<(double Lng, double Lat)>();
            foreach (var pt in outerRing.EnumerateArray())
            {
                if (pt.ValueKind == JsonValueKind.Array &&
                    pt.GetArrayLength() >= 2 &&
                    pt[0].TryGetDouble(out var lng) &&
                    pt[1].TryGetDouble(out var lat) &&
                    double.IsFinite(lng) &&
                    double.IsFinite(lat))
                {
                    points.Add((lng, lat));
                }
            }

            if (points.Count < 3)
            {
                return false;
            }

            if (points.Count > 3 &&
                Math.Abs(points[0].Lng - points[^1].Lng) < 1e-8 &&
                Math.Abs(points[0].Lat - points[^1].Lat) < 1e-8)
            {
                points.RemoveAt(points.Count - 1);
            }

            latitude = points.Average(p => p.Lat);
            longitude = points.Average(p => p.Lng);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public string? DetectDistrictInText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var districtName in DistrictCoordinates.Keys)
        {
            if (text.Contains(districtName, StringComparison.OrdinalIgnoreCase))
            {
                return districtName;
            }
        }

        return null;
    }

    private (double Lat, double Lng, string Label) ResolveCoordinates(
        double? latitude,
        double? longitude,
        string? district)
    {
        if (latitude.HasValue && longitude.HasValue &&
            double.IsFinite(latitude.Value) && double.IsFinite(longitude.Value) &&
            latitude.Value is >= -90 and <= 90 &&
            longitude.Value is >= -180 and <= 180)
        {
            var label = string.IsNullOrWhiteSpace(district)
                ? FormattableString.Invariant($"{latitude.Value:F4}, {longitude.Value:F4}")
                : $"{district.Trim()} (Mapped Field)";
            return (latitude.Value, longitude.Value, label);
        }

        if (!string.IsNullOrWhiteSpace(district) &&
            DistrictCoordinates.TryGetValue(district.Trim(), out var coords))
        {
            return (coords.Lat, coords.Lng, district.Trim());
        }

        var colombo = DistrictCoordinates["Colombo"];
        return (colombo.Lat, colombo.Lng, "Colombo (Default)");
    }

    private string? ResolveGoogleWeatherApiKey()
    {
        var configured = configuration["GoogleWeather:ApiKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        var envKey = Environment.GetEnvironmentVariable("GOOGLE_WEATHER_API_KEY");
        return string.IsNullOrWhiteSpace(envKey) ? null : envKey.Trim();
    }

    private async Task<List<RawForecastDay>?> TryFetchGoogleWeatherAsync(
        double lat,
        double lng,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var requestUri = FormattableString.Invariant(
            $"https://weather.googleapis.com/v1/forecast/days:lookup?key={Uri.EscapeDataString(apiKey)}&location.latitude={lat:F4}&location.longitude={lng:F4}&days=7&pageSize=7");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ProviderTimeout);

        try
        {
            using var response = await httpClient.GetAsync(requestUri, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google Weather API returned HTTP {StatusCode}; falling back to Open-Meteo.", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            if (!doc.RootElement.TryGetProperty("forecastDays", out var daysEl) ||
                daysEl.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new List<RawForecastDay>(7);
            foreach (var dayEl in daysEl.EnumerateArray())
            {
                if (!TryParseGoogleDate(dayEl, out var date))
                {
                    continue;
                }

                var hasDaytime = dayEl.TryGetProperty("daytimeForecast", out var daytime) &&
                                 daytime.ValueKind == JsonValueKind.Object;
                var hasNighttime = dayEl.TryGetProperty("nighttimeForecast", out var nighttime) &&
                                   nighttime.ValueKind == JsonValueKind.Object;
                var primary = hasDaytime ? daytime : (hasNighttime ? nighttime : default);

                var condition = ExtractNestedString(primary, "weatherCondition", "description", "text") ?? "Partly cloudy";
                var rainProb = (int)Math.Round(ExtractNestedDouble(primary, "precipitation", "probability", "percent") ?? 0d);
                var rainMm = ExtractNestedDouble(primary, "precipitation", "qpf", "quantity") ?? 0d;
                var nightRainMm = hasNighttime
                    ? (ExtractNestedDouble(nighttime, "precipitation", "qpf", "quantity") ?? 0d)
                    : 0d;
                var totalRainMm = Math.Round(rainMm + (hasDaytime ? nightRainMm : 0d), 1);

                var stormProb = (int)Math.Round(ExtractDouble(primary, "thunderstormProbability") ?? 0d);
                var humidity = (int)Math.Round(ExtractDouble(primary, "relativeHumidity") ?? 75d);
                var windSpeed = ExtractNestedDouble(primary, "wind", "speed", "value") ?? 8d;
                var windGust = ExtractNestedDouble(primary, "wind", "gust", "value") ?? windSpeed;

                var maxTemp = ExtractNestedDouble(dayEl, "maxTemperature", "degrees") ?? 31d;
                var minTemp = ExtractNestedDouble(dayEl, "minTemperature", "degrees") ?? 24d;

                result.Add(new RawForecastDay(
                    date,
                    condition,
                    Math.Round(maxTemp, 1),
                    Math.Round(minTemp, 1),
                    Math.Clamp(rainProb, 0, 100),
                    Math.Max(0, totalRainMm),
                    Math.Clamp(stormProb, 0, 100),
                    Math.Clamp(humidity, 0, 100),
                    Math.Round(Math.Max(0, windSpeed), 1),
                    Math.Round(Math.Max(0, windGust), 1)));
            }

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Google Weather API request failed; falling back to Open-Meteo.");
            return null;
        }
    }

    private async Task<List<RawForecastDay>?> TryFetchOpenMeteoAsync(
        double lat,
        double lng,
        CancellationToken cancellationToken)
    {
        var requestUri = FormattableString.Invariant(
            $"https://api.open-meteo.com/v1/forecast?latitude={lat:F4}&longitude={lng:F4}&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max,wind_speed_10m_max,wind_gusts_10m_max&timezone=Asia%2FColombo&forecast_days=7");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ProviderTimeout);

        try
        {
            using var response = await httpClient.GetAsync(requestUri, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Open-Meteo 7-day forecast returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            if (!doc.RootElement.TryGetProperty("daily", out var daily) ||
                !daily.TryGetProperty("time", out var times) ||
                times.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            daily.TryGetProperty("weather_code", out var codes);
            daily.TryGetProperty("temperature_2m_max", out var maxTemps);
            daily.TryGetProperty("temperature_2m_min", out var minTemps);
            daily.TryGetProperty("precipitation_sum", out var rainSums);
            daily.TryGetProperty("precipitation_probability_max", out var rainProbs);
            daily.TryGetProperty("wind_speed_10m_max", out var winds);
            daily.TryGetProperty("wind_gusts_10m_max", out var gusts);

            var result = new List<RawForecastDay>(7);
            var count = times.GetArrayLength();
            for (var i = 0; i < count; i++)
            {
                var dateText = times[i].GetString();
                if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                var wmoCode = GetArrayInt(codes, i) ?? 2;
                var maxTemp = GetArrayDouble(maxTemps, i) ?? 31.0;
                var minTemp = GetArrayDouble(minTemps, i) ?? 24.0;
                var rainMm = Math.Max(0, GetArrayDouble(rainSums, i) ?? 0.0);
                var rainProb = Math.Clamp(GetArrayInt(rainProbs, i) ?? (rainMm >= 5 ? 65 : 20), 0, 100);
                var wind = Math.Max(0, GetArrayDouble(winds, i) ?? 8.0);
                var gust = Math.Max(wind, GetArrayDouble(gusts, i) ?? wind);

                var stormProb = wmoCode switch
                {
                    >= 95 => Math.Max(70, rainProb),
                    >= 80 and <= 82 when rainMm >= 15 => 35,
                    >= 80 and <= 82 => 20,
                    _ => 5
                };

                var estimatedHumidity = rainMm >= 15 ? 88 : (rainMm >= 3 || rainProb >= 60 ? 82 : 74);

                result.Add(new RawForecastDay(
                    date,
                    DescribeWmoCode(wmoCode),
                    Math.Round(maxTemp, 1),
                    Math.Round(minTemp, 1),
                    rainProb,
                    Math.Round(rainMm, 1),
                    stormProb,
                    estimatedHumidity,
                    Math.Round(wind, 1),
                    Math.Round(gust, 1)));
            }

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Open-Meteo 7-day forecast failed; falling back to database WeatherObservations.");
            return null;
        }
    }

    private async Task<List<RawForecastDay>> BuildDatabaseFallbackDaysAsync(
        string? district,
        DateOnly todaySlt,
        CancellationToken cancellationToken)
    {
        WeatherObservation? latest = null;
        try
        {
            var query = db.WeatherObservations.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(district))
            {
                var trimmed = district.Trim();
                latest = await query
                    .Where(w => w.Location == trimmed)
                    .OrderByDescending(w => w.ObservationDate)
                    .ThenByDescending(w => w.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            latest ??= await query
                .OrderByDescending(w => w.ObservationDate)
                .ThenByDescending(w => w.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to query cached WeatherObservations for fallback.");
        }

        var tempMax = (double)(latest?.TemperatureC ?? 30.0m);
        var tempMin = Math.Round(tempMax - 6.0, 1);
        var rainMm = (double)(latest?.RainfallMm ?? 4.0m);
        var humidity = (int)Math.Round((double)(latest?.HumidityPercent ?? 76m));
        var rainProb = rainMm >= 15 ? 70 : (rainMm >= 5 ? 45 : 20);
        var condition = rainMm >= 15 ? "Moderate to heavy rain (cached baseline)" : "Warm tropical baseline (cached observation)";

        var days = new List<RawForecastDay>(7);
        for (var offset = 0; offset < 7; offset++)
        {
            days.Add(new RawForecastDay(
                todaySlt.AddDays(offset),
                condition,
                Math.Round(tempMax, 1),
                tempMin,
                rainProb,
                Math.Round(rainMm, 1),
                rainMm >= 20 ? 40 : 10,
                Math.Clamp(humidity, 40, 98),
                9.0,
                14.0));
        }

        return days;
    }

    private static DailyWeatherForecastItem EvaluateDayTasks(
        RawForecastDay day,
        DateOnly todaySlt,
        int index)
    {
        var dayOfWeek = day.Date.DayOfWeek.ToString();
        var dayLabel = index switch
        {
            0 => $"Today ({day.Date:yyyy-MM-dd}, {dayOfWeek})",
            1 => $"Tomorrow ({day.Date:yyyy-MM-dd}, {dayOfWeek})",
            _ when day.Date == todaySlt => $"Today ({day.Date:yyyy-MM-dd}, {dayOfWeek})",
            _ when day.Date == todaySlt.AddDays(1) => $"Tomorrow ({day.Date:yyyy-MM-dd}, {dayOfWeek})",
            _ => $"{dayOfWeek} ({day.Date:yyyy-MM-dd})"
        };

        var weeding = EvaluateWeeding(day);
        var spraying = EvaluateSpraying(day);
        var fertilizing = EvaluateFertilizing(day);
        var harvesting = EvaluateHarvesting(day);

        return new DailyWeatherForecastItem(
            day.Date,
            dayLabel,
            dayOfWeek,
            day.ConditionText,
            day.MaxTempC,
            day.MinTempC,
            day.PrecipitationProbabilityPercent,
            day.PrecipitationMm,
            day.ThunderstormProbabilityPercent,
            day.RelativeHumidityPercent,
            day.WindSpeedKmh,
            day.WindGustKmh,
            weeding,
            spraying,
            fertilizing,
            harvesting);
    }

    private static DailyTaskVerdict EvaluateWeeding(RawForecastDay day)
    {
        if (day.ThunderstormProbabilityPercent >= 40 || day.PrecipitationMm >= 20)
        {
            return new DailyTaskVerdict(
                "Manual Weeding / Field Work",
                "UNSAFE",
                $"High storm ({day.ThunderstormProbabilityPercent}%) or heavy rain ({day.PrecipitationMm:F1} mm) risk.");
        }

        if (day.PrecipitationProbabilityPercent >= 60 || day.PrecipitationMm >= 8 || day.MaxTempC >= 36)
        {
            return new DailyTaskVerdict(
                "Manual Weeding / Field Work",
                "CAUTION",
                $"Moderate rain risk ({day.PrecipitationProbabilityPercent}%, {day.PrecipitationMm:F1} mm) or heat ({day.MaxTempC:F1}°C); work early morning.");
        }

        return new DailyTaskVerdict(
            "Manual Weeding / Field Work",
            "SAFE",
            $"Favorable field conditions ({day.PrecipitationProbabilityPercent}% rain, {day.PrecipitationMm:F1} mm, {day.MaxTempC:F1}°C).");
    }

    private static DailyTaskVerdict EvaluateSpraying(RawForecastDay day)
    {
        if (day.PrecipitationProbabilityPercent >= 50 || day.PrecipitationMm >= 3 || day.WindSpeedKmh >= 15)
        {
            return new DailyTaskVerdict(
                "Herbicide / Pesticide Spraying",
                "UNSAFE",
                $"Wash-off or spray drift risk ({day.PrecipitationProbabilityPercent}% rain, {day.PrecipitationMm:F1} mm, wind {day.WindSpeedKmh:F1} km/h).");
        }

        if (day.PrecipitationProbabilityPercent >= 30 || day.WindSpeedKmh >= 10)
        {
            return new DailyTaskVerdict(
                "Herbicide / Pesticide Spraying",
                "CAUTION",
                $"Marginal conditions ({day.PrecipitationProbabilityPercent}% rain, wind {day.WindSpeedKmh:F1} km/h); spray only during calm morning hours.");
        }

        return new DailyTaskVerdict(
            "Herbicide / Pesticide Spraying",
            "SAFE",
            $"Dry & calm window ({day.PrecipitationProbabilityPercent}% rain, {day.PrecipitationMm:F1} mm, wind {day.WindSpeedKmh:F1} km/h).");
    }

    private static DailyTaskVerdict EvaluateFertilizing(RawForecastDay day)
    {
        if (day.PrecipitationMm >= 15 || day.PrecipitationProbabilityPercent >= 70)
        {
            return new DailyTaskVerdict(
                "Fertilizer Application (Urea / TSP / MOP)",
                "UNSAFE",
                $"High nutrient leaching/runoff risk ({day.PrecipitationProbabilityPercent}% rain, {day.PrecipitationMm:F1} mm).");
        }

        if (day.PrecipitationMm >= 5)
        {
            return new DailyTaskVerdict(
                "Fertilizer Application (Urea / TSP / MOP)",
                "CAUTION",
                $"Moderate rainfall ({day.PrecipitationMm:F1} mm); manage bund water before top-dressing.");
        }

        return new DailyTaskVerdict(
            "Fertilizer Application (Urea / TSP / MOP)",
            "SAFE",
            $"Low runoff risk ({day.PrecipitationMm:F1} mm, {day.PrecipitationProbabilityPercent}% rain); retain 2-3 cm standing water.");
    }

    private static DailyTaskVerdict EvaluateHarvesting(RawForecastDay day)
    {
        if (day.PrecipitationProbabilityPercent >= 50 || day.PrecipitationMm >= 5)
        {
            return new DailyTaskVerdict(
                "Harvesting & Paddy Sun-Drying",
                "UNSAFE",
                $"Rain moisture/shattering/spoilage risk ({day.PrecipitationProbabilityPercent}% rain, {day.PrecipitationMm:F1} mm).");
        }

        if (day.PrecipitationProbabilityPercent >= 30 || day.RelativeHumidityPercent >= 85)
        {
            return new DailyTaskVerdict(
                "Harvesting & Paddy Sun-Drying",
                "CAUTION",
                $"Elevated humidity ({day.RelativeHumidityPercent}%) or shower chance ({day.PrecipitationProbabilityPercent}%); plan mechanical drying backup.");
        }

        return new DailyTaskVerdict(
            "Harvesting & Paddy Sun-Drying",
            "SAFE",
            $"Dry harvest & sun-drying window ({day.PrecipitationProbabilityPercent}% rain, {day.PrecipitationMm:F1} mm, {day.RelativeHumidityPercent}% RH).");
    }

    private static IReadOnlyList<TaskBestWindow> ComputeBestWindows(IReadOnlyList<DailyWeatherForecastItem> days)
    {
        if (days.Count == 0)
        {
            return [];
        }

        static TaskBestWindow PickBest(
            string taskName,
            IReadOnlyList<DailyWeatherForecastItem> items,
            Func<DailyWeatherForecastItem, DailyTaskVerdict> selector)
        {
            var ranked = items
                .Select(d => new { Day = d, Verdict = selector(d) })
                .OrderBy(x => x.Verdict.Rating switch
                {
                    "SAFE" => 0,
                    "CAUTION" => 1,
                    _ => 2
                })
                .ThenBy(x => x.Day.PrecipitationMm)
                .ThenBy(x => x.Day.PrecipitationProbabilityPercent)
                .First();

            return new TaskBestWindow(
                taskName,
                ranked.Day.DayLabel,
                ranked.Verdict.Rating,
                ranked.Verdict.Reason);
        }

        return
        [
            PickBest("Manual Weeding / Field Work", days, d => d.WeedingSafety),
            PickBest("Herbicide / Pesticide Spraying", days, d => d.SprayingSafety),
            PickBest("Fertilizer Application (Urea / TSP / MOP)", days, d => d.FertilizingSafety),
            PickBest("Harvesting & Paddy Sun-Drying", days, d => d.HarvestingSafety)
        ];
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

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, slt).DateTime);
    }

    private static bool TryParseGoogleDate(JsonElement dayEl, out DateOnly date)
    {
        date = default;
        if (!dayEl.TryGetProperty("displayDate", out var displayDate) ||
            !displayDate.TryGetProperty("year", out var yearEl) ||
            !displayDate.TryGetProperty("month", out var monthEl) ||
            !displayDate.TryGetProperty("day", out var dayNumEl) ||
            !yearEl.TryGetInt32(out var year) ||
            !monthEl.TryGetInt32(out var month) ||
            !dayNumEl.TryGetInt32(out var day))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    private static string? ExtractNestedString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static double? ExtractNestedDouble(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }
        return current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out var val) ? val : null;
    }

    private static double? ExtractDouble(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(property, out var el) &&
        el.ValueKind == JsonValueKind.Number &&
        el.TryGetDouble(out var val)
            ? val
            : null;

    private static int? GetArrayInt(JsonElement array, int index)
    {
        if (array.ValueKind != JsonValueKind.Array || index >= array.GetArrayLength())
        {
            return null;
        }
        var el = array[index];
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var val) ? val : null;
    }

    private static double? GetArrayDouble(JsonElement array, int index)
    {
        if (array.ValueKind != JsonValueKind.Array || index >= array.GetArrayLength())
        {
            return null;
        }
        var el = array[index];
        return el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var val) ? val : null;
    }

    private static string DescribeWmoCode(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 or 3 => "Partly cloudy",
        45 or 48 => "Morning fog/mist",
        51 or 53 or 55 => "Light drizzle",
        61 or 63 or 65 => "Rain",
        80 or 81 or 82 => "Rain showers",
        95 or 96 or 99 => "Thunderstorms",
        _ => "Variable tropical conditions"
    };
}
