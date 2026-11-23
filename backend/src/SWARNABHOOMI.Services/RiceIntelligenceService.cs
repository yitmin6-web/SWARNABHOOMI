using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Services;

public sealed record ResourceRuleDto(
    Guid Id,
    string Category,
    string VersionName,
    decimal SeedPaddyKgPerHectare,
    decimal UreaKgPerHectare,
    decimal TspKgPerHectare,
    decimal MopKgPerHectare,
    string Unit,
    string SourceReference,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    bool IsActive,
    int Version,
    string? ReviewedBy,
    DateTime? ReviewedAt,
    string? ChangeReason,
    string? Notes,
    DateTime CreatedAt);

public sealed record CreateResourceRuleDto(
    string VersionName,
    decimal SeedPaddyKgPerHectare,
    decimal UreaKgPerHectare,
    decimal TspKgPerHectare,
    decimal MopKgPerHectare,
    string? SourceReference = null,
    string? ChangeReason = null,
    string? Notes = null,
    bool IsActive = true);

public sealed record FieldInputRequirementDto(
    Guid FieldId,
    string FieldName,
    Guid FarmId,
    string FarmName,
    Guid FarmerId,
    string FarmerName,
    string? District,
    string? Province,
    decimal AreaHectares,
    string VerificationStatus,
    string? ActiveVarietyName,
    string? ActiveSeason,
    decimal SeedPaddyKg,
    decimal UreaKg,
    int UreaBags,
    decimal TspKg,
    int TspBags,
    decimal MopKg,
    int MopBags,
    int TotalFertilizerBags);

public sealed record FieldHarvestForecastDto(
    Guid ForecastId,
    Guid CultivationId,
    Guid FieldId,
    string FieldName,
    Guid FarmId,
    string FarmName,
    Guid FarmerId,
    string FarmerName,
    string? District,
    string? Province,
    decimal AreaHectares,
    Guid RiceVarietyId,
    string RiceVarietyName,
    string VarietyCode,
    int MaturityDays,
    string Season,
    DateOnly PlantingDate,
    DateOnly ExpectedHarvestDate,
    string HarvestMonth,
    string GrowthStage,
    string VerificationStatus,
    decimal VerificationMultiplier,
    decimal WeatherMultiplier,
    string? WeatherSummary,
    string ConfidenceCategory,
    decimal ForecastedYieldKg,
    decimal ForecastedYieldMt,
    DateTime UpdatedAt);

public sealed record DistrictSupplyRollupDto(
    string District,
    string? Province,
    decimal ForecastedSupplyMt,
    decimal VerifiedSupplyMt,
    decimal PendingSupplyMt,
    decimal ActiveDemandMt,
    decimal SurplusOrDeficitMt,
    string Status,
    decimal CultivatedHectares,
    int FieldsCount,
    decimal AvailableStockMt = 0,
    decimal ActualHarvestedMt = 0);

public sealed record MonthlyHarvestScheduleDto(
    string Month,
    string MonthName,
    decimal ForecastedSupplyMt,
    int FieldsCount,
    IReadOnlyList<string> VarietiesBreakdown);

public sealed record IntelligenceSummaryTotalsDto(
    decimal TotalForecastedMt,
    decimal TotalActiveDemandMt,
    decimal NetSurplusOrDeficitMt,
    decimal TotalCultivatedAreaHectares,
    int TotalFieldsCount,
    decimal VerifiedSharePercentage,
    decimal TotalAvailableStockMt = 0,
    decimal AverageForecastAccuracyPercent = 0);

public sealed record IntelligenceQueryFilter(
    Guid? SeasonId = null,
    string? Season = null,
    string? Province = null,
    string? District = null,
    Guid? VarietyId = null);

public sealed record RiceIntelligenceOverviewDto(
    ResourceRuleDto? ActiveRule,
    IReadOnlyList<FieldInputRequirementDto> FieldInputs,
    IReadOnlyList<FieldHarvestForecastDto> FieldForecasts,
    IReadOnlyList<DistrictSupplyRollupDto> DistrictRollups,
    IReadOnlyList<MonthlyHarvestScheduleDto> MonthlySchedule,
    IntelligenceSummaryTotalsDto SummaryTotals);

public sealed record HarvestForecastDto(
    Guid Id,
    Guid CultivationId,
    DateOnly TargetWindowStart,
    DateOnly TargetWindowEnd,
    decimal PredictedPaddyQuantityKg,
    decimal PredictedPaddyQuantityMt,
    string ConfidenceCategory,
    string? Assumptions,
    DateTime CreatedAt);

public interface IRiceIntelligenceService
{
    Task<IReadOnlyList<ResourceRuleDto>> GetResourceRulesAsync(
        CancellationToken cancellationToken = default);

    Task<ResourceRuleDto?> GetActiveResourceRuleAsync(
        CancellationToken cancellationToken = default);

