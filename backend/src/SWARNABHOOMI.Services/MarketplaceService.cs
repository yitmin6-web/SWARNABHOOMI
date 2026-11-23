using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Services;

public sealed record DemandItemDto(
    Guid Id,
    Guid DemandId,
    Guid RiceVarietyId,
    string RiceVarietyName,
    string VarietyCode,
    string Grade,
    decimal QuantityTonnes,
    decimal QuantityKg,
    decimal? OfferPricePerKg,
    string? Notes);

public sealed record DemandDto(
    Guid Id,
    Guid OwnerId,
    string OwnerName,
    string? OwnerBusinessName,
    string District,
    string Season,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    string? Notes,
    decimal TotalQuantityTonnes,
    decimal TotalQuantityKg,
    int ItemsCount,
    int PurchaseRequestsCount,
    IReadOnlyList<DemandItemDto> DemandItems,
    DateTime CreatedAt);

public sealed record CreateDemandItemDto(
    Guid RiceVarietyId,
    decimal QuantityKg,
    decimal? QuantityTonnes = null,
    string? Grade = "Grade A",
    decimal? OfferPricePerKg = null,
    string? Notes = null);

public sealed record CreateDemandDto(
    string District,
    string Season,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<CreateDemandItemDto> Items,
    string? Notes = null);

public sealed record UpdateDemandStatusDto(
    string Status,
    string? Notes = null);

public sealed record DemandQueryFilter(
    string? District = null,
    string? Season = null,
    string? Status = null,
    Guid? VarietyId = null);

public sealed record MarketplaceSupplyItemDto(
    string District,
    string? Province,
    Guid RiceVarietyId,
    string RiceVarietyName,
    string VarietyCode,
    decimal AvailableStockKg,
    decimal AvailableStockMt,
    decimal ForecastedSupplyMt,
    decimal? LatestCbslPricePerKg,
    int ActiveLotCount,
    Guid? MatchingDemandItemId,
    Guid? SuggestedFarmerId);

public sealed record DistrictSupplyGapDto(
    string District,
    string? Province,
    decimal TotalDemandMt,
    decimal ForecastedSupplyMt,
    decimal AvailableStockMt,
    decimal SupplyGapMt,
    string GapStatus); // "Surplus", "Deficit", "Balanced"

public sealed record MarketplaceSupplyOverviewDto(
    IReadOnlyList<MarketplaceSupplyItemDto> SupplyListings,
    IReadOnlyList<DistrictSupplyGapDto> RegionalGaps,
    decimal TotalOpenDemandMt,
    decimal TotalForecastedSupplyMt,
    decimal TotalAvailableStockMt,
    int ActivePurchaseRequestsCount);

public sealed record MarketplaceSupplyFilter(
    string? Province = null,
    string? District = null,
    Guid? VarietyId = null);

public sealed record PurchaseRequestDto(
    Guid Id,
    Guid BuyerId,
    string BuyerName,
    string? BuyerBusinessName,
    Guid? FarmerId,
    string FarmerName,
    string? FarmerPhone,
    string? FarmerEmail,
    bool IsFarmerContactRevealed,
    Guid DemandItemId,
    Guid? DemandId,
    string DemandSeason,
    string District,
    Guid RiceVarietyId,
    string RiceVarietyName,
    string VarietyCode,
    decimal QuantityKg,
    decimal QuantityMt,
    decimal OfferPricePerKg,
    decimal TotalOrderValueLkr,
    string Status,
    string? Terms,
    string? DeliveryTerms,
    DateTime CreatedAt);

public sealed record CreatePurchaseRequestDto(
    Guid DemandItemId,
    decimal QuantityKg,
    decimal OfferPricePerKg,
    Guid? FarmerId = null,
    string? Terms = null,
    string? DeliveryTerms = null);

public sealed record UpdatePurchaseRequestStatusDto(
    string Status,
    string? Notes = null);

public sealed record PurchaseRequestFilter(
    string? Status = null,
    string? District = null,
    Guid? VarietyId = null);

