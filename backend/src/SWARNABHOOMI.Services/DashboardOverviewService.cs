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

public sealed class DashboardOverviewService(SWARNABHOOMIContext db) : IDashboardOverviewService
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

                var myFieldArea = await db.Fields
                    .AsNoTracking()
                    .Where(field => field.Farm.UserId == userId)
                    .SumAsync(field => (decimal?)field.AreaHectares, cancellationToken) ?? 0m;

                var myFarmArea = await db.Farms
                    .AsNoTracking()
                    .Where(farm => farm.UserId == userId)
                    .SumAsync(farm => (decimal?)farm.AreaHectares, cancellationToken) ?? 0m;

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
                    VisibleCards: ["myFarms", "myFields", "myTotalArea", "marketPrices", "weatherRecords", "varieties"],
                    DistrictSupplies: null,
                    PendingFieldVerifications: null,
                    VerifiedFields: null);
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
                var districtSupplies = await db.Fields
                    .AsNoTracking()
                    .Where(field => field.Farm.District != null &&
                        field.Verifications.Any(v => v.Status == "Verified"))
                    .GroupBy(field => field.Farm.District!)
                    .Select(group => new DistrictSupplySummary(
                        group.Key,
                        group.Count(),
                        Math.Round(group.Sum(f => f.AreaHectares), 2)))
                    .OrderBy(ds => ds.District)
                    .ToListAsync(cancellationToken);

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
                    VisibleCards: ["demandEntries", "varieties", "marketPrices", "weatherRecords"],
                    DistrictSupplies: districtSupplies,
                    PendingFieldVerifications: null,
                    VerifiedFields: null);
            }

            case UserRole.Officer:
            {
                var assignedDistricts = await db.OfficerAssignments
                    .AsNoTracking()
                    .Where(assignment => assignment.UserId == userId && assignment.IsActive)
                    .Select(assignment => assignment.District)
                    .Distinct()
                    .ToListAsync(cancellationToken);

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

                var pendingFieldVerifications = await db.Fields
                    .AsNoTracking()
                    .Where(f => f.Farm.District != null && assignedDistricts.Contains(f.Farm.District))
                    .CountAsync(f => !f.Verifications.Any() ||
                        f.Verifications.OrderByDescending(v => v.CreatedAt).Select(v => v.Status).FirstOrDefault() == "Pending",
                        cancellationToken);

                var verifiedFields = await db.Fields
                    .AsNoTracking()
                    .Where(f => f.Farm.District != null && assignedDistricts.Contains(f.Farm.District))
                    .CountAsync(f => f.Verifications.OrderByDescending(v => v.CreatedAt).Select(v => v.Status).FirstOrDefault() == "Verified",
                        cancellationToken);

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
                    VisibleCards: ["farms", "pendingVerifications", "verifiedFields", "demandEntries", "varieties", "marketPrices", "weatherRecords"],
                    DistrictSupplies: null,
                    PendingFieldVerifications: pendingFieldVerifications,
                    VerifiedFields: verifiedFields);
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

                var pendingFieldVerifications = await db.Fields
                    .AsNoTracking()
                    .CountAsync(f => !f.Verifications.Any() ||
                        f.Verifications.OrderByDescending(v => v.CreatedAt).Select(v => v.Status).FirstOrDefault() == "Pending",
                        cancellationToken);

                var verifiedFields = await db.Fields
                    .AsNoTracking()
                    .CountAsync(f => f.Verifications.OrderByDescending(v => v.CreatedAt).Select(v => v.Status).FirstOrDefault() == "Verified",
                        cancellationToken);

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
                    VisibleCards: ["farms", "pendingVerifications", "verifiedFields", "demandEntries", "varieties", "marketPrices", "weatherRecords"],
                    DistrictSupplies: null,
                    PendingFieldVerifications: pendingFieldVerifications,
                    VerifiedFields: verifiedFields);
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

                var pendingFieldVerifications = await db.Fields
                    .AsNoTracking()
                    .CountAsync(f => !f.Verifications.Any() ||
                        f.Verifications.OrderByDescending(v => v.CreatedAt).Select(v => v.Status).FirstOrDefault() == "Pending",
                        cancellationToken);

                var verifiedFields = await db.Fields
                    .AsNoTracking()
                    .CountAsync(f => f.Verifications.OrderByDescending(v => v.CreatedAt).Select(v => v.Status).FirstOrDefault() == "Verified",
                        cancellationToken);

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
                    VisibleCards: ["users", "farms", "pendingVerifications", "verifiedFields", "demandEntries", "varieties", "marketPrices", "weatherRecords"],
                    DistrictSupplies: null,
                    PendingFieldVerifications: pendingFieldVerifications,
                    VerifiedFields: verifiedFields);
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
    int? VerifiedFields = null
);

public sealed record DistrictSupplySummary(
    string District,
    int VerifiedFields,
    decimal VerifiedAreaHectares
);