    Task<ResourceRuleDto> CreateResourceRuleAsync(
        CreateResourceRuleDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<HarvestForecastDto?> UpsertForecastForCultivationAsync(
        Guid cultivationId,
        CancellationToken cancellationToken = default);

    Task<int> RecalculateForecastsForFieldAsync(
        Guid fieldId,
        CancellationToken cancellationToken = default);

    Task<int> RecalculateForecastsAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<RiceIntelligenceOverviewDto> GetIntelligenceOverviewAsync(
        IntelligenceQueryFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);
}

public sealed class RiceIntelligenceService(
    SWARNABHOOMIContext db,
    IGovernanceNotificationService notificationService) : IRiceIntelligenceService
{
    private sealed record RulePayload(
        string? versionName,
        decimal? seedPaddyKgPerHectare,
        decimal? ureaKgPerHectare,
        decimal? tspKgPerHectare,
        decimal? mopKgPerHectare,
        string? notes);

    public async Task<IReadOnlyList<ResourceRuleDto>> GetResourceRulesAsync(
        CancellationToken cancellationToken = default)
    {
        var rules = await db.ResourceRuleVersions
            .AsNoTracking()
            .OrderByDescending(r => r.Status == "Active")
            .ThenByDescending(r => r.Version)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return rules.Select(MapToDto).ToList();
    }

    public async Task<ResourceRuleDto?> GetActiveResourceRuleAsync(
        CancellationToken cancellationToken = default)
    {
        var rule = await db.ResourceRuleVersions
            .AsNoTracking()
            .Where(r => r.Status == "Active")
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return rule != null ? MapToDto(rule) : GetFallbackDefaultRule();
    }

    public async Task<ResourceRuleDto> CreateResourceRuleAsync(
        CreateResourceRuleDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role is not (UserRole.SuperAdmin or UserRole.Developer or UserRole.Government))
        {
            throw new UnauthorizedAccessException("Only SuperAdmin, Developer, and Government officials can publish agronomic resource rules.");
        }

        if (string.IsNullOrWhiteSpace(dto.VersionName))
        {
            throw new ArgumentException("Version name is required.");
        }

        if (dto.SeedPaddyKgPerHectare <= 0 || dto.UreaKgPerHectare <= 0 || dto.TspKgPerHectare <= 0 || dto.MopKgPerHectare <= 0)
        {
            throw new ArgumentException("All input rates per hectare must be positive decimal numbers.");
        }

        if (dto.IsActive)
        {
            var activeRules = await db.ResourceRuleVersions
                .Where(r => r.Status == "Active")
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);

            foreach (var existing in activeRules)
            {
                existing.Status = "Archived";
                existing.EffectiveTo = today;
                existing.UpdatedAt = now;
                existing.UpdatedBy = userId.ToString();
            }
        }

        var maxVersion = await db.ResourceRuleVersions
            .MaxAsync(r => (int?)r.Version, cancellationToken) ?? 0;

        var payload = new RulePayload(
            dto.VersionName.Trim(),
            Math.Round(dto.SeedPaddyKgPerHectare, 2),
            Math.Round(dto.UreaKgPerHectare, 2),
            Math.Round(dto.TspKgPerHectare, 2),
            Math.Round(dto.MopKgPerHectare, 2),
            dto.Notes?.Trim());

        var entity = new ResourceRuleVersion
        {
            Category = "PaddyInputStandard",
            ApplicabilityJson = JsonSerializer.Serialize(payload),
            MinimumValue = 50m,
            MaximumValue = 350m,
            Unit = "kg/ha",
            SourceReference = string.IsNullOrWhiteSpace(dto.SourceReference)
                ? "Department of Agriculture Sri Lanka (DOA)"
                : dto.SourceReference.Trim(),
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            EffectiveTo = null,
            Status = dto.IsActive ? "Active" : "Draft",
            Version = maxVersion + 1,
            ReviewedBy = userId.ToString(),
            ReviewedAt = DateTime.UtcNow,
            ChangeReason = dto.ChangeReason?.Trim() ?? "Agronomic update",
            CreatedBy = userId.ToString()
        };