public interface IMarketplaceService
{
    Task<IReadOnlyList<DemandDto>> GetDemandsAsync(
        DemandQueryFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<DemandDto> CreateDemandAsync(
        CreateDemandDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<DemandDto> UpdateDemandStatusAsync(
        Guid demandId,
        UpdateDemandStatusDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<MarketplaceSupplyOverviewDto> GetMarketplaceSupplyAsync(
        MarketplaceSupplyFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseRequestDto>> GetPurchaseRequestsAsync(
        PurchaseRequestFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<PurchaseRequestDto> CreatePurchaseRequestAsync(
        CreatePurchaseRequestDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<PurchaseRequestDto> UpdatePurchaseRequestStatusAsync(
        Guid requestId,
        UpdatePurchaseRequestStatusDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceService(
    SWARNABHOOMIContext db,
    IGovernanceNotificationService notificationService) : IMarketplaceService
{
    public async Task<IReadOnlyList<DemandDto>> GetDemandsAsync(
        DemandQueryFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var query = db.Demands
            .AsNoTracking()
            .Include(d => d.Owner)
                .ThenInclude(u => u.BuyerProfile)
            .Include(d => d.DemandItems)
                .ThenInclude(di => di.RiceVariety)
            .AsQueryable();

        // 1. Role-based scoping
        if (role == UserRole.Buyer)
        {
            // Buyer sees own demands with full details
            query = query.Where(d => d.OwnerId == userId);
        }
        else if (role == UserRole.Farmer)
        {
            // Farmers see open/active market demands
            query = query.Where(d => d.Status == "Open" || d.Status == "Submitted" || d.Status == "PartiallyFulfilled");
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
                query = query.Where(d => assignedDistricts.Contains(d.District) || d.District == "National");
            }
        }

        // 2. Query filters
        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            query = query.Where(d => d.District == filter.District.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Season))
        {
            query = query.Where(d => d.Season == filter.Season.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(d => d.Status.ToLower() == filter.Status.Trim().ToLower());
        }

        if (filter.VarietyId.HasValue && filter.VarietyId.Value != Guid.Empty)
        {
            query = query.Where(d => d.DemandItems.Any(di => di.RiceVarietyId == filter.VarietyId.Value));
        }

        var demands = await query
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        // Fetch purchase request counts grouped by DemandItemId (in memory for SQLite compatibility)
        var prCounts = await db.PurchaseRequests
            .AsNoTracking()
            .Select(pr => pr.DemandItemId)
            .ToListAsync(cancellationToken);

        var prCountMap = prCounts
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        return demands.Select(d =>
        {
            var items = d.DemandItems.Select(di =>
            {
                var tonnes = di.QuantityTonnes;
                var kg = Math.Round(tonnes * 1000m, 2);
                return new DemandItemDto(
                    di.Id,
                    di.DemandId,
                    di.RiceVarietyId,
                    di.RiceVariety?.Name ?? "Rice Variety",
                    di.RiceVariety?.VarietyCode ?? "—",
                    di.Grade ?? "Grade A",
                    tonnes,
                    kg,
                    di.OfferPricePerKg,
                    role == UserRole.Farmer ? null : di.Notes
                );
            }).ToList();

            var totalTonnes = items.Sum(i => i.QuantityTonnes);
            var totalKg = items.Sum(i => i.QuantityKg);
            var totalPrs = d.DemandItems.Sum(di => prCountMap.GetValueOrDefault(di.Id, 0));

            var buyerName = role == UserRole.Farmer
                ? "Registered Buyer"
                : $"{d.Owner.FirstName} {d.Owner.LastName}".Trim();

            var buyerBusinessName = role == UserRole.Farmer
                ? (d.Owner.BuyerProfile?.BusinessName ?? "Commercial Buyer")
                : (d.Owner.BuyerProfile?.BusinessName ?? $"{d.Owner.FirstName} {d.Owner.LastName}".Trim());

            return new DemandDto(
                d.Id,
                d.OwnerId,
                buyerName,
                buyerBusinessName,
                d.District,
                d.Season,
                d.PeriodStart,
                d.PeriodEnd,
                d.Status,
                role == UserRole.Farmer ? null : d.Notes,
                totalTonnes,
                totalKg,
                items.Count,
                totalPrs,
                items,
                d.CreatedAt
            );
        }).ToList();
    }

    public async Task<DemandDto> CreateDemandAsync(
        CreateDemandDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role is not (UserRole.Buyer or UserRole.SuperAdmin or UserRole.Developer))
        {
            throw new UnauthorizedAccessException("Only buyers and administrators can post procurement demands.");
        }

        if (string.IsNullOrWhiteSpace(dto.District))
        {
            throw new ArgumentException("Provide a target procurement district or 'National'.");
        }

        if (string.IsNullOrWhiteSpace(dto.Season))
        {
            throw new ArgumentException("Specify a target cultivation season (e.g. Yala 2027).");
        }

        if (dto.PeriodEnd < dto.PeriodStart)
        {
            throw new ArgumentException("Period end date must be on or after start date.");
        }

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ArgumentException("At least one rice variety demand line item is required.");
        }

        var demand = new Demand
        {
            OwnerId = userId,
            District = dto.District.Trim(),
            Season = dto.Season.Trim(),
            PeriodStart = dto.PeriodStart,
            PeriodEnd = dto.PeriodEnd,
            Status = "Open",
            Notes = dto.Notes?.Trim(),
            CreatedBy = userId.ToString()
        };

        foreach (var item in dto.Items)
        {
            var variety = await db.RiceVarieties
                .AsNoTracking()
                .SingleOrDefaultAsync(v => v.Id == item.RiceVarietyId, cancellationToken);

            if (variety == null)
            {
                throw new ArgumentException($"Rice variety {item.RiceVarietyId} not found.");
            }

            var tonnes = item.QuantityTonnes.HasValue && item.QuantityTonnes.Value > 0
                ? item.QuantityTonnes.Value
                : Math.Round(item.QuantityKg / 1000m, 3);

            if (tonnes <= 0)
            {
                throw new ArgumentException("Quantity must be greater than zero.");
            }

            demand.DemandItems.Add(new DemandItem
            {
                RiceVarietyId = item.RiceVarietyId,
                Grade = string.IsNullOrWhiteSpace(item.Grade) ? "Grade A" : item.Grade.Trim(),
                QuantityTonnes = Math.Round(tonnes, 3),
                OfferPricePerKg = item.OfferPricePerKg.HasValue && item.OfferPricePerKg.Value > 0
                    ? Math.Round(item.OfferPricePerKg.Value, 2)
                    : null,
                Notes = item.Notes?.Trim(),
                CreatedBy = userId.ToString()
            });
        }

        db.Demands.Add(demand);

        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "DemandCreated",
            EntityType = nameof(Demand),
            EntityId = demand.Id.ToString(),
            Result = "Success",
            Reason = $"Created procurement demand in '{demand.District}' for season '{demand.Season}' with {demand.DemandItems.Count} item line(s)"
        });

        await db.SaveChangesAsync(cancellationToken);

        var owner = await db.Users
            .AsNoTracking()
            .Include(u => u.BuyerProfile)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        var ownerName = owner != null ? $"{owner.FirstName} {owner.LastName}".Trim() : "Buyer";
        var ownerBusiness = owner?.BuyerProfile?.BusinessName ?? ownerName;

        var itemsDto = demand.DemandItems.Select(di =>
        {
            var v = db.RiceVarieties.Find(di.RiceVarietyId);
            return new DemandItemDto(
                di.Id,
                demand.Id,
                di.RiceVarietyId,
                v?.Name ?? "Variety",
                v?.VarietyCode ?? "—",
                di.Grade,
                di.QuantityTonnes,
                Math.Round(di.QuantityTonnes * 1000m, 2),
                di.OfferPricePerKg,
                di.Notes
            );
        }).ToList();

        return new DemandDto(
            demand.Id,
            demand.OwnerId,
            ownerName,
            ownerBusiness,
            demand.District,
            demand.Season,
            demand.PeriodStart,
            demand.PeriodEnd,
            demand.Status,
            demand.Notes,
            itemsDto.Sum(i => i.QuantityTonnes),
            itemsDto.Sum(i => i.QuantityKg),
            itemsDto.Count,
            0,
            itemsDto,
            demand.CreatedAt
        );
    }

    public async Task<DemandDto> UpdateDemandStatusAsync(
        Guid demandId,
        UpdateDemandStatusDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var demand = await db.Demands
            .Include(d => d.Owner)
                .ThenInclude(u => u.BuyerProfile)
            .Include(d => d.DemandItems)
                .ThenInclude(di => di.RiceVariety)
            .SingleOrDefaultAsync(d => d.Id == demandId, cancellationToken);

        if (demand == null)
        {
            throw new KeyNotFoundException("Demand record not found.");
        }

        if (role == UserRole.Buyer && demand.OwnerId != userId)
        {
            throw new UnauthorizedAccessException("You can only modify your own demand entries.");
        }
        else if (role is not (UserRole.Buyer or UserRole.SuperAdmin or UserRole.Developer))
        {
            throw new UnauthorizedAccessException("Unauthorized to update demand status.");
        }

        demand.Status = string.IsNullOrWhiteSpace(dto.Status) ? demand.Status : dto.Status.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            demand.Notes = dto.Notes.Trim();
        }
        demand.UpdatedAt = DateTime.UtcNow;
        demand.UpdatedBy = userId.ToString();

        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "DemandStatusUpdated",
            EntityType = nameof(Demand),
            EntityId = demand.Id.ToString(),
            Result = "Success",
            Reason = $"Updated demand status to '{demand.Status}'"
        });

        await db.SaveChangesAsync(cancellationToken);

        var itemsDto = demand.DemandItems.Select(di => new DemandItemDto(
            di.Id,
            demand.Id,
            di.RiceVarietyId,
            di.RiceVariety?.Name ?? "Variety",
            di.RiceVariety?.VarietyCode ?? "—",
            di.Grade,
            di.QuantityTonnes,
            Math.Round(di.QuantityTonnes * 1000m, 2),
            di.OfferPricePerKg,
            di.Notes
        )).ToList();

        return new DemandDto(
            demand.Id,
            demand.OwnerId,
            $"{demand.Owner.FirstName} {demand.Owner.LastName}".Trim(),
            demand.Owner.BuyerProfile?.BusinessName ?? $"{demand.Owner.FirstName} {demand.Owner.LastName}".Trim(),
            demand.District,
            demand.Season,
            demand.PeriodStart,
            demand.PeriodEnd,
            demand.Status,
            demand.Notes,
            itemsDto.Sum(i => i.QuantityTonnes),
            itemsDto.Sum(i => i.QuantityKg),
            itemsDto.Count,
            0,
            itemsDto,
            demand.CreatedAt
        );
    }

