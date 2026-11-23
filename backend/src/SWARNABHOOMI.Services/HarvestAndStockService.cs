using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Services;

public sealed record LogHarvestDto(
    Guid CultivationRecordId,
    decimal ActualHarvestKg,
    DateOnly HarvestDate,
    decimal? MoisturePercentage = null,
    string? QualityGrade = "Grade A",
    decimal? LossesKg = null,
    string? Notes = null,
    bool AddToStock = true,
    Guid? CultivationId = null,
    string? Quality = null)
{
    public Guid ResolvedCultivationId => CultivationRecordId != Guid.Empty ? CultivationRecordId : (CultivationId ?? Guid.Empty);
    public string ResolvedQualityGrade => !string.IsNullOrWhiteSpace(QualityGrade) ? QualityGrade : (Quality ?? "Grade A");
}

public sealed record HarvestRecordDto(
    Guid HarvestId,
    Guid CultivationId,
    Guid FieldId,
    string FieldName,
    Guid FarmId,
    string FarmName,
    Guid FarmerId,
    string FarmerName,
    string? District,
    string? Province,
    Guid RiceVarietyId,
    string RiceVarietyName,
    string VarietyCode,
    string Season,
    DateOnly ActualHarvestDate,
    decimal ActualHarvestKg,
    decimal ActualHarvestMt,
    decimal ForecastedYieldKg,
    decimal ForecastedYieldMt,
    decimal VarianceKg,
    decimal VariancePercent,
    decimal AccuracyPercent,
    string QualityGrade,
    decimal? MoisturePercentage,
    decimal? LossesKg,
    string? Notes,
    string VerificationStatus,
    DateTime CreatedAt);

public sealed record HarvestQueryFilter(
    Guid? SeasonId = null,
    string? Season = null,
    string? Province = null,
    string? District = null,
    Guid? VarietyId = null);

public sealed record UpsertStockDto(
    Guid RiceVarietyId,
    decimal QuantityKg,
    string? District = null,
    string? Province = null,
    string? Grade = "Grade A",
    string? CommodityStage = "Paddy",
    string? Notes = null,
    string? Location = null)
{
    public string ResolvedDistrict => !string.IsNullOrWhiteSpace(District) ? District : (!string.IsNullOrWhiteSpace(Location) ? Location : "National");
}

public sealed record StockRecordDto(
    Guid Id,
    Guid RiceVarietyId,
    string RiceVarietyName,
    string VarietyCode,
    string Location,
    string Grade,
    string CommodityStage,
    decimal QuantityKg,
    decimal QuantityMt,
    string Unit,
    DateOnly AsOfDate,
    string? VerificationStatus,
    string? CreatedBy,
    DateTime CreatedAt);

public sealed record DistrictVarietyStockDto(
    string District,
    string? Province,
    Guid RiceVarietyId,
    string RiceVarietyName,
    string VarietyCode,
    string CommodityStage,
    decimal TotalAvailableKg,
    decimal TotalAvailableMt,
    int LotCount);

public sealed record StockQueryFilter(
    string? Province = null,
    string? District = null,
    Guid? VarietyId = null);

public sealed record StockOverviewDto(
    IReadOnlyList<StockRecordDto> IndividualRecords,
    IReadOnlyList<DistrictVarietyStockDto> AggregatedByDistrictVariety,
    decimal TotalAvailableStockKg,
    decimal TotalAvailableStockMt,
    decimal AverageForecastAccuracyPercent = 0);