        db.ResourceRuleVersions.Add(entity);

        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "ResourceRuleCreated",
            EntityType = nameof(ResourceRuleVersion),
            EntityId = entity.Id.ToString(),
            Result = "Success",
            Reason = $"Published resource rule version {entity.Version} ({dto.VersionName})"
        });

        await db.SaveChangesAsync(cancellationToken);

        // Automatically trigger forecast recalculation so all yields and input demands update
        await RecalculateAllActiveForecastsInternalAsync(cancellationToken);

        return MapToDto(entity);
    }

    public async Task<HarvestForecastDto?> UpsertForecastForCultivationAsync(
        Guid cultivationId,
        CancellationToken cancellationToken = default)
    {
        var record = await db.CultivationRecords
            .Include(cr => cr.Field)
                .ThenInclude(f => f.Farm)
            .Include(cr => cr.Field)
                .ThenInclude(f => f.Verifications)
            .Include(cr => cr.RiceVariety)
            .SingleOrDefaultAsync(cr => cr.Id == cultivationId, cancellationToken);

        if (record is null || record.Field is null || record.RiceVariety is null)
        {
            return null;
        }

        var expectedHarvestDate = record.ExpectedHarvestDate ??
            record.PlantingDate.AddDays(record.RiceVariety.MaturityDays);

        // Verification Multiplier: Verified = 1.00, Pending = 0.90, Rejected = 0.00
        var latestVerification = record.Field.Verifications
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault();

        var verifStatus = latestVerification?.Status ?? "Pending";
        var verificationMultiplier = verifStatus.ToLowerInvariant() switch
        {
            "verified" => 1.00m,
            "rejected" => 0.00m,
            _ => 0.90m
        };

        // Weather Multiplier: check latest observation for district
        var weather = await db.WeatherObservations
            .AsNoTracking()
            .Where(w => w.Location == record.Field.Farm.District)
            .OrderByDescending(w => w.ObservationDate)
            .ThenByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        decimal weatherMultiplier = 1.00m;
        string weatherSummary = "Standard weather profile (1.00x)";

        if (weather != null)
        {
            if (weather.RainfallMm > 150m || weather.TemperatureC > 38m || weather.TemperatureC < 18m)
            {
                weatherMultiplier = 0.92m;
                weatherSummary = $"Adverse climate anomaly ({weather.TemperatureC ?? 30:F1}°C, {weather.RainfallMm ?? 0:F0}mm) (-8%)";
            }
            else
            {
                weatherMultiplier = 1.00m;
                weatherSummary = $"Optimal climate condition ({weather.TemperatureC ?? 30:F1}°C, {weather.RainfallMm ?? 0:F0}mm) (1.00x)";
            }
        }

        var baseYieldKgPerHa = record.RiceVariety.AverageYieldKgPerHectare ?? 4500m;
        var predictedYieldKg = Math.Round(record.AreaHectares * baseYieldKgPerHa * verificationMultiplier * weatherMultiplier, 2);

        var confidenceCategory = verificationMultiplier switch
        {
            1.00m when weatherMultiplier == 1.00m => "High",
            0.90m => "Medium",
            _ => "Low"
        };

        var assumptions = $"BaseYield={baseYieldKgPerHa}kg/ha;VerifMult={verificationMultiplier:F2} ({verifStatus});WeatherMult={weatherMultiplier:F2};{weatherSummary}";

        var forecast = await db.HarvestForecasts
            .FirstOrDefaultAsync(hf => hf.CultivationId == record.Id, cancellationToken);

        if (forecast is null)
        {
            forecast = new HarvestForecast
            {
                CultivationId = record.Id,
                RunVersion = "v1.0",
                MethodVersion = "DOA-Yield-Model-v1",
                TargetWindowStart = expectedHarvestDate,
                TargetWindowEnd = expectedHarvestDate.AddDays(14),
                PredictedPaddyQuantityKg = predictedYieldKg,
                ConfidenceCategory = confidenceCategory,
                Assumptions = assumptions,
                CreatedBy = "RiceIntelligenceEngine"
            };
            db.HarvestForecasts.Add(forecast);
        }
        else
        {
            forecast.TargetWindowStart = expectedHarvestDate;
            forecast.TargetWindowEnd = expectedHarvestDate.AddDays(14);
            forecast.PredictedPaddyQuantityKg = predictedYieldKg;
            forecast.ConfidenceCategory = confidenceCategory;
            forecast.Assumptions = assumptions;
            forecast.UpdatedAt = DateTime.UtcNow;
            forecast.UpdatedBy = "RiceIntelligenceEngine";
        }

        await db.SaveChangesAsync(cancellationToken);

        // Check if harvest window is approaching within 14 days and trigger idempotent notification
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var windowEnd = today.AddDays(14);
        if (expectedHarvestDate >= today && expectedHarvestDate <= windowEnd)
        {
            var farmerUserId = record.FarmerId;
            await notificationService.NotifyUpcomingHarvestWindowAsync(
                farmerUserId,
                record.Id,
                record.Field.Name,
                record.RiceVariety.Name,
                expectedHarvestDate.ToDateTime(TimeOnly.MinValue),
                cancellationToken);
        }

        return new HarvestForecastDto(
            forecast.Id,
            forecast.CultivationId,
            forecast.TargetWindowStart,
            forecast.TargetWindowEnd,
            forecast.PredictedPaddyQuantityKg,
            Math.Round(forecast.PredictedPaddyQuantityKg / 1000m, 3),
            forecast.ConfidenceCategory,
            forecast.Assumptions,
            forecast.CreatedAt);
    }

    public async Task<int> RecalculateForecastsForFieldAsync(
        Guid fieldId,
        CancellationToken cancellationToken = default)
    {
        var cultivations = await db.CultivationRecords
            .AsNoTracking()
            .Where(cr => cr.FieldId == fieldId && cr.Status != "Cancelled")
            .Select(cr => cr.Id)
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (var cultivationId in cultivations)
        {
            await UpsertForecastForCultivationAsync(cultivationId, cancellationToken);
            count++;
        }

        return count;
    }

    public async Task<int> RecalculateForecastsAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        IQueryable<CultivationRecord> query = db.CultivationRecords
            .Where(cr => cr.Status != "Cancelled");

        if (role == UserRole.Farmer)
        {
            query = query.Where(cr => cr.FarmerId == userId || cr.Field.Farm.UserId == userId);
        }
        else if (role == UserRole.Officer)
        {
            var assignedDistricts = await db.OfficerAssignments
                .AsNoTracking()
                .Where(oa => oa.UserId == userId && oa.IsActive && oa.District != null)
                .Select(oa => oa.District!)
                .ToListAsync(cancellationToken);

            if (assignedDistricts.Count > 0)
            {
                query = query.Where(cr => cr.Field.Farm.District != null && assignedDistricts.Contains(cr.Field.Farm.District));
            }
        }
        else if (role == UserRole.Buyer)
        {
            return 0;
        }

        var targetIds = await query.Select(cr => cr.Id).ToListAsync(cancellationToken);
        int count = 0;
        foreach (var id in targetIds)
        {
            await UpsertForecastForCultivationAsync(id, cancellationToken);
            count++;
        }

        return count;
    }

    public async Task<RiceIntelligenceOverviewDto> GetIntelligenceOverviewAsync(
        IntelligenceQueryFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var activeRule = await GetActiveResourceRuleAsync(cancellationToken) ?? GetFallbackDefaultRule();

        // 1. Fetch cultivations with related fields, farm, variety, verifications, and forecast
        var cultQuery = db.CultivationRecords
            .Include(cr => cr.Farmer)
            .Include(cr => cr.Field)
                .ThenInclude(f => f.Farm)
                    .ThenInclude(farm => farm.User)
            .Include(cr => cr.Field)
                .ThenInclude(f => f.Verifications)
            .Include(cr => cr.RiceVariety)
            .Include(cr => cr.HarvestForecasts)
            .Where(cr => cr.Status != "Cancelled");

        if (filter.SeasonId.HasValue && filter.SeasonId.Value != Guid.Empty)
        {
            var seasonName = await db.Seasons
                .Where(s => s.Id == filter.SeasonId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrEmpty(seasonName))
            {
                cultQuery = cultQuery.Where(cr => cr.Season == seasonName);
            }
        }
        else if (!string.IsNullOrWhiteSpace(filter.Season))
        {
            cultQuery = cultQuery.Where(cr => cr.Season == filter.Season.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Province))
        {
            cultQuery = cultQuery.Where(cr => cr.Field.Farm.Province == filter.Province.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            cultQuery = cultQuery.Where(cr => cr.Field.Farm.District == filter.District.Trim());
        }

        if (filter.VarietyId.HasValue && filter.VarietyId.Value != Guid.Empty)
        {
            cultQuery = cultQuery.Where(cr => cr.RiceVarietyId == filter.VarietyId.Value);
        }

        var allFilteredCultivations = await cultQuery
            .OrderByDescending(cr => cr.PlantingDate)
            .ToListAsync(cancellationToken);

        // Fetch active demands with items and varieties
        var demandQuery = db.Demands
            .Include(d => d.DemandItems)
                .ThenInclude(di => di.RiceVariety)
            .Where(d => d.Status != "Cancelled" && d.Status != "Draft");

        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            demandQuery = demandQuery.Where(d => d.District == filter.District.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Season))
        {
            demandQuery = demandQuery.Where(d => d.Season == filter.Season.Trim());
        }

        var activeDemands = await demandQuery.ToListAsync(cancellationToken);

        // Fetch available post-harvest stocks
        var stockQuery = db.StockRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            stockQuery = stockQuery.Where(s => s.Location.Contains(filter.District.Trim()));
        }
        if (filter.VarietyId.HasValue && filter.VarietyId.Value != Guid.Empty)
        {
            stockQuery = stockQuery.Where(s => s.RiceVarietyId == filter.VarietyId.Value);
        }
        var allStocks = await stockQuery.ToListAsync(cancellationToken);

        var districtStockMap = allStocks
            .GroupBy(s => s.Location)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));

        // Fetch actual harvests
        var harvestQuery = db.HarvestRecords
            .Include(hr => hr.CultivationRecord)
                .ThenInclude(cr => cr.Field)
                    .ThenInclude(f => f.Farm)
            .Include(hr => hr.CultivationRecord)
                .ThenInclude(cr => cr.HarvestForecasts)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            harvestQuery = harvestQuery.Where(hr => hr.CultivationRecord.Field.Farm.District == filter.District.Trim());
        }
        if (filter.VarietyId.HasValue && filter.VarietyId.Value != Guid.Empty)
        {
            harvestQuery = harvestQuery.Where(hr => hr.CultivationRecord.RiceVarietyId == filter.VarietyId.Value);
        }
        var allHarvests = await harvestQuery.ToListAsync(cancellationToken);

        var districtHarvestMap = allHarvests
            .Where(hr => !string.IsNullOrEmpty(hr.CultivationRecord.Field.Farm.District))
            .GroupBy(hr => hr.CultivationRecord.Field.Farm.District!)
            .ToDictionary(g => g.Key, g => g.Sum(hr => hr.ActualPaddyQuantityKg));

        decimal avgAccuracyPercent = 0;
        var validAccuracyList = allHarvests
            .Select(hr =>
            {
                var f = hr.CultivationRecord.HarvestForecasts.OrderByDescending(fc => fc.CreatedAt).FirstOrDefault();
                var fKg = f?.PredictedPaddyQuantityKg ?? (hr.CultivationRecord.AreaHectares * 4500m);
                return fKg > 0 ? Math.Max(0, 100m - Math.Abs((hr.ActualPaddyQuantityKg - fKg) / fKg * 100m)) : 0;
            })
            .ToList();

        if (validAccuracyList.Count > 0)
        {
            avgAccuracyPercent = Math.Round(validAccuracyList.Average(), 1);
        }

        // 2. Build district rollups (for Buyer, Officer, Admin/Gov/Dev)
        var districtDemandMap = activeDemands
            .GroupBy(d => d.District)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(d => d.DemandItems).Sum(di => di.QuantityTonnes)
            );

        var districtCultGroups = allFilteredCultivations
            .Where(cr => !string.IsNullOrEmpty(cr.Field.Farm.District))
            .GroupBy(cr => new { District = cr.Field.Farm.District!, cr.Field.Farm.Province })
            .ToList();

        var districtRollupList = new List<DistrictSupplyRollupDto>();
        foreach (var group in districtCultGroups)
        {
            decimal forecastedSupplyKg = 0;
            decimal verifiedSupplyKg = 0;
            decimal pendingSupplyKg = 0;
            decimal areaHectares = 0;

            foreach (var cr in group)
            {
                var forecast = cr.HarvestForecasts.OrderByDescending(f => f.CreatedAt).FirstOrDefault();
                var yieldKg = forecast?.PredictedPaddyQuantityKg ??
                    CalculateEstimatedYield(cr.AreaHectares, cr.RiceVariety.AverageYieldKgPerHectare ?? 4500m, cr.Field.Verifications);

                forecastedSupplyKg += yieldKg;
                areaHectares += cr.AreaHectares;

                var verifStatus = cr.Field.Verifications.OrderByDescending(v => v.CreatedAt).FirstOrDefault()?.Status ?? "Pending";
                if (string.Equals(verifStatus, "Verified", StringComparison.OrdinalIgnoreCase))
                {
                    verifiedSupplyKg += yieldKg;
                }
                else
                {
                    pendingSupplyKg += yieldKg;
                }
            }

            var forecastedSupplyMt = Math.Round(forecastedSupplyKg / 1000m, 3);
            var verifiedSupplyMt = Math.Round(verifiedSupplyKg / 1000m, 3);
            var pendingSupplyMt = Math.Round(pendingSupplyKg / 1000m, 3);

            districtDemandMap.TryGetValue(group.Key.District, out var demandMt);
            demandMt = Math.Round(demandMt, 3);

            var surplusOrDeficitMt = Math.Round(forecastedSupplyMt - demandMt, 3);
            var status = surplusOrDeficitMt > 0.5m
                ? "Surplus"
                : surplusOrDeficitMt < -0.5m
                    ? "Deficit"
                    : "Balanced";

            districtStockMap.TryGetValue(group.Key.District, out var stockKg);
            districtHarvestMap.TryGetValue(group.Key.District, out var harvestedKg);
            var availableStockMt = Math.Round(stockKg / 1000m, 3);
            var actualHarvestedMt = Math.Round(harvestedKg / 1000m, 3);

            districtRollupList.Add(new DistrictSupplyRollupDto(
                group.Key.District,
                group.Key.Province,
                forecastedSupplyMt,
                verifiedSupplyMt,
                pendingSupplyMt,
                demandMt,
                surplusOrDeficitMt,
                status,
                Math.Round(areaHectares, 2),
                group.Select(c => c.FieldId).Distinct().Count(),
                availableStockMt,
                actualHarvestedMt
            ));
        }

        districtRollupList = districtRollupList.OrderByDescending(r => r.ForecastedSupplyMt).ToList();

        // 3. Build monthly harvest schedule
        var monthlyGroups = allFilteredCultivations
            .GroupBy(cr =>
            {
                var date = cr.ExpectedHarvestDate ?? cr.PlantingDate.AddDays(cr.RiceVariety.MaturityDays);
                return date.ToString("yyyy-MM");
            })
            .OrderBy(g => g.Key)
            .ToList();

        var monthlyScheduleList = new List<MonthlyHarvestScheduleDto>();
        foreach (var group in monthlyGroups)
        {
            decimal monthSupplyKg = 0;
            var varietySums = new Dictionary<string, decimal>();

            foreach (var cr in group)
            {
                var forecast = cr.HarvestForecasts.OrderByDescending(f => f.CreatedAt).FirstOrDefault();
                var yieldKg = forecast?.PredictedPaddyQuantityKg ??
                    CalculateEstimatedYield(cr.AreaHectares, cr.RiceVariety.AverageYieldKgPerHectare ?? 4500m, cr.Field.Verifications);

                monthSupplyKg += yieldKg;

                var vName = cr.RiceVariety.Name;
                varietySums[vName] = varietySums.GetValueOrDefault(vName) + yieldKg;
            }

            var monthSupplyMt = Math.Round(monthSupplyKg / 1000m, 3);
            var parsedDate = DateTime.TryParse($"{group.Key}-01", out var pDate) ? pDate : DateTime.UtcNow;
            var monthName = parsedDate.ToString("MMMM yyyy");

            var varietyBreakdown = varietySums
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Key}: {Math.Round(kv.Value / 1000m, 1):N1} MT")
                .ToList();

            monthlyScheduleList.Add(new MonthlyHarvestScheduleDto(
                group.Key,
                monthName,
                monthSupplyMt,
                group.Select(c => c.FieldId).Distinct().Count(),
                varietyBreakdown
            ));
        }

        // 4. Role-based scoping of FieldInputs and FieldForecasts
        List<FieldInputRequirementDto> fieldInputs = new();
        List<FieldHarvestForecastDto> fieldForecasts = new();

        if (role == UserRole.Farmer)
        {
            // Farmer sees only their own farms and fields
            var farmerFields = await db.Fields
                .Include(f => f.Farm)
                    .ThenInclude(farm => farm.User)
                .Include(f => f.Verifications)
                .Include(f => f.CultivationRecords)
                    .ThenInclude(cr => cr.RiceVariety)
                .Where(f => f.Farm.UserId == userId)
                .ToListAsync(cancellationToken);

            var farmerCultivations = allFilteredCultivations
                .Where(cr => cr.FarmerId == userId || cr.Field.Farm.UserId == userId)
                .ToList();

            foreach (var field in farmerFields)
            {
                var activeCult = field.CultivationRecords
                    .Where(cr => cr.Status != "Cancelled" && cr.Status != "Harvested")
                    .OrderByDescending(cr => cr.PlantingDate)
                    .FirstOrDefault();

                fieldInputs.Add(ComputeFieldInputs(
                    field,
                    activeRule,
                    activeCult?.RiceVariety.Name,
                    activeCult?.Season));
            }

            foreach (var cr in farmerCultivations)
            {
                fieldForecasts.Add(MapToFieldForecastDto(cr));
            }

            // Summary Totals for Farmer (own metrics)
            var totalFarmerYieldMt = fieldForecasts.Sum(f => f.ForecastedYieldMt);
            var totalFarmerArea = fieldInputs.Sum(f => f.AreaHectares);
            var verifiedFarmerYieldMt = fieldForecasts
                .Where(f => f.VerificationStatus == "Verified")
                .Sum(f => f.ForecastedYieldMt);

            var farmerStockKg = allStocks.Where(s => s.CreatedBy == userId.ToString()).Sum(s => s.Quantity);
            var farmerHarvests = allHarvests.Where(hr => hr.CultivationRecord.FarmerId == userId || hr.CultivationRecord.Field.Farm.UserId == userId).ToList();
            decimal farmerAvgAccuracy = 0;
            if (farmerHarvests.Count > 0)
            {
                var fAccList = farmerHarvests.Select(hr =>
                {
                    var f = hr.CultivationRecord.HarvestForecasts.OrderByDescending(fc => fc.CreatedAt).FirstOrDefault();
                    var fKg = f?.PredictedPaddyQuantityKg ?? (hr.CultivationRecord.AreaHectares * 4500m);
                    return fKg > 0 ? Math.Max(0, 100m - Math.Abs((hr.ActualPaddyQuantityKg - fKg) / fKg * 100m)) : 0;
                }).ToList();
                farmerAvgAccuracy = fAccList.Count > 0 ? Math.Round(fAccList.Average(), 1) : 0;
            }

            var summary = new IntelligenceSummaryTotalsDto(
                TotalForecastedMt: totalFarmerYieldMt,
                TotalActiveDemandMt: 0,
                NetSurplusOrDeficitMt: totalFarmerYieldMt,
                TotalCultivatedAreaHectares: Math.Round(totalFarmerArea, 2),
                TotalFieldsCount: fieldInputs.Count,
                VerifiedSharePercentage: totalFarmerYieldMt > 0
                    ? Math.Round((verifiedFarmerYieldMt / totalFarmerYieldMt) * 100m, 1)
                    : 0,
                TotalAvailableStockMt: Math.Round(farmerStockKg / 1000m, 3),
                AverageForecastAccuracyPercent: farmerAvgAccuracy
            );

            // Hide network-wide district rollups from Farmer
            return new RiceIntelligenceOverviewDto(
                ActiveRule: activeRule,
                FieldInputs: fieldInputs,
                FieldForecasts: fieldForecasts,
                DistrictRollups: Array.Empty<DistrictSupplyRollupDto>(),
                MonthlySchedule: monthlyScheduleList,
                SummaryTotals: summary
            );
        }
        else if (role == UserRole.Buyer)
        {
            // Buyer gets anonymized district-level rollups with ZERO farmer/field identities
            var totalForecastedMt = districtRollupList.Sum(r => r.ForecastedSupplyMt);
            var totalDemandMt = districtRollupList.Sum(r => r.ActiveDemandMt);
            var totalArea = districtRollupList.Sum(r => r.CultivatedHectares);
            var totalFields = districtRollupList.Sum(r => r.FieldsCount);
            var verifiedSupplyMt = districtRollupList.Sum(r => r.VerifiedSupplyMt);

            var summary = new IntelligenceSummaryTotalsDto(
                TotalForecastedMt: totalForecastedMt,
                TotalActiveDemandMt: totalDemandMt,
                NetSurplusOrDeficitMt: Math.Round(totalForecastedMt - totalDemandMt, 3),
                TotalCultivatedAreaHectares: totalArea,
                TotalFieldsCount: totalFields,
                VerifiedSharePercentage: totalForecastedMt > 0
                    ? Math.Round((verifiedSupplyMt / totalForecastedMt) * 100m, 1)
                    : 0,
                TotalAvailableStockMt: Math.Round(allStocks.Sum(s => s.Quantity) / 1000m, 3),
                AverageForecastAccuracyPercent: avgAccuracyPercent
            );

            return new RiceIntelligenceOverviewDto(
                ActiveRule: activeRule,
                FieldInputs: Array.Empty<FieldInputRequirementDto>(),
                FieldForecasts: Array.Empty<FieldHarvestForecastDto>(),
                DistrictRollups: districtRollupList,
                MonthlySchedule: monthlyScheduleList,
                SummaryTotals: summary
            );
        }
        else if (role == UserRole.Officer)
        {
            // Officer sees records for their assigned district(s)
            var assignedDistricts = await db.OfficerAssignments
                .AsNoTracking()
                .Where(oa => oa.UserId == userId && oa.IsActive && oa.District != null)
                .Select(oa => oa.District!)
                .ToListAsync(cancellationToken);

            var scopedCultivations = assignedDistricts.Count > 0
                ? allFilteredCultivations.Where(cr => cr.Field.Farm.District != null && assignedDistricts.Contains(cr.Field.Farm.District)).ToList()
                : allFilteredCultivations;

            foreach (var cr in scopedCultivations)
            {
                fieldInputs.Add(ComputeFieldInputs(
                    cr.Field,
                    activeRule,
                    cr.RiceVariety.Name,
                    cr.Season));

                fieldForecasts.Add(MapToFieldForecastDto(cr));
            }

            var scopedRollups = assignedDistricts.Count > 0
                ? districtRollupList.Where(r => assignedDistricts.Contains(r.District)).ToList()
                : districtRollupList;

            var totalForecastedMt = scopedRollups.Sum(r => r.ForecastedSupplyMt);
            var totalDemandMt = scopedRollups.Sum(r => r.ActiveDemandMt);
            var totalArea = scopedRollups.Sum(r => r.CultivatedHectares);
            var totalFields = scopedRollups.Sum(r => r.FieldsCount);
            var verifiedSupplyMt = scopedRollups.Sum(r => r.VerifiedSupplyMt);

            var summary = new IntelligenceSummaryTotalsDto(
                TotalForecastedMt: totalForecastedMt,
                TotalActiveDemandMt: totalDemandMt,
                NetSurplusOrDeficitMt: Math.Round(totalForecastedMt - totalDemandMt, 3),
                TotalCultivatedAreaHectares: totalArea,
                TotalFieldsCount: totalFields,
                VerifiedSharePercentage: totalForecastedMt > 0
                    ? Math.Round((verifiedSupplyMt / totalForecastedMt) * 100m, 1)
                    : 0,
                TotalAvailableStockMt: Math.Round(allStocks.Sum(s => s.Quantity) / 1000m, 3),
                AverageForecastAccuracyPercent: avgAccuracyPercent
            );

            return new RiceIntelligenceOverviewDto(
                ActiveRule: activeRule,
                FieldInputs: fieldInputs,
                FieldForecasts: fieldForecasts,
                DistrictRollups: scopedRollups,
                MonthlySchedule: monthlyScheduleList,
                SummaryTotals: summary
            );
        }
        else
        {
            // SuperAdmin, Developer, Government: Full National Breakdown
            foreach (var cr in allFilteredCultivations)
            {
                fieldInputs.Add(ComputeFieldInputs(
                    cr.Field,
                    activeRule,
                    cr.RiceVariety.Name,
                    cr.Season));

                fieldForecasts.Add(MapToFieldForecastDto(cr));
            }

            var totalForecastedMt = districtRollupList.Sum(r => r.ForecastedSupplyMt);
            var totalDemandMt = districtRollupList.Sum(r => r.ActiveDemandMt);
            var totalArea = districtRollupList.Sum(r => r.CultivatedHectares);
            var totalFields = districtRollupList.Sum(r => r.FieldsCount);
            var verifiedSupplyMt = districtRollupList.Sum(r => r.VerifiedSupplyMt);

            var summary = new IntelligenceSummaryTotalsDto(
                TotalForecastedMt: totalForecastedMt,
                TotalActiveDemandMt: totalDemandMt,
                NetSurplusOrDeficitMt: Math.Round(totalForecastedMt - totalDemandMt, 3),
                TotalCultivatedAreaHectares: totalArea,
                TotalFieldsCount: totalFields,
                VerifiedSharePercentage: totalForecastedMt > 0
                    ? Math.Round((verifiedSupplyMt / totalForecastedMt) * 100m, 1)
                    : 0,
                TotalAvailableStockMt: Math.Round(allStocks.Sum(s => s.Quantity) / 1000m, 3),
                AverageForecastAccuracyPercent: avgAccuracyPercent
            );

            return new RiceIntelligenceOverviewDto(
                ActiveRule: activeRule,
                FieldInputs: fieldInputs,
                FieldForecasts: fieldForecasts,
                DistrictRollups: districtRollupList,
                MonthlySchedule: monthlyScheduleList,
                SummaryTotals: summary
            );
        }
    }

    private async Task RecalculateAllActiveForecastsInternalAsync(CancellationToken cancellationToken)
    {
        var activeIds = await db.CultivationRecords
            .AsNoTracking()
            .Where(cr => cr.Status != "Cancelled" && cr.Status != "Harvested")
            .Select(cr => cr.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in activeIds)
        {
            await UpsertForecastForCultivationAsync(id, cancellationToken);
        }
    }

    private static decimal CalculateEstimatedYield(
        decimal areaHectares,
        decimal baseYieldPerHa,
        IEnumerable<FieldVerification> verifications)
    {
        var latest = verifications.OrderByDescending(v => v.CreatedAt).FirstOrDefault()?.Status ?? "Pending";
        var mult = latest.ToLowerInvariant() switch
        {
            "verified" => 1.00m,
            "rejected" => 0.00m,
            _ => 0.90m
        };

        return Math.Round(areaHectares * baseYieldPerHa * mult, 2);
    }

    private static FieldInputRequirementDto ComputeFieldInputs(
        Field field,
        ResourceRuleDto rule,
        string? varietyName,
        string? season)
    {
        var seedPaddyKg = Math.Round(field.AreaHectares * rule.SeedPaddyKgPerHectare, 2);
        var ureaKg = Math.Round(field.AreaHectares * rule.UreaKgPerHectare, 2);
        var tspKg = Math.Round(field.AreaHectares * rule.TspKgPerHectare, 2);
        var mopKg = Math.Round(field.AreaHectares * rule.MopKgPerHectare, 2);

        var ureaBags = (int)Math.Ceiling(ureaKg / 50m);
        var tspBags = (int)Math.Ceiling(tspKg / 50m);
        var mopBags = (int)Math.Ceiling(mopKg / 50m);
        var totalBags = ureaBags + tspBags + mopBags;

        var verifStatus = field.Verifications
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault()?.Status ?? "Pending";

        var farmerName = field.Farm.User != null
            ? $"{field.Farm.User.FirstName} {field.Farm.User.LastName}".Trim()
            : "Registered Farmer";

        return new FieldInputRequirementDto(
            field.Id,
            field.Name,
            field.FarmId,
            field.Farm.Name,
            field.Farm.UserId,
            farmerName,
            field.Farm.District,
            field.Farm.Province,
            field.AreaHectares,
            verifStatus,
            varietyName,
            season,
            seedPaddyKg,
            ureaKg,
            ureaBags,
            tspKg,
            tspBags,
            mopKg,
            mopBags,
            totalBags
        );
    }

    private static FieldHarvestForecastDto MapToFieldForecastDto(CultivationRecord cr)
    {
        var forecast = cr.HarvestForecasts.OrderByDescending(f => f.CreatedAt).FirstOrDefault();
        var expectedHarvestDate = cr.ExpectedHarvestDate ?? cr.PlantingDate.AddDays(cr.RiceVariety.MaturityDays);

        var latestVerif = cr.Field.Verifications.OrderByDescending(v => v.CreatedAt).FirstOrDefault();
        var verifStatus = latestVerif?.Status ?? "Pending";
        var verifMult = verifStatus.ToLowerInvariant() switch
        {
            "verified" => 1.00m,
            "rejected" => 0.00m,
            _ => 0.90m
        };

        var farmerName = cr.Farmer != null
            ? $"{cr.Farmer.FirstName} {cr.Farmer.LastName}".Trim()
            : cr.Field.Farm.User != null
                ? $"{cr.Field.Farm.User.FirstName} {cr.Field.Farm.User.LastName}".Trim()
                : "Registered Farmer";

        var yieldKg = forecast?.PredictedPaddyQuantityKg ??
            Math.Round(cr.AreaHectares * (cr.RiceVariety.AverageYieldKgPerHectare ?? 4500m) * verifMult, 2);

        var yieldMt = Math.Round(yieldKg / 1000m, 3);

        return new FieldHarvestForecastDto(
            forecast?.Id ?? Guid.Empty,
            cr.Id,
            cr.FieldId,
            cr.Field.Name,
            cr.Field.FarmId,
            cr.Field.Farm.Name,
            cr.FarmerId,
            farmerName,
            cr.Field.Farm.District,
            cr.Field.Farm.Province,
            cr.AreaHectares,
            cr.RiceVarietyId,
            cr.RiceVariety.Name,
            cr.RiceVariety.VarietyCode,
            cr.RiceVariety.MaturityDays,
            cr.Season,
            cr.PlantingDate,
            expectedHarvestDate,
            expectedHarvestDate.ToString("yyyy-MM"),
            cr.Status,
            verifStatus,
            verifMult,
            1.00m,
            forecast?.Assumptions,
            forecast?.ConfidenceCategory ?? (verifMult == 1.00m ? "High" : "Medium"),
            yieldKg,
            yieldMt,
            forecast?.UpdatedAt ?? forecast?.CreatedAt ?? cr.CreatedAt
        );
    }

    private static ResourceRuleDto MapToDto(ResourceRuleVersion rule)
    {
        var payload = ParsePayload(rule.ApplicabilityJson);
        var versionName = !string.IsNullOrWhiteSpace(payload?.versionName)
            ? payload.versionName
            : $"DOA Standard v{rule.Version}";

        var seedKg = payload?.seedPaddyKgPerHectare ?? 100m;
        var ureaKg = payload?.ureaKgPerHectare ?? 225m;
        var tspKg = payload?.tspKgPerHectare ?? 55m;
        var mopKg = payload?.mopKgPerHectare ?? 60m;

        return new ResourceRuleDto(
            rule.Id,
            rule.Category,
            versionName,
            seedKg,
            ureaKg,
            tspKg,
            mopKg,
            rule.Unit,
            rule.SourceReference,
            rule.EffectiveFrom,
            rule.EffectiveTo,
            rule.Status,
            rule.Status == "Active",
            rule.Version,
            rule.ReviewedBy,
            rule.ReviewedAt,
            rule.ChangeReason,
            payload?.notes,
            rule.CreatedAt
        );
    }

    private static RulePayload? ParsePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<RulePayload>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static ResourceRuleDto GetFallbackDefaultRule()
    {
        return new ResourceRuleDto(
            Guid.Empty,
            "PaddyInputStandard",
            "DOA Irrigated Paddy Standard (2026/2027)",
            100m,
            225m,
            55m,
            60m,
            "kg/ha",
            "Department of Agriculture Sri Lanka (DOA)",
            new DateOnly(2026, 1, 1),
            null,
            "Active",
            true,
            1,
            "DOA Technical Division",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "Initial baseline standard",
            "Standard national fertilizer and seed recommendation by Department of Agriculture Sri Lanka for irrigated lowlands.",
            DateTime.UtcNow
        );
    }
}