    public async Task<MarketplaceSupplyOverviewDto> GetMarketplaceSupplyAsync(
        MarketplaceSupplyFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        // 1. Materialize StockRecords in memory (SQLite decimal rule)
        var stockQuery = db.StockRecords
            .AsNoTracking()
            .Include(s => s.RiceVariety)
            .Where(s => s.Quantity > 0);

        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            stockQuery = stockQuery.Where(s => s.Location == filter.District.Trim());
        }

        if (filter.VarietyId.HasValue && filter.VarietyId.Value != Guid.Empty)
        {
            stockQuery = stockQuery.Where(s => s.RiceVarietyId == filter.VarietyId.Value);
        }

        var rawStocks = await stockQuery
            .Select(s => new
            {
                s.Id,
                District = s.Location ?? "National",
                s.RiceVarietyId,
                VarietyName = s.RiceVariety.Name,
                VarietyCode = s.RiceVariety.VarietyCode,
                s.Grade,
                s.CommodityStage,
                QuantityKg = s.Quantity,
                s.CreatedBy
            })
            .ToListAsync(cancellationToken);

        // 2. Materialize HarvestForecasts in memory
        var forecastQuery = db.HarvestForecasts
            .AsNoTracking()
            .Include(hf => hf.CultivationRecord)
                .ThenInclude(cr => cr.Field)
                    .ThenInclude(f => f.Farm)
            .Include(hf => hf.CultivationRecord)
                .ThenInclude(cr => cr.RiceVariety)
            .Where(hf => hf.CultivationRecord.Status != "Cancelled");

        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            forecastQuery = forecastQuery.Where(hf => hf.CultivationRecord.Field.Farm.District == filter.District.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Province))
        {
            forecastQuery = forecastQuery.Where(hf => hf.CultivationRecord.Field.Farm.Province == filter.Province.Trim());
        }

        if (filter.VarietyId.HasValue && filter.VarietyId.Value != Guid.Empty)
        {
            forecastQuery = forecastQuery.Where(hf => hf.CultivationRecord.RiceVarietyId == filter.VarietyId.Value);
        }

        var rawForecasts = await forecastQuery
            .Select(hf => new
            {
                District = hf.CultivationRecord.Field.Farm.District ?? "National",
                Province = hf.CultivationRecord.Field.Farm.Province,
                hf.CultivationRecord.RiceVarietyId,
                VarietyName = hf.CultivationRecord.RiceVariety.Name,
                VarietyCode = hf.CultivationRecord.RiceVariety.VarietyCode,
                ForecastKg = hf.PredictedPaddyQuantityKg,
                FarmerId = hf.CultivationRecord.FarmerId
            })
            .ToListAsync(cancellationToken);

        // 3. Materialize Latest CBSL Price Observations in memory
        var rawPrices = await db.MarketPriceObservations
            .AsNoTracking()
            .OrderByDescending(p => p.ObservationDate)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.RiceVarietyId,
                p.Value,
                p.ObservationDate
            })
            .ToListAsync(cancellationToken);

        var latestPriceMap = rawPrices
            .GroupBy(p => p.RiceVarietyId)
            .ToDictionary(g => g.Key, g => g.First().Value);

        // 4. Materialize Open DemandItems to link purchase requests
        var openDemandItems = await db.DemandItems
            .AsNoTracking()
            .Include(di => di.Demand)
            .Where(di => di.Demand.Status == "Open" || di.Demand.Status == "PartiallyFulfilled")
            .Select(di => new
            {
                di.Id,
                di.RiceVarietyId,
                di.Demand.District,
                di.Demand.OwnerId
            })
            .ToListAsync(cancellationToken);

        // 5. Aggregate Supply by (District, RiceVarietyId)
        var allKeys = rawStocks.Select(s => new { s.District, s.RiceVarietyId, s.VarietyName, s.VarietyCode })
            .Union(rawForecasts.Select(f => new { f.District, f.RiceVarietyId, f.VarietyName, f.VarietyCode }))
            .Distinct()
            .ToList();

        var listings = new List<MarketplaceSupplyItemDto>();

        foreach (var key in allKeys)
        {
            var matchingStocks = rawStocks.Where(s => s.District == key.District && s.RiceVarietyId == key.RiceVarietyId).ToList();
            var matchingForecasts = rawForecasts.Where(f => f.District == key.District && f.RiceVarietyId == key.RiceVarietyId).ToList();

            var availKg = matchingStocks.Sum(s => s.QuantityKg);
            var availMt = Math.Round(availKg / 1000m, 2);

            var forecastKg = matchingForecasts.Sum(f => f.ForecastKg);
            var forecastMt = Math.Round(forecastKg / 1000m, 2);

            var cbslPrice = latestPriceMap.TryGetValue(key.RiceVarietyId, out var price) ? price : (decimal?)null;

            // Link a matching demand item if available
            var linkedDemandItem = openDemandItems.FirstOrDefault(di =>
                di.RiceVarietyId == key.RiceVarietyId && (di.District == key.District || di.District == "National"));

            // Suggest a farmer ID from stock or forecast (for internal PR linking, masked from Buyer in PR display)
            Guid? suggestedFarmerId = null;
            var stockCreator = matchingStocks.FirstOrDefault()?.CreatedBy;
            if (stockCreator != null && Guid.TryParse(stockCreator, out var sFId))
            {
                suggestedFarmerId = sFId;
            }
            else if (matchingForecasts.Count > 0)
            {
                suggestedFarmerId = matchingForecasts.First().FarmerId;
            }

            var province = matchingForecasts.FirstOrDefault()?.Province;

            listings.Add(new MarketplaceSupplyItemDto(
                key.District,
                province,
                key.RiceVarietyId,
                key.VarietyName,
                key.VarietyCode,
                availKg,
                availMt,
                forecastMt,
                cbslPrice,
                matchingStocks.Count,
                linkedDemandItem?.Id,
                suggestedFarmerId
            ));
        }

        // 6. District Demand vs. Supply Gaps (for Admin / Government / Officer)
        var openDemands = await db.DemandItems
            .AsNoTracking()
            .Include(di => di.Demand)
            .Where(di => di.Demand.Status == "Open" || di.Demand.Status == "PartiallyFulfilled")
            .Select(di => new
            {
                District = di.Demand.District ?? "National",
                di.QuantityTonnes
            })
            .ToListAsync(cancellationToken);

        var demandByDistrict = openDemands
            .GroupBy(d => d.District)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.QuantityTonnes));

        var regionalGaps = new List<DistrictSupplyGapDto>();
        var distinctDistricts = listings.Select(l => l.District)
            .Union(demandByDistrict.Keys)
            .Distinct()
            .ToList();

        foreach (var dist in distinctDistricts)
        {
            var distDemandMt = demandByDistrict.GetValueOrDefault(dist, 0m);
            var distForecastMt = listings.Where(l => l.District == dist).Sum(l => l.ForecastedSupplyMt);
            var distStockMt = listings.Where(l => l.District == dist).Sum(l => l.AvailableStockMt);
            var gapMt = Math.Round((distForecastMt + distStockMt) - distDemandMt, 2);

            var gapStatus = gapMt > 10m ? "Surplus" : gapMt < -10m ? "Deficit" : "Balanced";
            var prov = listings.FirstOrDefault(l => l.District == dist)?.Province;

            regionalGaps.Add(new DistrictSupplyGapDto(
                dist,
                prov,
                distDemandMt,
                distForecastMt,
                distStockMt,
                gapMt,
                gapStatus
            ));
        }

        var activePrsCount = await db.PurchaseRequests
            .AsNoTracking()
            .CountAsync(pr => pr.Status == "Pending" || pr.Status == "Accepted", cancellationToken);

        return new MarketplaceSupplyOverviewDto(
            listings.OrderBy(l => l.District).ThenBy(l => l.RiceVarietyName).ToList(),
            regionalGaps.OrderBy(g => g.District).ToList(),
            Math.Round(demandByDistrict.Values.Sum(), 2),
            Math.Round(listings.Sum(l => l.ForecastedSupplyMt), 2),
            Math.Round(listings.Sum(l => l.AvailableStockMt), 2),
            activePrsCount
        );
    }

    public async Task<IReadOnlyList<PurchaseRequestDto>> GetPurchaseRequestsAsync(
        PurchaseRequestFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var query = db.PurchaseRequests
            .AsNoTracking()
            .Include(pr => pr.Buyer)
                .ThenInclude(u => u.BuyerProfile)
            .Include(pr => pr.Farmer)
            .Include(pr => pr.DemandItem)
                .ThenInclude(di => di.RiceVariety)
            .Include(pr => pr.DemandItem)
                .ThenInclude(di => di.Demand)
            .AsQueryable();

        // 1. Role-based encapsulation
        if (role == UserRole.Buyer)
        {
            // Buyer only sees their own requests
            query = query.Where(pr => pr.BuyerId == userId);
        }
        else if (role == UserRole.Farmer)
        {
            // Farmer sees requests directed to them, or open requests in their district / stock
            query = query.Where(pr => pr.FarmerId == userId || pr.FarmerId == null);
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
                query = query.Where(pr => assignedDistricts.Contains(pr.DemandItem.Demand.District));
            }
        }

        // 2. Filters
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(pr => pr.Status.ToLower() == filter.Status.Trim().ToLower());
        }

        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            query = query.Where(pr => pr.DemandItem.Demand.District == filter.District.Trim());
        }

        if (filter.VarietyId.HasValue && filter.VarietyId.Value != Guid.Empty)
        {
            query = query.Where(pr => pr.DemandItem.RiceVarietyId == filter.VarietyId.Value);
        }

        var records = await query
            .OrderByDescending(pr => pr.CreatedAt)
            .ToListAsync(cancellationToken);

        return records.Select(pr =>
        {
            var isAcceptedOrFulfilled = pr.Status == "Accepted" || pr.Status == "Fulfilled";

            // STRICT PRIVACY RULE 3:
            // Hide farmer name, phone, and contact details from Buyer while Pending or Rejected
            string farmerName;
            string? farmerPhone;
            string? farmerEmail;
            bool isContactRevealed;

            if (role == UserRole.Buyer)
            {
                if (isAcceptedOrFulfilled && pr.Farmer != null)
                {
                    farmerName = $"{pr.Farmer.FirstName} {pr.Farmer.LastName}".Trim();
                    farmerPhone = pr.Farmer.PhoneNumber;
                    farmerEmail = pr.Farmer.Email;
                    isContactRevealed = true;
                }
                else
                {
                    farmerName = "Verified District Producer (Hidden until accepted)";
                    farmerPhone = null;
                    farmerEmail = null;
                    isContactRevealed = false;
                }
            }
            else
            {
                // Farmer, Officer, Admin see true names
                farmerName = pr.Farmer != null
                    ? $"{pr.Farmer.FirstName} {pr.Farmer.LastName}".Trim()
                    : "Open District Pool";
                farmerPhone = pr.Farmer?.PhoneNumber;
                farmerEmail = pr.Farmer?.Email;
                isContactRevealed = pr.Farmer != null;
            }

            var buyerName = $"{pr.Buyer.FirstName} {pr.Buyer.LastName}".Trim();
            var buyerBusiness = pr.Buyer.BuyerProfile?.BusinessName ?? buyerName;
            var totalValue = Math.Round(pr.QuantityKg * pr.OfferPricePerKg, 2);

            return new PurchaseRequestDto(
                pr.Id,
                pr.BuyerId,
                buyerName,
                buyerBusiness,
                pr.FarmerId,
                farmerName,
                farmerPhone,
                farmerEmail,
                isContactRevealed,
                pr.DemandItemId,
                pr.DemandItem?.DemandId,
                pr.DemandItem?.Demand?.Season ?? "Current",
                pr.DemandItem?.Demand?.District ?? "National",
                pr.DemandItem?.RiceVarietyId ?? Guid.Empty,
                pr.DemandItem?.RiceVariety?.Name ?? "Rice Variety",
                pr.DemandItem?.RiceVariety?.VarietyCode ?? "—",
                pr.QuantityKg,
                Math.Round(pr.QuantityKg / 1000m, 2),
                pr.OfferPricePerKg,
                totalValue,
                pr.Status,
                pr.Terms,
                pr.DeliveryTerms,
                pr.CreatedAt
            );
        }).ToList();
    }

    public async Task<PurchaseRequestDto> CreatePurchaseRequestAsync(
        CreatePurchaseRequestDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role is not (UserRole.Buyer or UserRole.SuperAdmin or UserRole.Developer))
        {
            throw new UnauthorizedAccessException("Only buyers and administrators can create purchase requests.");
        }

        if (dto.QuantityKg <= 0)
        {
            throw new ArgumentException("Order quantity must be greater than zero kg.");
        }

        if (dto.OfferPricePerKg <= 0)
        {
            throw new ArgumentException("Offered price per kg must be greater than zero.");
        }

        var demandItem = await db.DemandItems
            .Include(di => di.Demand)
            .Include(di => di.RiceVariety)
            .SingleOrDefaultAsync(di => di.Id == dto.DemandItemId, cancellationToken);

        if (demandItem == null)
        {
            throw new KeyNotFoundException("Matching procurement demand item line not found.");
        }

        var pr = new PurchaseRequest
        {
            BuyerId = userId,
            FarmerId = dto.FarmerId,
            DemandItemId = dto.DemandItemId,
            QuantityKg = Math.Round(dto.QuantityKg, 2),
            OfferPricePerKg = Math.Round(dto.OfferPricePerKg, 2),
            Status = "Pending",
            Terms = dto.Terms?.Trim(),
            DeliveryTerms = dto.DeliveryTerms?.Trim(),
            CreatedBy = userId.ToString()
        };

        db.PurchaseRequests.Add(pr);

        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "PurchaseRequestCreated",
            EntityType = nameof(PurchaseRequest),
            EntityId = pr.Id.ToString(),
            Result = "Success",
            Reason = $"Created purchase request for {pr.QuantityKg:N0} kg of '{demandItem.RiceVariety?.Name}' at LKR {pr.OfferPricePerKg:N2}/kg"
        });

        await db.SaveChangesAsync(cancellationToken);

        // Trigger in-app notification to matching farmers
        await notificationService.NotifyPurchaseRequestSubmittedAsync(
            pr.Id,
            pr.FarmerId,
            demandItem.Demand.District,
            demandItem.RiceVariety?.Name ?? "Paddy",
            pr.QuantityKg,
            cancellationToken);

        var buyer = await db.Users
            .AsNoTracking()
            .Include(u => u.BuyerProfile)
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        var buyerName = buyer != null ? $"{buyer.FirstName} {buyer.LastName}".Trim() : "Buyer";

        return new PurchaseRequestDto(
            pr.Id,
            pr.BuyerId,
            buyerName,
            buyer?.BuyerProfile?.BusinessName ?? buyerName,
            pr.FarmerId,
            "Verified District Producer (Hidden until accepted)",
            null,
            null,
            false,
            demandItem.Id,
            demandItem.DemandId,
            demandItem.Demand.Season,
            demandItem.Demand.District,
            demandItem.RiceVarietyId,
            demandItem.RiceVariety?.Name ?? "Rice Variety",
            demandItem.RiceVariety?.VarietyCode ?? "—",
            pr.QuantityKg,
            Math.Round(pr.QuantityKg / 1000m, 2),
            pr.OfferPricePerKg,
            Math.Round(pr.QuantityKg * pr.OfferPricePerKg, 2),
            pr.Status,
            pr.Terms,
            pr.DeliveryTerms,
            pr.CreatedAt
        );
    }

    public async Task<PurchaseRequestDto> UpdatePurchaseRequestStatusAsync(
        Guid requestId,
        UpdatePurchaseRequestStatusDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var pr = await db.PurchaseRequests
            .Include(r => r.Buyer)
                .ThenInclude(u => u.BuyerProfile)
            .Include(r => r.Farmer)
            .Include(r => r.DemandItem)
                .ThenInclude(di => di.RiceVariety)
            .Include(r => r.DemandItem)
                .ThenInclude(di => di.Demand)
            .SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (pr == null)
        {
            throw new KeyNotFoundException("Purchase request not found.");
        }

        var newStatus = dto.Status.Trim();
        var validStatuses = new[] { "Pending", "Accepted", "Rejected", "Fulfilled", "Cancelled" };
        if (!validStatuses.Contains(newStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid purchase request status '{newStatus}'.");
        }

        // Authorization checks on status transitions
        if (newStatus.Equals("Accepted", StringComparison.OrdinalIgnoreCase) ||
            newStatus.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
        {
            // Only the matched farmer or an admin can accept or reject
            if (role == UserRole.Farmer)
            {
                if (pr.FarmerId.HasValue && pr.FarmerId.Value != userId)
                {
                    throw new UnauthorizedAccessException("Only the designated producer can accept or decline this order.");
                }
                // If farmerId was open, claim it
                if (!pr.FarmerId.HasValue)
                {
                    pr.FarmerId = userId;
                }
            }
            else if (role is not (UserRole.SuperAdmin or UserRole.Developer))
            {
                throw new UnauthorizedAccessException("Unauthorized to accept or reject this purchase request.");
            }
        }
        else if (newStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            if (role == UserRole.Buyer && pr.BuyerId != userId)
            {
                throw new UnauthorizedAccessException("You can only cancel your own purchase requests.");
            }
            if (pr.Status != "Pending" && role == UserRole.Buyer)
            {
                throw new InvalidOperationException("Only pending purchase requests can be cancelled.");
            }
        }
        else if (newStatus.Equals("Fulfilled", StringComparison.OrdinalIgnoreCase))
        {
            if (role == UserRole.Buyer && pr.BuyerId != userId)
            {
                throw new UnauthorizedAccessException("Unauthorized to mark this order fulfilled.");
            }
            if (role == UserRole.Farmer && pr.FarmerId != userId)
            {
                throw new UnauthorizedAccessException("Unauthorized to mark this order fulfilled.");
            }

            // Deduct fulfilled inventory from StockRecord if matching stock exists
            if (pr.FarmerId.HasValue)
            {
                var farmerIdStr = pr.FarmerId.Value.ToString();
                var stock = await db.StockRecords
                    .FirstOrDefaultAsync(s =>
                        s.CreatedBy == farmerIdStr &&
                        s.RiceVarietyId == pr.DemandItem.RiceVarietyId &&
                        s.Quantity > 0,
                        cancellationToken);

                if (stock != null)
                {
                    stock.Quantity = Math.Max(0, stock.Quantity - pr.QuantityKg);
                    stock.UpdatedAt = DateTime.UtcNow;
                    stock.UpdatedBy = userId.ToString();
                }
            }
        }

        pr.Status = newStatus;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            pr.Terms = string.IsNullOrWhiteSpace(pr.Terms)
                ? dto.Notes.Trim()
                : $"{pr.Terms} | {dto.Notes.Trim()}";
        }
        pr.UpdatedAt = DateTime.UtcNow;
        pr.UpdatedBy = userId.ToString();

        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "PurchaseRequestStatusUpdated",
            EntityType = nameof(PurchaseRequest),
            EntityId = pr.Id.ToString(),
            Result = "Success",
            Reason = $"Updated purchase request {pr.Id} status to '{pr.Status}'"
        });

        await db.SaveChangesAsync(cancellationToken);

        // Fetch refreshed farmer entity if farmer was just claimed
        if (pr.Farmer == null && pr.FarmerId.HasValue)
        {
            pr.Farmer = await db.Users.FindAsync([pr.FarmerId.Value], cancellationToken);
        }

        var isAcceptedOrFulfilled = pr.Status == "Accepted" || pr.Status == "Fulfilled";
        string farmerName;
        string? farmerPhone;
        string? farmerEmail;
        bool isContactRevealed;

        if (role == UserRole.Buyer)
        {
            if (isAcceptedOrFulfilled && pr.Farmer != null)
            {
                farmerName = $"{pr.Farmer.FirstName} {pr.Farmer.LastName}".Trim();
                farmerPhone = pr.Farmer.PhoneNumber;
                farmerEmail = pr.Farmer.Email;
                isContactRevealed = true;
            }
            else
            {
                farmerName = "Verified District Producer (Hidden until accepted)";
                farmerPhone = null;
                farmerEmail = null;
                isContactRevealed = false;
            }
        }
        else
        {
            farmerName = pr.Farmer != null ? $"{pr.Farmer.FirstName} {pr.Farmer.LastName}".Trim() : "Open District Pool";
            farmerPhone = pr.Farmer?.PhoneNumber;
            farmerEmail = pr.Farmer?.Email;
            isContactRevealed = pr.Farmer != null;
        }

        var buyerName = $"{pr.Buyer.FirstName} {pr.Buyer.LastName}".Trim();

        return new PurchaseRequestDto(
            pr.Id,
            pr.BuyerId,
            buyerName,
            pr.Buyer.BuyerProfile?.BusinessName ?? buyerName,
            pr.FarmerId,
            farmerName,
            farmerPhone,
            farmerEmail,
            isContactRevealed,
            pr.DemandItemId,
            pr.DemandItem?.DemandId,
            pr.DemandItem?.Demand?.Season ?? "Current",
            pr.DemandItem?.Demand?.District ?? "National",
            pr.DemandItem?.RiceVarietyId ?? Guid.Empty,
            pr.DemandItem?.RiceVariety?.Name ?? "Rice Variety",
            pr.DemandItem?.RiceVariety?.VarietyCode ?? "—",
            pr.QuantityKg,
            Math.Round(pr.QuantityKg / 1000m, 2),
            pr.OfferPricePerKg,
            Math.Round(pr.QuantityKg * pr.OfferPricePerKg, 2),
            pr.Status,
            pr.Terms,
            pr.DeliveryTerms,
            pr.CreatedAt
        );
    }
}