public interface IHarvestAndStockService
{
    Task<HarvestRecordDto> LogHarvestAsync(
        LogHarvestDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HarvestRecordDto>> GetHarvestsAsync(
        HarvestQueryFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<StockRecordDto> UpsertStockAsync(
        UpsertStockDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<StockOverviewDto> GetStocksAsync(
        StockQueryFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);
}

public sealed class HarvestAndStockService(SWARNABHOOMIContext db) : IHarvestAndStockService
{
    public async Task<HarvestRecordDto> LogHarvestAsync(
        LogHarvestDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role == UserRole.Buyer)
        {
            throw new UnauthorizedAccessException("Buyers cannot log harvest records.");
        }

        if (dto.ActualHarvestKg <= 0)
        {
            throw new ArgumentException("Actual harvested quantity must be greater than zero kg.");
        }

        if (dto.MoisturePercentage.HasValue && (dto.MoisturePercentage.Value < 0 || dto.MoisturePercentage.Value > 40))
        {
            throw new ArgumentException("Moisture percentage must be between 0% and 40%.");
        }

        var targetId = dto.ResolvedCultivationId;
        var cr = await db.CultivationRecords
            .Include(c => c.Field)
                .ThenInclude(f => f.Farm)
                    .ThenInclude(farm => farm.User)
            .Include(c => c.RiceVariety)
            .Include(c => c.HarvestForecasts)
            .SingleOrDefaultAsync(c => c.Id == targetId, cancellationToken);

        if (cr is null)
        {
            throw new KeyNotFoundException("Cultivation cycle not found.");
        }

        // Ownership verification for Farmer
        if (role == UserRole.Farmer)
        {
            if (cr.Field.Farm.UserId != userId && cr.FarmerId != userId)
            {
                throw new UnauthorizedAccessException("You can only log harvest records for your own cultivated fields.");
            }
        }
        else if (role == UserRole.Officer)
        {
            var assignedDistricts = await db.OfficerAssignments
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsActive && a.District != null)
                .Select(a => a.District!)
                .ToListAsync(cancellationToken);

            if (assignedDistricts.Count > 0 && cr.Field.Farm.District != null && !assignedDistricts.Contains(cr.Field.Farm.District))
            {
                throw new UnauthorizedAccessException("Officers can only log harvests in their assigned district.");
            }
        }

        // Forecast variance engine
        var latestForecast = cr.HarvestForecasts.OrderByDescending(f => f.CreatedAt).FirstOrDefault();
        var forecastedYieldKg = latestForecast != null
            ? latestForecast.PredictedPaddyQuantityKg
            : Math.Round(cr.AreaHectares * (cr.RiceVariety.AverageYieldKgPerHectare ?? 4500m), 2);

        var actualKg = Math.Round(dto.ActualHarvestKg, 2);
        var varianceKg = Math.Round(actualKg - forecastedYieldKg, 2);
        var variancePercent = forecastedYieldKg > 0
            ? Math.Round((varianceKg / forecastedYieldKg) * 100m, 2)
            : 0;

        var accuracyPercent = forecastedYieldKg > 0
            ? Math.Max(0, Math.Round(100m - Math.Abs(variancePercent), 2))
            : 0;

        var qualityGrade = string.IsNullOrWhiteSpace(dto.QualityGrade) ? "Grade A" : dto.QualityGrade.Trim();
        var qualityString = dto.MoisturePercentage.HasValue
            ? $"{qualityGrade} ({dto.MoisturePercentage.Value:F1}% moisture)"
            : qualityGrade;

        var harvestRecord = new HarvestRecord
        {
            CultivationId = cr.Id,
            ActualHarvestDate = dto.HarvestDate,
            ActualPaddyQuantityKg = actualKg,
            Quality = qualityString,
            LossesKg = dto.LossesKg.HasValue ? Math.Round(dto.LossesKg.Value, 2) : null,
            EvidenceReference = dto.Notes?.Trim(),
            VerificationStatus = role is UserRole.Officer or UserRole.SuperAdmin or UserRole.Government ? "Verified" : "Pending",
            CreatedBy = userId.ToString()
        };

        db.HarvestRecords.Add(harvestRecord);

        // Advance cultivation stage to Harvested
        cr.Status = "Harvested";
        cr.UpdatedAt = DateTime.UtcNow;
        cr.UpdatedBy = userId.ToString();

        // If AddToStock is true, increment or create stock record for this farmer
        if (dto.AddToStock)
        {
            var location = cr.Field.Farm.District ?? "National";
            var existingStock = await db.StockRecords
                .FirstOrDefaultAsync(s =>
                    s.CreatedBy == userId.ToString() &&
                    s.RiceVarietyId == cr.RiceVarietyId &&
                    s.Grade == qualityGrade &&
                    s.CommodityStage == "Paddy" &&
                    s.Location == location,
                    cancellationToken);

            if (existingStock != null)
            {
                existingStock.Quantity += actualKg;
                existingStock.AsOfDate = dto.HarvestDate;
                existingStock.UpdatedAt = DateTime.UtcNow;
                existingStock.UpdatedBy = userId.ToString();
            }
            else
            {
                var newStock = new StockRecord
                {
                    Location = location,
                    RiceVarietyId = cr.RiceVarietyId,
                    Grade = qualityGrade,
                    CommodityStage = "Paddy",
                    Quantity = actualKg,
                    Unit = "kg",
                    AsOfDate = dto.HarvestDate,
                    VerificationStatus = harvestRecord.VerificationStatus,
                    CreatedBy = userId.ToString()
                };
                db.StockRecords.Add(newStock);
            }
        }

        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "HarvestRecorded",
            EntityType = nameof(HarvestRecord),
            EntityId = harvestRecord.Id.ToString(),
            Result = "Success",
            Reason = $"Logged actual harvest of {actualKg:N1} kg on field '{cr.Field.Name}' (Forecast: {forecastedYieldKg:N1} kg, Accuracy: {accuracyPercent}%)"
        });

