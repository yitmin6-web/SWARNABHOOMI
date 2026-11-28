using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Services;

public sealed record AssistantIntelligenceBriefing(
    UserRole Role,
    DateTimeOffset SriLankaNow,
    string WeatherSource,
    string ForecastLocation,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Opportunities,
    WeatherForecastBriefing Weather,
    string FormattedPromptContext);

public interface IAssistantContextIntelligenceService
{
    Task<AssistantIntelligenceBriefing> BuildBriefingAsync(
        Guid userId,
        UserRole role,
        string? userMessage,
        CancellationToken cancellationToken = default);
}

public sealed class AssistantContextIntelligenceService(
    SWARNABHOOMIContext db,
    IAssistantWeatherForecastService weatherService,
    ILogger<AssistantContextIntelligenceService> logger) : IAssistantContextIntelligenceService
{
    private sealed record AgronomicRuleRates(
        string VersionName,
        decimal SeedPaddyKgPerHa,
        decimal UreaKgPerHa,
        decimal TspKgPerHa,
        decimal MopKgPerHa);

    private sealed record PriceReference(
        Guid VarietyId,
        string VarietyName,
        string Market,
        decimal ValuePerKg,
        DateOnly ObservationDate);

    public async Task<AssistantIntelligenceBriefing> BuildBriefingAsync(
        Guid userId,
        UserRole role,
        string? userMessage,
        CancellationToken cancellationToken = default)
    {
        var sltNow = GetSriLankaNow();
        var todaySlt = DateOnly.FromDateTime(sltNow.DateTime);
        var risks = new List<string>();
        var opportunities = new List<string>();
        var sb = new StringBuilder();

        sb.AppendLine($"CURRENT SRI LANKA DATE/TIME (UTC+05:30): {sltNow:yyyy-MM-dd HH:mm} ({sltNow.DayOfWeek}), Today={todaySlt:yyyy-MM-dd}, Tomorrow={todaySlt.AddDays(1):yyyy-MM-dd}");
        sb.AppendLine($"SIGNED-IN USER ROLE: {role}");

        double? weatherLat = null;
        double? weatherLng = null;
        string? weatherDistrict = null;

        try
        {
            var userInfo = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.FirstName, u.LastName, u.District, u.Province })
                .FirstOrDefaultAsync(cancellationToken);

            if (userInfo is not null)
            {
                weatherDistrict = userInfo.District;
                sb.AppendLine($"USER PROFILE: {userInfo.FirstName} {userInfo.LastName} | District={userInfo.District ?? "Unspecified"} | Province={userInfo.Province ?? "Unspecified"}");
            }

            var ruleRates = await LoadActiveResourceRuleAsync(cancellationToken);
            var priceByVariety = await LoadLatestPricesAsync(cancellationToken);

            switch (role)
            {
                case UserRole.Farmer:
                    (weatherLat, weatherLng, weatherDistrict) = await AppendFarmerContextAsync(
                        sb, userId, todaySlt, ruleRates, priceByVariety, risks, opportunities, cancellationToken);
                    break;

                case UserRole.Buyer:
                    var buyerDistrict = await AppendBuyerContextAsync(
                        sb, userId, priceByVariety, risks, opportunities, cancellationToken);
                    weatherDistrict = weatherService.DetectDistrictInText(userMessage) ?? buyerDistrict ?? weatherDistrict;
                    break;

                case UserRole.Officer:
                    var officerDistrict = await AppendOfficerContextAsync(
                        sb, userId, todaySlt, risks, opportunities, cancellationToken);
                    weatherDistrict = weatherService.DetectDistrictInText(userMessage) ?? officerDistrict ?? weatherDistrict;
                    break;

                case UserRole.Government:
                case UserRole.SuperAdmin:
                case UserRole.Developer:
                default:
                    await AppendNationalGovernanceContextAsync(
                        sb, ruleRates, priceByVariety, risks, opportunities, cancellationToken);
                    weatherDistrict = weatherService.DetectDistrictInText(userMessage) ?? weatherDistrict;
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Partial database context failure while building assistant intelligence briefing for user {UserId}.", userId);
            sb.AppendLine("NOTE: Some live database records could not be loaded; rely on available data only.");
        }

        WeatherForecastBriefing weather;
        try
        {
            weather = await weatherService.Get7DayForecastAsync(weatherLat, weatherLng, weatherDistrict, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "7-day weather forecast retrieval failed; building minimal fallback.");
            weather = new WeatherForecastBriefing(
                weatherDistrict ?? "Colombo (Default)",
                6.9271,
                79.8612,
                "Unavailable",
                DateTimeOffset.UtcNow,
                [],
                []);
        }

        AppendWeatherRisksAndOpportunities(role, weather, risks, opportunities);

        sb.AppendLine();
        sb.AppendLine("CALCULATED RISKS:");
        if (risks.Count == 0)
        {
            sb.AppendLine("- No critical near-term risks detected in current records.");
        }
        else
        {
            foreach (var risk in risks.Distinct().Take(6))
            {
                sb.AppendLine($"- {risk}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("CALCULATED OPPORTUNITIES:");
        if (opportunities.Count == 0)
        {
            sb.AppendLine("- Monitor upcoming district procurement windows and weather conditions.");
        }
        else
        {
            foreach (var opp in opportunities.Distinct().Take(6))
            {
                sb.AppendLine($"- {opp}");
            }
        }

        sb.AppendLine();
        AppendWeatherTable(sb, weather);

        return new AssistantIntelligenceBriefing(
            role,
            sltNow,
            weather.Source,
            weather.LocationLabel,
            risks,
            opportunities,
            weather,
            sb.ToString());
    }

    private async Task<(double? Lat, double? Lng, string? District)> AppendFarmerContextAsync(
        StringBuilder sb,
        Guid farmerId,
        DateOnly todaySlt,
        AgronomicRuleRates ruleRates,
        Dictionary<Guid, PriceReference> priceByVariety,
        List<string> risks,
        List<string> opportunities,
        CancellationToken cancellationToken)
    {
        var farms = await db.Farms
            .AsNoTracking()
            .Where(f => f.UserId == farmerId)
            .Select(f => new { f.Id, f.Name, f.District, f.Province, f.AreaHectares })
            .ToListAsync(cancellationToken);

        var fields = await db.Fields
            .AsNoTracking()
            .Where(fld => fld.Farm.UserId == farmerId)
            .Select(fld => new
            {
                fld.Id,
                fld.Name,
                FarmName = fld.Farm.Name,
                fld.Farm.District,
                fld.AreaHectares,
                fld.Latitude,
                fld.Longitude,
                fld.BoundaryGeoJson,
                VerificationStatus = fld.Verifications
                    .OrderByDescending(v => v.CreatedAt)
                    .Select(v => v.Status)
                    .FirstOrDefault() ?? "Pending"
            })
            .ToListAsync(cancellationToken);

        var cultivations = await db.CultivationRecords
            .AsNoTracking()
            .Where(cr => (cr.FarmerId == farmerId || cr.Field.Farm.UserId == farmerId) && cr.Status != "Cancelled")
            .OrderByDescending(cr => cr.PlantingDate)
            .Select(cr => new
            {
                cr.Id,
                cr.FieldId,
                FieldName = cr.Field.Name,
                District = cr.Field.Farm.District,
                cr.RiceVarietyId,
                VarietyName = cr.RiceVariety.Name,
                cr.RiceVariety.MaturityDays,
                AvgYieldKgPerHa = cr.RiceVariety.AverageYieldKgPerHectare ?? 4500m,
                cr.Season,
                cr.PlantingDate,
                cr.ExpectedHarvestDate,
                cr.AreaHectares,
                cr.Status,
                Forecast = cr.HarvestForecasts
                    .OrderByDescending(hf => hf.CreatedAt)
                    .Select(hf => new { hf.PredictedPaddyQuantityKg, hf.ConfidenceCategory, hf.Assumptions })
                    .FirstOrDefault(),
                Harvest = cr.HarvestRecords
                    .OrderByDescending(hr => hr.ActualHarvestDate)
                    .Select(hr => new { hr.ActualHarvestDate, hr.ActualPaddyQuantityKg, hr.Quality })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var farmerIdStr = farmerId.ToString();
        var stocks = await db.StockRecords
            .AsNoTracking()
            .Where(s => s.CreatedBy == farmerIdStr && s.Quantity > 0)
            .Select(s => new
            {
                s.Location,
                s.RiceVarietyId,
                VarietyName = s.RiceVariety.Name,
                s.Grade,
                s.CommodityStage,
                s.Quantity,
                s.AsOfDate
            })
            .ToListAsync(cancellationToken);

        var incomingRequests = await db.PurchaseRequests
            .AsNoTracking()
            .Where(pr => pr.FarmerId == farmerId && (pr.Status == "Pending" || pr.Status == "Accepted"))
            .Select(pr => new
            {
                pr.Status,
                pr.QuantityKg,
                pr.OfferPricePerKg,
                District = pr.DemandItem.Demand.District,
                VarietyId = pr.DemandItem.RiceVarietyId,
                VarietyName = pr.DemandItem.RiceVariety.Name
            })
            .ToListAsync(cancellationToken);

        var farmerDistricts = farms
            .Select(f => f.District)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var openDistrictDemands = await db.DemandItems
            .AsNoTracking()
            .Where(di => (di.Demand.Status == "Open" || di.Demand.Status == "PartiallyFulfilled") &&
                         (farmerDistricts.Count == 0 || farmerDistricts.Contains(di.Demand.District) || di.Demand.District == "National"))
            .Select(di => new
            {
                di.Demand.District,
                di.RiceVarietyId,
                VarietyName = di.RiceVariety.Name,
                di.QuantityTonnes,
                di.OfferPricePerKg
            })
            .Take(15)
            .ToListAsync(cancellationToken);

        sb.AppendLine($"FARMER PORTFOLIO: Farms={farms.Count}, Fields={fields.Count}, TotalMappedFieldHa={fields.Sum(f => f.AreaHectares):F2} ha");
        sb.AppendLine($"ACTIVE DOA INPUT STANDARD ({ruleRates.VersionName}): Seed={ruleRates.SeedPaddyKgPerHa:F0} kg/ha, Urea={ruleRates.UreaKgPerHa:F0} kg/ha, TSP={ruleRates.TspKgPerHa:F0} kg/ha, MOP={ruleRates.MopKgPerHa:F0} kg/ha");

        foreach (var fld in fields.Take(8))
        {
            var seedKg = Math.Round(fld.AreaHectares * ruleRates.SeedPaddyKgPerHa, 1);
            var ureaKg = Math.Round(fld.AreaHectares * ruleRates.UreaKgPerHa, 1);
            var tspKg = Math.Round(fld.AreaHectares * ruleRates.TspKgPerHa, 1);
            var mopKg = Math.Round(fld.AreaHectares * ruleRates.MopKgPerHa, 1);
            var ureaBags = (int)Math.Ceiling(ureaKg / 50m);
            sb.AppendLine($"  * Field '{fld.Name}' ({fld.District ?? "N/A"}): {fld.AreaHectares:F2} ha, Verification={fld.VerificationStatus} | Inputs: Seed={seedKg:F0} kg, Urea={ureaKg:F0} kg ({ureaBags} x50kg bags), TSP={tspKg:F0} kg, MOP={mopKg:F0} kg");

            if (fld.VerificationStatus != "Verified")
            {
                risks.Add($"Field '{fld.Name}' is {fld.VerificationStatus} — forecasted yield takes a {(fld.VerificationStatus == "Rejected" ? "100%" : "10%")} verification confidence discount until verified by your Agrarian Officer.");
            }
        }

        decimal totalProjectedKg = 0m;
        decimal totalEstimatedRevenueLkr = 0m;
        sb.AppendLine("CULTIVATION CYCLES & PREDICTIONS:");
        foreach (var cr in cultivations.Take(10))
        {
            var expectedHarvest = cr.ExpectedHarvestDate ?? cr.PlantingDate.AddDays(cr.MaturityDays);
            var daysSincePlanting = Math.Max(0, todaySlt.DayNumber - cr.PlantingDate.DayNumber);
            var daysToHarvest = expectedHarvest.DayNumber - todaySlt.DayNumber;
            var projectedKg = cr.Forecast?.PredictedPaddyQuantityKg ?? Math.Round(cr.AreaHectares * cr.AvgYieldKgPerHa, 2);

            priceByVariety.TryGetValue(cr.RiceVarietyId, out var priceRef);
            var estRevenue = priceRef is not null ? Math.Round(projectedKg * priceRef.ValuePerKg, 0) : 0m;

            if (cr.Status != "Harvested")
            {
                totalProjectedKg += projectedKg;
                totalEstimatedRevenueLkr += estRevenue;
            }

            var revText = priceRef is not null
                ? $", EstRevenue=LKR {estRevenue:N0} @ LKR {priceRef.ValuePerKg:F2}/kg ({priceRef.Market})"
                : string.Empty;

            var harvestText = cr.Harvest is not null
                ? $" | ActualHarvest={cr.Harvest.ActualPaddyQuantityKg:N0} kg on {cr.Harvest.ActualHarvestDate:yyyy-MM-dd} ({cr.Harvest.Quality})"
                : string.Empty;

            sb.AppendLine($"  * {cr.FieldName} | {cr.VarietyName} ({cr.Season}): Stage={cr.Status}, Planted={cr.PlantingDate:yyyy-MM-dd} ({daysSincePlanting}d ago), ExpectedHarvest={expectedHarvest:yyyy-MM-dd} (in {daysToHarvest}d), Area={cr.AreaHectares:F2} ha, ProjectedYield={projectedKg:N0} kg ({projectedKg / 1000m:F2} MT, Conf={cr.Forecast?.ConfidenceCategory ?? "Est"}){revText}{harvestText}");

            if (cr.Status != "Harvested" && daysToHarvest is >= 0 and <= 14)
            {
                risks.Add($"Field '{cr.FieldName}' ({cr.VarietyName}) reaches harvest maturity in {daysToHarvest} day(s) on {expectedHarvest:yyyy-MM-dd}; ensure dry harvest scheduling and storage readiness.");
            }
        }

        if (totalProjectedKg > 0)
        {
            sb.AppendLine($"TOTAL ACTIVE PROJECTED HARVEST: {totalProjectedKg:N0} kg ({totalProjectedKg / 1000m:F2} MT), Est. Gross Revenue: LKR {totalEstimatedRevenueLkr:N0}");
        }

        if (stocks.Count > 0)
        {
            var totalStockKg = stocks.Sum(s => s.Quantity);
            sb.AppendLine($"FARMER READY STOCK INVENTORY: {totalStockKg:N0} kg ({totalStockKg / 1000m:F2} MT) across {stocks.Count} lot(s):");
            foreach (var st in stocks.Take(5))
            {
                sb.AppendLine($"  * {st.VarietyName} ({st.Grade}, {st.CommodityStage}) in {st.Location}: {st.Quantity:N0} kg as of {st.AsOfDate:yyyy-MM-dd}");
            }
        }

        if (incomingRequests.Count > 0)
        {
            sb.AppendLine($"INCOMING PURCHASE OFFERS ({incomingRequests.Count}):");
            foreach (var pr in incomingRequests.Take(5))
            {
                priceByVariety.TryGetValue(pr.VarietyId, out var cbslRef);
                var cmp = cbslRef is not null ? $" (vs CBSL benchmark LKR {cbslRef.ValuePerKg:F2}/kg)" : string.Empty;
                sb.AppendLine($"  * {pr.Status}: {pr.QuantityKg:N0} kg of {pr.VarietyName} in {pr.District} @ LKR {pr.OfferPricePerKg:F2}/kg{cmp}");

                if (pr.Status == "Pending" && cbslRef is not null && pr.OfferPricePerKg < cbslRef.ValuePerKg * 0.95m)
                {
                    risks.Add($"Pending offer for {pr.VarietyName} (LKR {pr.OfferPricePerKg:F2}/kg) is below the latest {cbslRef.Market} CBSL benchmark (LKR {cbslRef.ValuePerKg:F2}/kg).");
                }
                else if (pr.Status == "Pending")
                {
                    opportunities.Add($"Pending buyer offer for {pr.QuantityKg:N0} kg of {pr.VarietyName} @ LKR {pr.OfferPricePerKg:F2}/kg in {pr.District} is awaiting your review.");
                }
            }
        }

        if (openDistrictDemands.Count > 0)
        {
            var demandByVar = openDistrictDemands
                .GroupBy(d => new { d.District, d.VarietyName, d.RiceVarietyId })
                .Select(g => new
                {
                    g.Key.District,
                    g.Key.VarietyName,
                    VarietyId = g.Key.RiceVarietyId,
                    Tonnes = g.Sum(x => x.QuantityTonnes),
                    MaxOffer = g.Max(x => x.OfferPricePerKg)
                })
                .OrderByDescending(x => x.Tonnes)
                .Take(5)
                .ToList();

            sb.AppendLine("OPEN ANONYMIZED BUYER DEMAND IN YOUR DISTRICTS:");
            foreach (var d in demandByVar)
            {
                sb.AppendLine($"  * {d.District} - {d.VarietyName}: {d.Tonnes:F1} MT open demand{(d.MaxOffer.HasValue ? $", up to LKR {d.MaxOffer.Value:F2}/kg" : string.Empty)}");
                if (stocks.Any(s => s.RiceVarietyId == d.VarietyId))
                {
                    opportunities.Add($"Your stored {d.VarietyName} stock matches {d.Tonnes:F1} MT of open buyer demand in {d.District}.");
                }
            }
        }

        // Resolve centroid from primary field
        double? lat = null;
        double? lng = null;
        string? district = farms.FirstOrDefault()?.District;
        var primaryField = fields.FirstOrDefault();
        if (primaryField is not null)
        {
            district = primaryField.District ?? district;
            if (weatherService.TryComputeGeoJsonCentroid(primaryField.BoundaryGeoJson, out var cLat, out var cLng))
            {
                lat = cLat;
                lng = cLng;
            }
            else if (primaryField.Latitude.HasValue && primaryField.Longitude.HasValue)
            {
                lat = primaryField.Latitude;
                lng = primaryField.Longitude;
            }
        }

        return (lat, lng, district);
    }

    private async Task<string?> AppendBuyerContextAsync(
        StringBuilder sb,
        Guid buyerId,
        Dictionary<Guid, PriceReference> priceByVariety,
        List<string> risks,
        List<string> opportunities,
        CancellationToken cancellationToken)
    {
        var demands = await db.Demands
            .AsNoTracking()
            .Where(d => d.OwnerId == buyerId && d.Status != "Cancelled")
            .Select(d => new
            {
                d.District,
                d.Season,
                d.Status,
                Items = d.DemandItems.Select(di => new
                {
                    VarietyName = di.RiceVariety.Name,
                    di.QuantityTonnes,
                    di.OfferPricePerKg
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        var myPrs = await db.PurchaseRequests
            .AsNoTracking()
            .Where(pr => pr.BuyerId == buyerId)
            .Select(pr => new
            {
                pr.Status,
                pr.QuantityKg,
                pr.OfferPricePerKg,
                District = pr.DemandItem.Demand.District,
                VarietyName = pr.DemandItem.RiceVariety.Name
            })
            .ToListAsync(cancellationToken);

        var stocks = await db.StockRecords
            .AsNoTracking()
            .Where(s => s.Quantity > 0)
            .Select(s => new { s.Location, VarietyName = s.RiceVariety.Name, s.Quantity })
            .ToListAsync(cancellationToken);

        var forecasts = await db.HarvestForecasts
            .AsNoTracking()
            .Where(hf => hf.CultivationRecord.Status != "Cancelled" && hf.CultivationRecord.Status != "Harvested")
            .Select(hf => new
            {
                District = hf.CultivationRecord.Field.Farm.District ?? "National",
                VarietyName = hf.CultivationRecord.RiceVariety.Name,
                hf.PredictedPaddyQuantityKg
            })
            .ToListAsync(cancellationToken);

        var openDemandAll = await db.DemandItems
            .AsNoTracking()
            .Where(di => di.Demand.Status == "Open" || di.Demand.Status == "PartiallyFulfilled")
            .Select(di => new { di.Demand.District, di.QuantityTonnes })
            .ToListAsync(cancellationToken);

        sb.AppendLine($"BUYER PROCUREMENT PORTFOLIO: ActiveDemands={demands.Count}, TotalDemandMT={demands.SelectMany(d => d.Items).Sum(i => i.QuantityTonnes):F1} MT");
        foreach (var d in demands.Take(6))
        {
            var mt = d.Items.Sum(i => i.QuantityTonnes);
            sb.AppendLine($"  * {d.District} ({d.Season}, {d.Status}): {mt:F1} MT across {d.Items.Count} variety line(s)");
        }

        sb.AppendLine($"BUYER PURCHASE REQUESTS: Total={myPrs.Count} (Pending={myPrs.Count(p => p.Status == "Pending")}, Accepted={myPrs.Count(p => p.Status == "Accepted")}, Fulfilled={myPrs.Count(p => p.Status == "Fulfilled")})");
        sb.AppendLine("ANONYMIZED DISTRICT SUPPLY ROLLUPS (NO FARMER PII):");

        var stockByDistrict = stocks.GroupBy(s => s.Location).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity) / 1000m);
        var forecastByDistrict = forecasts.GroupBy(f => f.District).ToDictionary(g => g.Key, g => g.Sum(x => x.PredictedPaddyQuantityKg) / 1000m);
        var demandByDistrict = openDemandAll.GroupBy(d => d.District).ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityTonnes));

        var allDistricts = stockByDistrict.Keys.Union(forecastByDistrict.Keys).Union(demandByDistrict.Keys).Distinct().ToList();
        var rollups = allDistricts.Select(dist =>
        {
            var st = stockByDistrict.GetValueOrDefault(dist);
            var fc = forecastByDistrict.GetValueOrDefault(dist);
            var dm = demandByDistrict.GetValueOrDefault(dist);
            return new { District = dist, StockMt = st, ForecastMt = fc, DemandMt = dm, BalanceMt = st + fc - dm };
        }).OrderByDescending(r => r.StockMt + r.ForecastMt).ToList();

        foreach (var r in rollups.Take(10))
        {
            var tag = r.BalanceMt < -10 ? "Deficit" : (r.BalanceMt > 10 ? "Surplus" : "Balanced");
            sb.AppendLine($"  * {r.District}: ReadyStock={r.StockMt:F1} MT, ProjectedHarvest={r.ForecastMt:F1} MT, OpenDemand={r.DemandMt:F1} MT ({tag}, Net={r.BalanceMt:+0.0;-0.0;0.0} MT)");
        }

        foreach (var deficit in rollups.Where(r => r.BalanceMt < -10).Take(3))
        {
            risks.Add($"District supply deficit in {deficit.District} (Net {deficit.BalanceMt:F1} MT); procurement competition may raise spot prices.");
        }

        foreach (var surplus in rollups.Where(r => r.StockMt > 5 && r.BalanceMt > 10).Take(3))
        {
            opportunities.Add($"Supply surplus in {surplus.District} with {surplus.StockMt:F1} MT ready stock and {surplus.ForecastMt:F1} MT projected harvest available for sourcing.");
        }

        AppendMarketPricesSummary(sb, priceByVariety);
        return demands.FirstOrDefault()?.District;
    }

    private async Task<string?> AppendOfficerContextAsync(
        StringBuilder sb,
        Guid officerId,
        DateOnly todaySlt,
        List<string> risks,
        List<string> opportunities,
        CancellationToken cancellationToken)
    {
        var assignedDistricts = await db.OfficerAssignments
            .AsNoTracking()
            .Where(oa => oa.UserId == officerId && oa.IsActive)
            .Select(oa => oa.District)
            .Distinct()
            .ToListAsync(cancellationToken);

        var fieldsQuery = db.Fields.AsNoTracking();
        if (assignedDistricts.Count > 0)
        {
            fieldsQuery = fieldsQuery.Where(f => f.Farm.District != null && assignedDistricts.Contains(f.Farm.District));
        }

        var fields = await fieldsQuery
            .Select(f => new
            {
                f.Name,
                District = f.Farm.District ?? "Unspecified",
                f.AreaHectares,
                Status = f.Verifications
                    .OrderByDescending(v => v.CreatedAt)
                    .Select(v => v.Status)
                    .FirstOrDefault() ?? "Pending"
            })
            .ToListAsync(cancellationToken);

        var cultQuery = db.CultivationRecords
            .AsNoTracking()
            .Where(cr => cr.Status != "Cancelled" && cr.Status != "Harvested");
        if (assignedDistricts.Count > 0)
        {
            cultQuery = cultQuery.Where(cr => cr.Field.Farm.District != null && assignedDistricts.Contains(cr.Field.Farm.District));
        }

        var activeCults = await cultQuery
            .Select(cr => new
            {
                District = cr.Field.Farm.District ?? "Unspecified",
                FieldName = cr.Field.Name,
                VarietyName = cr.RiceVariety.Name,
                cr.Status,
                cr.ExpectedHarvestDate,
                PredictedKg = cr.HarvestForecasts
                    .OrderByDescending(hf => hf.CreatedAt)
                    .Select(hf => hf.PredictedPaddyQuantityKg)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var pendingCount = fields.Count(f => f.Status == "Pending");
        var verifiedCount = fields.Count(f => f.Status == "Verified");
        var rejectedCount = fields.Count(f => f.Status == "Rejected");
        var totalForecastMt = activeCults.Sum(c => c.PredictedKg) / 1000m;

        sb.AppendLine($"OFFICER JURISDICTION DISTRICTS: {(assignedDistricts.Count > 0 ? string.Join(", ", assignedDistricts) : "All Districts")}");
        sb.AppendLine($"JURISDICTION SUMMARY: Fields={fields.Count} (Verified={verifiedCount}, Pending={pendingCount}, Rejected={rejectedCount}), ActiveCultivations={activeCults.Count}, ProjectedHarvest={totalForecastMt:F1} MT");

        var approaching = activeCults
            .Where(c => c.ExpectedHarvestDate.HasValue &&
                        c.ExpectedHarvestDate.Value >= todaySlt &&
                        c.ExpectedHarvestDate.Value <= todaySlt.AddDays(14))
            .ToList();

        if (pendingCount > 0)
        {
            risks.Add($"{pendingCount} pending field verification(s) in your jurisdiction are reducing forecasted yield confidence by 10% until inspected.");
            opportunities.Add($"Clearing the {pendingCount} pending field verification(s) immediately restores full 1.00x confidence in national rice supply forecasts.");
        }

        if (approaching.Count > 0)
        {
            risks.Add($"{approaching.Count} active field(s) in your jurisdiction reach harvest maturity within 14 days; coordinate wet-weather advisories and storage readiness.");
        }

        return assignedDistricts.FirstOrDefault();
    }

    private async Task AppendNationalGovernanceContextAsync(
        StringBuilder sb,
        AgronomicRuleRates ruleRates,
        Dictionary<Guid, PriceReference> priceByVariety,
        List<string> risks,
        List<string> opportunities,
        CancellationToken cancellationToken)
    {
        var activeFarmers = await db.Users.AsNoTracking().CountAsync(u => u.Role == UserRole.Farmer && u.IsActive, cancellationToken);
        var activeBuyers = await db.Users.AsNoTracking().CountAsync(u => u.Role == UserRole.Buyer && u.IsActive, cancellationToken);
        var activeOfficers = await db.Users.AsNoTracking().CountAsync(u => u.Role == UserRole.Officer && u.IsActive, cancellationToken);

        var fieldStatuses = await db.Fields
            .AsNoTracking()
            .Select(f => new
            {
                District = f.Farm.District ?? "Unspecified",
                Status = f.Verifications
                    .OrderByDescending(v => v.CreatedAt)
                    .Select(v => v.Status)
                    .FirstOrDefault() ?? "Pending"
            })
            .ToListAsync(cancellationToken);

        var forecasts = await db.HarvestForecasts
            .AsNoTracking()
            .Where(hf => hf.CultivationRecord.Status != "Cancelled" && hf.CultivationRecord.Status != "Harvested")
            .Select(hf => new
            {
                District = hf.CultivationRecord.Field.Farm.District ?? "Unspecified",
                hf.PredictedPaddyQuantityKg
            })
            .ToListAsync(cancellationToken);

        var stocks = await db.StockRecords
            .AsNoTracking()
            .Where(s => s.Quantity > 0)
            .Select(s => new { s.Location, s.Quantity })
            .ToListAsync(cancellationToken);

        var demands = await db.DemandItems
            .AsNoTracking()
            .Where(di => di.Demand.Status == "Open" || di.Demand.Status == "PartiallyFulfilled")
            .Select(di => new { di.Demand.District, di.QuantityTonnes })
            .ToListAsync(cancellationToken);

        var totalForecastMt = forecasts.Sum(f => f.PredictedPaddyQuantityKg) / 1000m;
        var totalStockMt = stocks.Sum(s => s.Quantity) / 1000m;
        var totalDemandMt = demands.Sum(d => d.QuantityTonnes);
        var pendingVerifs = fieldStatuses.Count(f => f.Status == "Pending");
        var verifiedFields = fieldStatuses.Count(f => f.Status == "Verified");

        sb.AppendLine($"NATIONAL NETWORK SNAPSHOT: Farmers={activeFarmers}, Buyers={activeBuyers}, Officers={activeOfficers}, Fields={fieldStatuses.Count} (Verified={verifiedFields}, Pending={pendingVerifs})");
        sb.AppendLine($"NATIONAL BALANCE: ProjectedHarvest={totalForecastMt:F1} MT, ReadyStock={totalStockMt:F1} MT, OpenDemand={totalDemandMt:F1} MT, NetBalance={(totalForecastMt + totalStockMt - totalDemandMt):+0.0;-0.0;0.0} MT");
        sb.AppendLine($"ACTIVE DOA STANDARD: {ruleRates.VersionName} (Seed {ruleRates.SeedPaddyKgPerHa:F0}, Urea {ruleRates.UreaKgPerHa:F0}, TSP {ruleRates.TspKgPerHa:F0}, MOP {ruleRates.MopKgPerHa:F0} kg/ha)");

        var fcByDist = forecasts.GroupBy(f => f.District).ToDictionary(g => g.Key, g => g.Sum(x => x.PredictedPaddyQuantityKg) / 1000m);
        var stByDist = stocks.GroupBy(s => s.Location).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity) / 1000m);
        var dmByDist = demands.GroupBy(d => d.District).ToDictionary(g => g.Key, g => g.Sum(x => x.QuantityTonnes));

        var balances = fcByDist.Keys.Union(stByDist.Keys).Union(dmByDist.Keys).Distinct()
            .Select(dist => new
            {
                District = dist,
                SupplyMt = fcByDist.GetValueOrDefault(dist) + stByDist.GetValueOrDefault(dist),
                DemandMt = dmByDist.GetValueOrDefault(dist),
                BalanceMt = fcByDist.GetValueOrDefault(dist) + stByDist.GetValueOrDefault(dist) - dmByDist.GetValueOrDefault(dist)
            })
            .ToList();

        var topSurplus = balances.OrderByDescending(b => b.BalanceMt).Take(5).ToList();
        var topDeficit = balances.OrderBy(b => b.BalanceMt).Where(b => b.BalanceMt < 0).Take(5).ToList();

        sb.AppendLine("TOP SURPLUS DISTRICTS: " + string.Join(", ", topSurplus.Select(s => $"{s.District} (+{s.BalanceMt:F1} MT)")));
        if (topDeficit.Count > 0)
        {
            sb.AppendLine("TOP DEFICIT DISTRICTS: " + string.Join(", ", topDeficit.Select(d => $"{d.District} ({d.BalanceMt:F1} MT)")));
            risks.Add($"Supply deficit detected in {string.Join(", ", topDeficit.Select(d => $"{d.District} ({d.BalanceMt:F1} MT)"))}.");
        }

        if (pendingVerifs > 0)
        {
            risks.Add($"{pendingVerifs} fields nationally await verification (10% forecast confidence penalty applied).");
        }

        if (topSurplus.Count > 0)
        {
            opportunities.Add($"Surplus supply concentrated in {string.Join(", ", topSurplus.Take(3).Select(s => $"{s.District} (+{s.BalanceMt:F1} MT)"))} — prioritize logistics matching to deficit centers.");
        }

        AppendMarketPricesSummary(sb, priceByVariety);
    }

    private async Task<AgronomicRuleRates> LoadActiveResourceRuleAsync(CancellationToken cancellationToken)
    {
        var activeRule = await db.ResourceRuleVersions
            .AsNoTracking()
            .Where(r => r.Status == "Active")
            .OrderByDescending(r => r.Version)
            .Select(r => r.ApplicabilityJson)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(activeRule))
        {
            try
            {
                using var doc = JsonDocument.Parse(activeRule);
                var root = doc.RootElement;
                return new AgronomicRuleRates(
                    root.TryGetProperty("versionName", out var vn) ? vn.GetString() ?? "DOA Standard" : "DOA Standard",
                    root.TryGetProperty("seedPaddyKgPerHectare", out var sd) && sd.TryGetDecimal(out var seed) ? seed : 100m,
                    root.TryGetProperty("ureaKgPerHectare", out var ur) && ur.TryGetDecimal(out var urea) ? urea : 225m,
                    root.TryGetProperty("tspKgPerHectare", out var ts) && ts.TryGetDecimal(out var tsp) ? tsp : 55m,
                    root.TryGetProperty("mopKgPerHectare", out var mp) && mp.TryGetDecimal(out var mop) ? mop : 60m);
            }
            catch (JsonException)
            {
            }
        }

        return new AgronomicRuleRates("DOA Irrigated Paddy Standard", 100m, 225m, 55m, 60m);
    }

    private async Task<Dictionary<Guid, PriceReference>> LoadLatestPricesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.MarketPriceObservations
            .AsNoTracking()
            .OrderByDescending(p => p.ObservationDate)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.RiceVarietyId,
                VarietyName = p.RiceVariety.Name,
                p.Market,
                p.Value,
                p.ObservationDate
            })
            .ToListAsync(cancellationToken);

        var dict = new Dictionary<Guid, PriceReference>();
        foreach (var row in rows)
        {
            if (!dict.ContainsKey(row.RiceVarietyId))
            {
                dict[row.RiceVarietyId] = new PriceReference(
                    row.RiceVarietyId,
                    row.VarietyName,
                    row.Market,
                    row.Value,
                    row.ObservationDate);
            }
        }
        return dict;
    }

    private static void AppendMarketPricesSummary(StringBuilder sb, Dictionary<Guid, PriceReference> priceByVariety)
    {
        if (priceByVariety.Count == 0)
        {
            return;
        }

        sb.AppendLine("LATEST MARKET PRICE BENCHMARKS (LKR/KG):");
        foreach (var p in priceByVariety.Values.Take(8))
        {
            sb.AppendLine($"  * {p.VarietyName}: LKR {p.ValuePerKg:F2}/kg ({p.Market}, {p.ObservationDate:yyyy-MM-dd})");
        }
    }

    private static void AppendWeatherRisksAndOpportunities(
        UserRole role,
        WeatherForecastBriefing weather,
        List<string> risks,
        List<string> opportunities)
    {
        if (weather.Days.Count == 0)
        {
            return;
        }

        var unsafeHarvestDays = weather.Days.Where(d => d.HarvestingSafety.Rating == "UNSAFE").ToList();
        if (unsafeHarvestDays.Count > 0)
        {
            risks.Add($"7-day weather alert in {weather.LocationLabel}: {unsafeHarvestDays.Count} day(s) UNSAFE for harvesting/sun-drying ({string.Join(", ", unsafeHarvestDays.Take(3).Select(d => d.DayLabel))}).");
        }

        var unsafeSprayDays = weather.Days.Where(d => d.SprayingSafety.Rating == "UNSAFE").ToList();
        if (unsafeSprayDays.Count > 0 && role is UserRole.Farmer or UserRole.Officer)
        {
            risks.Add($"Spray wash-off/drift risk on {string.Join(", ", unsafeSprayDays.Take(3).Select(d => d.DayLabel))}.");
        }

        foreach (var window in weather.BestWindows.Take(4))
        {
            if (window.BestRating == "SAFE")
            {
                opportunities.Add($"Best 7-day window for {window.Task}: {window.BestDayLabel} ({window.BestRating} — {window.Summary}).");
            }
        }
    }

    private static void AppendWeatherTable(StringBuilder sb, WeatherForecastBriefing weather)
    {
        sb.AppendLine($"7-DAY WEATHER & TASK-SAFETY TABLE (Location: {weather.LocationLabel} [{weather.Latitude:F4}, {weather.Longitude:F4}] | Source: {weather.Source}):");
        if (weather.Days.Count == 0)
        {
            sb.AppendLine("- Forecast unavailable.");
            return;
        }

        foreach (var d in weather.Days)
        {
            sb.AppendLine(FormattableString.Invariant(
                $"  * {d.DayLabel}: {d.ConditionText}, Temp {d.MinTempC:F1}-{d.MaxTempC:F1}C, RainProb {d.PrecipitationProbabilityPercent}%, Rain {d.PrecipitationMm:F1}mm, StormProb {d.ThunderstormProbabilityPercent}%, RH {d.RelativeHumidityPercent}%, Wind {d.WindSpeedKmh:F1}km/h (Gust {d.WindGustKmh:F1}) | Weeding={d.WeedingSafety.Rating} | Spraying={d.SprayingSafety.Rating} | Fertilizer={d.FertilizingSafety.Rating} | Harvest/Dry={d.HarvestingSafety.Rating}"));
        }

        if (weather.BestWindows.Count > 0)
        {
            sb.AppendLine("BEST RECOMMENDED 7-DAY TASK WINDOWS:");
            foreach (var w in weather.BestWindows)
            {
                sb.AppendLine($"  * {w.Task}: {w.BestDayLabel} [{w.BestRating}] - {w.Summary}");
            }
        }
    }

    private static DateTimeOffset GetSriLankaNow()
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

        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, slt);
    }
}
