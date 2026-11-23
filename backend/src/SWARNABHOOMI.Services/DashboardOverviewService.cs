using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Services;

public interface IDashboardOverviewService
{
    Task<DashboardOverviewResponse> GetOverviewAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);
}

public sealed class DashboardOverviewService(
    SWARNABHOOMIContext db,
    IGovernanceNotificationService governanceService) : IDashboardOverviewService
{
    public async Task<DashboardOverviewResponse> GetOverviewAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        switch (role)
        {
            case UserRole.Farmer:
            {
                var myFarms = await db.Farms
                    .AsNoTracking()
                    .CountAsync(farm => farm.UserId == userId, cancellationToken);

                var myFields = await db.Fields
                    .AsNoTracking()
                    .CountAsync(field => field.Farm.UserId == userId, cancellationToken);

                // Materialize projected decimal values to memory first for SQLite compatibility
                var myFieldAreas = await db.Fields
                    .AsNoTracking()
                    .Where(field => field.Farm.UserId == userId)
                    .Select(field => field.AreaHectares)
                    .ToListAsync(cancellationToken);
                var myFieldArea = myFieldAreas.Count > 0 ? myFieldAreas.Sum() : 0m;

                var myFarmAreas = await db.Farms
                    .AsNoTracking()
                    .Where(farm => farm.UserId == userId)
                    .Select(farm => farm.AreaHectares)
                    .ToListAsync(cancellationToken);
                var myFarmArea = myFarmAreas.Count > 0 ? myFarmAreas.Sum() : 0m;

                var totalArea = myFieldArea > 0 ? myFieldArea : myFarmArea;

                var demandEntries = await db.Demands
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var varieties = await db.RiceVarieties
                    .AsNoTracking()
                    .CountAsync(v => v.IsActive, cancellationToken);

                var marketPrices = await db.MarketPriceObservations
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var weatherRecords = await db.WeatherObservations
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                // Forecasted Harvest Tonnes for Farmer
                var myForecastYields = await db.HarvestForecasts
                    .AsNoTracking()
                    .Where(hf => hf.CultivationRecord.FarmerId == userId || hf.CultivationRecord.Field.Farm.UserId == userId)
                    .Select(hf => hf.PredictedPaddyQuantityKg)
                    .ToListAsync(cancellationToken);
                var forecastedHarvestTonnes = myForecastYields.Count > 0
                    ? Math.Round(myForecastYields.Sum() / 1000m, 2)
                    : 0m;

                // Active Cultivations for Farmer
                var activeCultivations = await db.CultivationRecords
                    .AsNoTracking()
                    .CountAsync(cr => (cr.FarmerId == userId || cr.Field.Farm.UserId == userId) &&
                                      cr.Status != "Harvested" && cr.Status != "Cancelled", cancellationToken);

                // Open Purchase Requests for Farmer (incoming)
                var openPurchaseRequests = await db.PurchaseRequests
                    .AsNoTracking()
                    .CountAsync(pr => pr.FarmerId == userId && (pr.Status == "Pending" || pr.Status == "Accepted"), cancellationToken);

                // Unread Notifications for Farmer (after idempotent harvest check)
                await governanceService.EnsureUpcomingHarvestNotificationsAsync(userId, cancellationToken);
                var unreadNotifications = await db.Notifications
                    .AsNoTracking()
                    .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

                return new DashboardOverviewResponse(
                    Users: null, // Strictly encapsulated: never expose network-wide user count to farmers
                    Farms: myFarms, // Scoped to farmer's own farms
                    DemandEntries: demandEntries,
                    Varieties: varieties,
                    MarketPrices: marketPrices,
                    WeatherRecords: weatherRecords,
                    MyFarms: myFarms,
                    MyFields: myFields,
                    MyTotalAreaHectares: Math.Round(totalArea, 2),
                    VisibleCards: ["myFarms", "myFields", "myTotalArea", "forecastedHarvest", "activeCultivations", "openPurchaseRequests", "unreadNotifications", "marketPrices", "weatherRecords", "varieties"],
                    DistrictSupplies: null,
                    PendingFieldVerifications: null,
                    VerifiedFields: null,
                    ForecastedHarvestTonnes: forecastedHarvestTonnes,
                    ActiveCultivations: activeCultivations,
                    OpenPurchaseRequests: openPurchaseRequests,
                    UnreadNotifications: unreadNotifications);
            }

            case UserRole.Buyer:
            {
                var demandEntries = await db.Demands
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var varieties = await db.RiceVarieties
                    .AsNoTracking()
                    .CountAsync(v => v.IsActive, cancellationToken);

                var marketPrices = await db.MarketPriceObservations
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var weatherRecords = await db.WeatherObservations
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                // Anonymized district-level aggregated verified supply
                // Materialize projected values to memory first to avoid SQLite decimal SUM / GroupBy translation exceptions
                var verifiedFields = await db.Fields
                    .AsNoTracking()
                    .Where(field => field.Farm.District != null &&
                        field.Verifications.Any(v => v.Status == "Verified"))
                    .Select(field => new
                    {
                        District = field.Farm.District!,
                        field.AreaHectares
                    })
                    .ToListAsync(cancellationToken);

                var districtSupplies = verifiedFields
                    .GroupBy(field => field.District)
                    .Select(group => new DistrictSupplySummary(
                        group.Key,
                        group.Count(),
                        Math.Round(group.Sum(f => f.AreaHectares), 2)))
                    .OrderBy(ds => ds.District)
                    .ToList();

                // Anonymized regional/national forecasted supply MT for Buyer
                var buyerYields = await db.HarvestForecasts
                    .AsNoTracking()
                    .Where(hf => hf.CultivationRecord.Status != "Cancelled")
                    .Select(hf => hf.PredictedPaddyQuantityKg)
                    .ToListAsync(cancellationToken);
                var forecastedSupplyMt = buyerYields.Count > 0
                    ? Math.Round(buyerYields.Sum() / 1000m, 2)
                    : 0m;

                // Open Purchase Requests for Buyer (outgoing)
                var openPurchaseRequests = await db.PurchaseRequests
                    .AsNoTracking()
                    .CountAsync(pr => pr.BuyerId == userId && (pr.Status == "Pending" || pr.Status == "Accepted"), cancellationToken);

                // Unread Notifications for Buyer
                var unreadNotifications = await db.Notifications
                    .AsNoTracking()
                    .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

                return new DashboardOverviewResponse(
                    Users: null, // Strictly encapsulated: no network-wide member count
                    Farms: null, // Strictly encapsulated: no individual farm count
                    DemandEntries: demandEntries,
                    Varieties: varieties,
                    MarketPrices: marketPrices,
                    WeatherRecords: weatherRecords,
                    MyFarms: null,
                    MyFields: null,
                    MyTotalAreaHectares: null,
                    VisibleCards: ["demandEntries", "forecastedHarvest", "openPurchaseRequests", "unreadNotifications", "varieties", "marketPrices", "weatherRecords"],
                    DistrictSupplies: districtSupplies,
                    PendingFieldVerifications: null,
                    VerifiedFields: null,
                    ForecastedHarvestTonnes: forecastedSupplyMt,
                    ActiveCultivations: 0,
                    OpenPurchaseRequests: openPurchaseRequests,
                    UnreadNotifications: unreadNotifications);
            }

            case UserRole.Officer:
            {
                var assignedDistricts = await governanceService.GetAssignedDistrictsForOfficerAsync(userId, cancellationToken);

                var regionalFarms = await db.Farms
                    .AsNoTracking()
                    .CountAsync(farm => farm.District != null && assignedDistricts.Contains(farm.District), cancellationToken);

                var demandEntries = await db.Demands
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var varieties = await db.RiceVarieties
                    .AsNoTracking()
                    .CountAsync(v => v.IsActive, cancellationToken);

                var marketPrices = await db.MarketPriceObservations
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var weatherRecords = await db.WeatherObservations
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var verificationQuery = db.Fields
                    .AsNoTracking()
                    .AsQueryable();

                if (assignedDistricts.Count > 0)
                {
                    verificationQuery = verificationQuery.Where(f => f.Farm.District != null && assignedDistricts.Contains(f.Farm.District));
                }

                var verificationStatuses = await verificationQuery
                    .Select(f => f.Verifications
                        .OrderByDescending(v => v.CreatedAt)
                        .Select(v => v.Status)
                        .FirstOrDefault())
                    .ToListAsync(cancellationToken);

                var pendingFieldVerifications = verificationStatuses.Count(s => string.IsNullOrEmpty(s) || s == "Pending");
                var verifiedFields = verificationStatuses.Count(s => s == "Verified");

                // Forecasted Harvest in Officer's districts
                var officerForecastQuery = db.HarvestForecasts
                    .AsNoTracking()
                    .Where(hf => hf.CultivationRecord.Status != "Cancelled");
                if (assignedDistricts.Count > 0)
                {
                    officerForecastQuery = officerForecastQuery.Where(hf =>
                        hf.CultivationRecord.Field.Farm.District != null &&
                        assignedDistricts.Contains(hf.CultivationRecord.Field.Farm.District));
                }
                var officerYields = await officerForecastQuery
                    .Select(hf => hf.PredictedPaddyQuantityKg)
                    .ToListAsync(cancellationToken);
                var officerForecastMt = officerYields.Count > 0
                    ? Math.Round(officerYields.Sum() / 1000m, 2)
                    : 0m;

                // Active Cultivations in Officer's districts
                var officerCultQuery = db.CultivationRecords
                    .AsNoTracking()
                    .Where(cr => cr.Status != "Harvested" && cr.Status != "Cancelled");
                if (assignedDistricts.Count > 0)
                {
                    officerCultQuery = officerCultQuery.Where(cr =>
                        cr.Field.Farm.District != null &&
                        assignedDistricts.Contains(cr.Field.Farm.District));
                }
                var activeCultivations = await officerCultQuery.CountAsync(cancellationToken);

                // Open Purchase Requests in Officer's districts
                var officerPrQuery = db.PurchaseRequests
                    .AsNoTracking()
                    .Where(pr => pr.Status == "Pending" || pr.Status == "Accepted");
                if (assignedDistricts.Count > 0)
                {
                    officerPrQuery = officerPrQuery.Where(pr =>
                        pr.DemandItem.Demand.District != null &&
                        assignedDistricts.Contains(pr.DemandItem.Demand.District));
                }
                var openPurchaseRequests = await officerPrQuery.CountAsync(cancellationToken);

                // Unread Notifications for Officer
                var unreadNotifications = await db.Notifications
                    .AsNoTracking()
                    .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

                return new DashboardOverviewResponse(
                    Users: null,
                    Farms: regionalFarms,
                    DemandEntries: demandEntries,
                    Varieties: varieties,
                    MarketPrices: marketPrices,
                    WeatherRecords: weatherRecords,
                    MyFarms: null,
                    MyFields: null,
                    MyTotalAreaHectares: null,
                    VisibleCards: ["farms", "pendingVerifications", "verifiedFields", "forecastedHarvest", "activeCultivations", "openPurchaseRequests", "unreadNotifications", "demandEntries", "varieties", "marketPrices", "weatherRecords"],
                    DistrictSupplies: null,
                    PendingFieldVerifications: pendingFieldVerifications,
                    VerifiedFields: verifiedFields,
                    ForecastedHarvestTonnes: officerForecastMt,
                    ActiveCultivations: activeCultivations,
                    OpenPurchaseRequests: openPurchaseRequests,
                    UnreadNotifications: unreadNotifications);
            }

            case UserRole.Government:
            {
                var users = await db.Users
                    .AsNoTracking()
                    .CountAsync(u => u.IsActive, cancellationToken);

                var farms = await db.Farms
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var demandEntries = await db.Demands
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var varieties = await db.RiceVarieties
                    .AsNoTracking()
                    .CountAsync(v => v.IsActive, cancellationToken);

                var marketPrices = await db.MarketPriceObservations
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var weatherRecords = await db.WeatherObservations
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var verificationStatuses = await db.Fields
                    .AsNoTracking()
                    .Select(f => f.Verifications
                        .OrderByDescending(v => v.CreatedAt)
                        .Select(v => v.Status)
                        .FirstOrDefault())
                    .ToListAsync(cancellationToken);

                var pendingFieldVerifications = verificationStatuses.Count(s => string.IsNullOrEmpty(s) || s == "Pending");
                var verifiedFields = verificationStatuses.Count(s => s == "Verified");

                // National Forecasted Harvest
                var nationalYields = await db.HarvestForecasts
                    .AsNoTracking()
                    .Where(hf => hf.CultivationRecord.Status != "Cancelled")
                    .Select(hf => hf.PredictedPaddyQuantityKg)
                    .ToListAsync(cancellationToken);
                var nationalForecastMt = nationalYields.Count > 0
                    ? Math.Round(nationalYields.Sum() / 1000m, 2)
                    : 0m;

                // National Active Cultivations
                var activeCultivations = await db.CultivationRecords
                    .AsNoTracking()
                    .CountAsync(cr => cr.Status != "Harvested" && cr.Status != "Cancelled", cancellationToken);

                // National Open Purchase Requests
                var openPurchaseRequests = await db.PurchaseRequests
                    .AsNoTracking()
                    .CountAsync(pr => pr.Status == "Pending" || pr.Status == "Accepted", cancellationToken);

                // Unread Notifications
                var unreadNotifications = await db.Notifications
                    .AsNoTracking()
                    .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

                return new DashboardOverviewResponse(
                    Users: users,
                    Farms: farms,
                    DemandEntries: demandEntries,
                    Varieties: varieties,
                    MarketPrices: marketPrices,
                    WeatherRecords: weatherRecords,
                    MyFarms: null,
                    MyFields: null,
                    MyTotalAreaHectares: null,
                    VisibleCards: ["farms", "pendingVerifications", "verifiedFields", "forecastedHarvest", "activeCultivations", "openPurchaseRequests", "unreadNotifications", "demandEntries", "varieties", "marketPrices", "weatherRecords"],
                    DistrictSupplies: null,
                    PendingFieldVerifications: pendingFieldVerifications,
                    VerifiedFields: verifiedFields,
                    ForecastedHarvestTonnes: nationalForecastMt,
                    ActiveCultivations: activeCultivations,
                    OpenPurchaseRequests: openPurchaseRequests,
                    UnreadNotifications: unreadNotifications);
            }

            case UserRole.SuperAdmin:
            case UserRole.Developer:
            default:
            {
                var users = await db.Users
                    .AsNoTracking()
                    .CountAsync(u => u.IsActive, cancellationToken);

                var farms = await db.Farms
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var demandEntries = await db.Demands
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var varieties = await db.RiceVarieties
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var marketPrices = await db.MarketPriceObservations
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var weatherRecords = await db.WeatherObservations
                    .AsNoTracking()
                    .CountAsync(cancellationToken);

                var verificationStatuses = await db.Fields
                    .AsNoTracking()
                    .Select(f => f.Verifications
                        .OrderByDescending(v => v.CreatedAt)
                        .Select(v => v.Status)
                        .FirstOrDefault())
                    .ToListAsync(cancellationToken);

                var pendingFieldVerifications = verificationStatuses.Count(s => string.IsNullOrEmpty(s) || s == "Pending");
                var verifiedFields = verificationStatuses.Count(s => s == "Verified");

                // National Forecasted Harvest
                var nationalYields = await db.HarvestForecasts
                    .AsNoTracking()
                    .Where(hf => hf.CultivationRecord.Status != "Cancelled")
                    .Select(hf => hf.PredictedPaddyQuantityKg)
                    .ToListAsync(cancellationToken);
                var nationalForecastMt = nationalYields.Count > 0
                    ? Math.Round(nationalYields.Sum() / 1000m, 2)
                    : 0m;

                // National Active Cultivations
                var activeCultivations = await db.CultivationRecords
                    .AsNoTracking()
                    .CountAsync(cr => cr.Status != "Harvested" && cr.Status != "Cancelled", cancellationToken);

                // National Open Purchase Requests
                var openPurchaseRequests = await db.PurchaseRequests
                    .AsNoTracking()
                    .CountAsync(pr => pr.Status == "Pending" || pr.Status == "Accepted", cancellationToken);

                // Unread Notifications
                var unreadNotifications = await db.Notifications
                    .AsNoTracking()
                    .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

                return new DashboardOverviewResponse(
                    Users: users,
                    Farms: farms,
                    DemandEntries: demandEntries,
                    Varieties: varieties,
                    MarketPrices: marketPrices,
                    WeatherRecords: weatherRecords,
                    MyFarms: null,
                    MyFields: null,
                    MyTotalAreaHectares: null,
                    VisibleCards: ["users", "farms", "pendingVerifications", "verifiedFields", "forecastedHarvest", "activeCultivations", "openPurchaseRequests", "unreadNotifications", "demandEntries", "varieties", "marketPrices", "weatherRecords"],
                    DistrictSupplies: null,
                    PendingFieldVerifications: pendingFieldVerifications,
                    VerifiedFields: verifiedFields,
                    ForecastedHarvestTonnes: nationalForecastMt,
                    ActiveCultivations: activeCultivations,
                    OpenPurchaseRequests: openPurchaseRequests,
                    UnreadNotifications: unreadNotifications);
            }
        }
    }
}

public sealed record DashboardOverviewResponse(
    int? Users,
    int? Farms,
    int? DemandEntries,
    int? Varieties,
    int? MarketPrices,
    int? WeatherRecords,
    int? MyFarms,
    int? MyFields,
    decimal? MyTotalAreaHectares,
    IReadOnlyCollection<string> VisibleCards,
    IReadOnlyCollection<DistrictSupplySummary>? DistrictSupplies,
    int? PendingFieldVerifications = null,
    int? VerifiedFields = null,
    decimal? ForecastedHarvestTonnes = null,
    int? ActiveCultivations = null,
    int? OpenPurchaseRequests = null,
    int? UnreadNotifications = null
);

public sealed record DistrictSupplySummary(
    string District,
    int VerifiedFields,
    decimal VerifiedAreaHectares
);