        await db.SaveChangesAsync(cancellationToken);

        var farmerName = cr.Field.Farm.User != null
            ? $"{cr.Field.Farm.User.FirstName} {cr.Field.Farm.User.LastName}".Trim()
            : "Registered Farmer";

        return new HarvestRecordDto(
            harvestRecord.Id,
            cr.Id,
            cr.FieldId,
            cr.Field.Name,
            cr.Field.FarmId,
            cr.Field.Farm.Name,
            cr.FarmerId,
            farmerName,
            cr.Field.Farm.District,
            cr.Field.Farm.Province,
            cr.RiceVarietyId,
            cr.RiceVariety.Name,
            cr.RiceVariety.VarietyCode,
            cr.Season,
            harvestRecord.ActualHarvestDate,
            actualKg,
            Math.Round(actualKg / 1000m, 3),
            forecastedYieldKg,
            Math.Round(forecastedYieldKg / 1000m, 3),
            varianceKg,
            variancePercent,
            accuracyPercent,
            qualityGrade,
            dto.MoisturePercentage,
            dto.LossesKg,
            dto.Notes,
            harvestRecord.VerificationStatus,
            harvestRecord.CreatedAt
        );
    }

    public async Task<IReadOnlyList<HarvestRecordDto>> GetHarvestsAsync(
        HarvestQueryFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role == UserRole.Buyer)
        {
            throw new UnauthorizedAccessException("Buyers do not have access to individual harvest records.");
        }

        var query = db.HarvestRecords
            .Include(hr => hr.CultivationRecord)
                .ThenInclude(cr => cr.Field)
                    .ThenInclude(f => f.Farm)
                        .ThenInclude(farm => farm.User)
            .Include(hr => hr.CultivationRecord)
                .ThenInclude(cr => cr.RiceVariety)
            .Include(hr => hr.CultivationRecord)
                .ThenInclude(cr => cr.HarvestForecasts)
            .AsNoTracking();

        if (role == UserRole.Farmer)
        {
            query = query.Where(hr =>
                hr.CultivationRecord.FarmerId == userId ||
                hr.CultivationRecord.Field.Farm.UserId == userId);
        }
        else if (role == UserRole.Officer)
        {
            var assignedDistricts = await db.OfficerAssignments
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsActive && a.District != null)
                .Select(a => a.District!)
                .ToListAsync(cancellationToken);

            if (assignedDistricts.Count > 0)
            {
                query = query.Where(hr =>
                    hr.CultivationRecord.Field.Farm.District != null &&
                    assignedDistricts.Contains(hr.CultivationRecord.Field.Farm.District));
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Season))
        {
            query = query.Where(hr => hr.CultivationRecord.Season == filter.Season.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Province))
        {
            query = query.Where(hr => hr.CultivationRecord.Field.Farm.Province == filter.Province.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            query = query.Where(hr => hr.CultivationRecord.Field.Farm.District == filter.District.Trim());
        }

        if (filter.VarietyId.HasValue && filter.VarietyId.Value != Guid.Empty)
        {
            query = query.Where(hr => hr.CultivationRecord.RiceVarietyId == filter.VarietyId.Value);
        }

        var records = await query
            .OrderByDescending(hr => hr.ActualHarvestDate)
            .ThenByDescending(hr => hr.CreatedAt)
            .ToListAsync(cancellationToken);

        return records.Select(hr =>
        {
            var cr = hr.CultivationRecord;
            var latestForecast = cr.HarvestForecasts.OrderByDescending(f => f.CreatedAt).FirstOrDefault();
            var forecastedYieldKg = latestForecast != null
                ? latestForecast.PredictedPaddyQuantityKg
                : Math.Round(cr.AreaHectares * (cr.RiceVariety.AverageYieldKgPerHectare ?? 4500m), 2);

            var actualKg = hr.ActualPaddyQuantityKg;
            var varianceKg = Math.Round(actualKg - forecastedYieldKg, 2);
            var variancePercent = forecastedYieldKg > 0
                ? Math.Round((varianceKg / forecastedYieldKg) * 100m, 2)
                : 0;

            var accuracyPercent = forecastedYieldKg > 0
                ? Math.Max(0, Math.Round(100m - Math.Abs(variancePercent), 2))
                : 0;

            var farmerName = cr.Field.Farm.User != null
                ? $"{cr.Field.Farm.User.FirstName} {cr.Field.Farm.User.LastName}".Trim()
                : "Registered Farmer";

            // Extract grade from quality string if present
            var qualityGrade = hr.Quality ?? "Standard";

            return new HarvestRecordDto(
                hr.Id,
                cr.Id,
                cr.FieldId,
                cr.Field.Name,
                cr.Field.FarmId,
                cr.Field.Farm.Name,
                cr.FarmerId,
                farmerName,
                cr.Field.Farm.District,
                cr.Field.Farm.Province,
                cr.RiceVarietyId,
                cr.RiceVariety.Name,
                cr.RiceVariety.VarietyCode,
                cr.Season,
                hr.ActualHarvestDate,
                actualKg,
                Math.Round(actualKg / 1000m, 3),
                forecastedYieldKg,
                Math.Round(forecastedYieldKg / 1000m, 3),
                varianceKg,
                variancePercent,
                accuracyPercent,
                qualityGrade,
                null,
                hr.LossesKg,
                hr.EvidenceReference,
                hr.VerificationStatus,
                hr.CreatedAt
            );
        }).ToList();
    }

    public async Task<StockRecordDto> UpsertStockAsync(
        UpsertStockDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role == UserRole.Buyer)
        {
            throw new UnauthorizedAccessException("Buyers cannot create or update stock records.");
        }

        if (dto.QuantityKg < 0)
        {
            throw new ArgumentException("Quantity cannot be negative.");
        }

        var variety = await db.RiceVarieties
            .AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == dto.RiceVarietyId, cancellationToken);

        if (variety is null)
        {
            throw new KeyNotFoundException("Rice variety not found.");
        }

        var grade = string.IsNullOrWhiteSpace(dto.Grade) ? "Grade A" : dto.Grade.Trim();
        var commodityStage = string.IsNullOrWhiteSpace(dto.CommodityStage) ? "Paddy" : dto.CommodityStage.Trim();
        var location = dto.ResolvedDistrict;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Find existing record for this user, variety, grade, stage, and location
        var existing = await db.StockRecords
            .FirstOrDefaultAsync(s =>
                s.CreatedBy == userId.ToString() &&
                s.RiceVarietyId == dto.RiceVarietyId &&
                s.Grade == grade &&
                s.CommodityStage == commodityStage &&
                s.Location == location,
                cancellationToken);

        if (existing != null)
        {
            existing.Quantity = Math.Round(dto.QuantityKg, 2);
            existing.AsOfDate = today;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = userId.ToString();
        }
        else
        {
            existing = new StockRecord
            {
                Location = location,
                RiceVarietyId = dto.RiceVarietyId,
                Grade = grade,
                CommodityStage = commodityStage,
                Quantity = Math.Round(dto.QuantityKg, 2),
                Unit = "kg",
                AsOfDate = today,
                VerificationStatus = role is UserRole.Officer or UserRole.SuperAdmin or UserRole.Government ? "Verified" : "SelfReported",
                CreatedBy = userId.ToString()
            };
            db.StockRecords.Add(existing);
        }

        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "StockUpdated",
            EntityType = nameof(StockRecord),
            EntityId = existing.Id.ToString(),
            Result = "Success",
            Reason = $"Updated {commodityStage} stock of '{variety.Name}' to {dto.QuantityKg:N1} kg in {location}"
        });

        await db.SaveChangesAsync(cancellationToken);

        return new StockRecordDto(
            existing.Id,
            variety.Id,
            variety.Name,
            variety.VarietyCode,
            existing.Location,
            existing.Grade,
            existing.CommodityStage,
            existing.Quantity,
            Math.Round(existing.Quantity / 1000m, 3),
            existing.Unit,
            existing.AsOfDate,
            existing.VerificationStatus,
            existing.CreatedBy,
            existing.CreatedAt
        );
    }

    public async Task<StockOverviewDto> GetStocksAsync(
        StockQueryFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var stockQuery = db.StockRecords
            .Include(s => s.RiceVariety)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            stockQuery = stockQuery.Where(s => s.Location.Contains(filter.District.Trim()));
        }

        if (filter.VarietyId.HasValue && filter.VarietyId.Value != Guid.Empty)
        {
            stockQuery = stockQuery.Where(s => s.RiceVarietyId == filter.VarietyId.Value);
        }

        var allStocks = await stockQuery.ToListAsync(cancellationToken);

        // Compute average forecast accuracy across all actual harvests
        var accuracyStats = await db.HarvestRecords
            .Include(hr => hr.CultivationRecord)
                .ThenInclude(cr => cr.HarvestForecasts)
            .Include(hr => hr.CultivationRecord)
                .ThenInclude(cr => cr.RiceVariety)
            .AsNoTracking()
            .Select(hr => new
            {
                ActualKg = hr.ActualPaddyQuantityKg,
                ForecastKg = hr.CultivationRecord.HarvestForecasts.OrderByDescending(f => f.CreatedAt).Select(f => f.PredictedPaddyQuantityKg).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        decimal avgAccuracy = 0;
        if (accuracyStats.Count > 0)
        {
            var validAccuracies = accuracyStats
                .Where(a => a.ForecastKg > 0)
                .Select(a => Math.Max(0, 100m - Math.Abs((a.ActualKg - a.ForecastKg) / a.ForecastKg * 100m)))
                .ToList();

            if (validAccuracies.Count > 0)
            {
                avgAccuracy = Math.Round(validAccuracies.Average(), 1);
            }
        }

        // Build AggregatedByDistrictVariety
        var aggregated = allStocks
            .GroupBy(s => new { s.Location, s.RiceVarietyId, s.RiceVariety.Name, s.RiceVariety.VarietyCode, s.CommodityStage })
            .Select(g => new DistrictVarietyStockDto(
                District: g.Key.Location,
                Province: null,
                RiceVarietyId: g.Key.RiceVarietyId,
                RiceVarietyName: g.Key.Name,
                VarietyCode: g.Key.VarietyCode,
                CommodityStage: g.Key.CommodityStage,
                TotalAvailableKg: g.Sum(s => s.Quantity),
                TotalAvailableMt: Math.Round(g.Sum(s => s.Quantity) / 1000m, 3),
                LotCount: g.Count()
            ))
            .OrderByDescending(a => a.TotalAvailableMt)
            .ToList();

        if (role == UserRole.Buyer)
        {
            // Buyer gets strictly anonymized aggregated stock with ZERO individual records
            var totalKg = aggregated.Sum(a => a.TotalAvailableKg);
            return new StockOverviewDto(
                IndividualRecords: Array.Empty<StockRecordDto>(),
                AggregatedByDistrictVariety: aggregated,
                TotalAvailableStockKg: totalKg,
                TotalAvailableStockMt: Math.Round(totalKg / 1000m, 3),
                AverageForecastAccuracyPercent: avgAccuracy
            );
        }

        if (role == UserRole.Farmer)
        {
            // Farmer sees only their own stock records
            var farmerStocks = allStocks
                .Where(s => s.CreatedBy == userId.ToString())
                .Select(s => new StockRecordDto(
                    s.Id,
                    s.RiceVarietyId,
                    s.RiceVariety.Name,
                    s.RiceVariety.VarietyCode,
                    s.Location,
                    s.Grade,
                    s.CommodityStage,
                    s.Quantity,
                    Math.Round(s.Quantity / 1000m, 3),
                    s.Unit,
                    s.AsOfDate,
                    s.VerificationStatus,
                    s.CreatedBy,
                    s.CreatedAt
                ))
                .OrderByDescending(s => s.AsOfDate)
                .ToList();

            var farmerAggregated = farmerStocks
                .GroupBy(s => new { s.Location, s.RiceVarietyId, s.RiceVarietyName, s.VarietyCode, s.CommodityStage })
                .Select(g => new DistrictVarietyStockDto(
                    District: g.Key.Location,
                    Province: null,
                    RiceVarietyId: g.Key.RiceVarietyId,
                    RiceVarietyName: g.Key.RiceVarietyName,
                    VarietyCode: g.Key.VarietyCode,
                    CommodityStage: g.Key.CommodityStage,
                    TotalAvailableKg: g.Sum(s => s.QuantityKg),
                    TotalAvailableMt: Math.Round(g.Sum(s => s.QuantityKg) / 1000m, 3),
                    LotCount: g.Count()
                ))
                .ToList();

            var farmerTotalKg = farmerStocks.Sum(s => s.QuantityKg);

            return new StockOverviewDto(
                IndividualRecords: farmerStocks,
                AggregatedByDistrictVariety: farmerAggregated,
                TotalAvailableStockKg: farmerTotalKg,
                TotalAvailableStockMt: Math.Round(farmerTotalKg / 1000m, 3),
                AverageForecastAccuracyPercent: avgAccuracy
            );
        }

        if (role == UserRole.Officer)
        {
            var assignedDistricts = await db.OfficerAssignments
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsActive && a.District != null)
                .Select(a => a.District!)
                .ToListAsync(cancellationToken);

            var scopedStocks = assignedDistricts.Count > 0
                ? allStocks.Where(s => assignedDistricts.Any(d => s.Location.Contains(d))).ToList()
                : allStocks;

            var officerIndividual = scopedStocks.Select(s => new StockRecordDto(
                s.Id,
                s.RiceVarietyId,
                s.RiceVariety.Name,
                s.RiceVariety.VarietyCode,
                s.Location,
                s.Grade,
                s.CommodityStage,
                s.Quantity,
                Math.Round(s.Quantity / 1000m, 3),
                s.Unit,
                s.AsOfDate,
                s.VerificationStatus,
                s.CreatedBy,
                s.CreatedAt
            )).ToList();

            var totalKg = scopedStocks.Sum(s => s.Quantity);

            return new StockOverviewDto(
                IndividualRecords: officerIndividual,
                AggregatedByDistrictVariety: aggregated.Where(a => assignedDistricts.Count == 0 || assignedDistricts.Contains(a.District)).ToList(),
                TotalAvailableStockKg: totalKg,
                TotalAvailableStockMt: Math.Round(totalKg / 1000m, 3),
                AverageForecastAccuracyPercent: avgAccuracy
            );
        }

        // SuperAdmin, Developer, Government
        var allIndividual = allStocks.Select(s => new StockRecordDto(
            s.Id,
            s.RiceVarietyId,
            s.RiceVariety.Name,
            s.RiceVariety.VarietyCode,
            s.Location,
            s.Grade,
            s.CommodityStage,
            s.Quantity,
            Math.Round(s.Quantity / 1000m, 3),
            s.Unit,
            s.AsOfDate,
            s.VerificationStatus,
            s.CreatedBy,
            s.CreatedAt
        )).ToList();

        var totalNationalKg = allStocks.Sum(s => s.Quantity);

        return new StockOverviewDto(
            IndividualRecords: allIndividual,
            AggregatedByDistrictVariety: aggregated,
            TotalAvailableStockKg: totalNationalKg,
            TotalAvailableStockMt: Math.Round(totalNationalKg / 1000m, 3),
            AverageForecastAccuracyPercent: avgAccuracy
        );
    }
}
