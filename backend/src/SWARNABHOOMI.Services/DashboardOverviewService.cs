using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Services;

public sealed class DashboardOverviewService(SWARNABHOOMIContext db)
{
    public async Task<DashboardOverview> GetOverviewAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var roleName = principal.FindFirst(ClaimTypes.Role)?.Value;
        if (!Enum.TryParse<UserRole>(roleName, out var role))
        {
            throw new UnauthorizedAccessException("A valid user role is required.");
        }

        var visibleCards = GetVisibleCards(role);
        var users = visibleCards.Contains(DashboardCard.Users)
            ? await db.Users.CountAsync(user => user.IsActive, cancellationToken)
            : (int?)null;
        var farms = visibleCards.Contains(DashboardCard.Farms)
            ? await db.Farms.CountAsync(cancellationToken)
            : (int?)null;
        var demandEntries = visibleCards.Contains(DashboardCard.DemandEntries)
            ? await db.Demands.CountAsync(cancellationToken)
            : (int?)null;
        var varieties = visibleCards.Contains(DashboardCard.Varieties)
            ? await db.RiceVarieties.CountAsync(cancellationToken)
            : (int?)null;
        var marketPrices = visibleCards.Contains(DashboardCard.MarketPrices)
            ? await db.MarketPriceObservations.CountAsync(cancellationToken)
            : (int?)null;
        var weatherRecords = visibleCards.Contains(DashboardCard.WeatherRecords)
            ? await db.WeatherObservations.CountAsync(cancellationToken)
            : (int?)null;

        return new DashboardOverview(
            users,
            farms,
            demandEntries,
            varieties,
            marketPrices,
            weatherRecords,
            visibleCards.ToArray());
    }

    private static HashSet<string> GetVisibleCards(UserRole role) => role switch
    {
        UserRole.SuperAdmin or UserRole.Developer =>
        [DashboardCard.Users, DashboardCard.Farms, DashboardCard.DemandEntries, DashboardCard.Varieties,
            DashboardCard.MarketPrices, DashboardCard.WeatherRecords],
        UserRole.Officer or UserRole.Government =>
        [DashboardCard.Farms, DashboardCard.DemandEntries, DashboardCard.Varieties, DashboardCard.MarketPrices,
            DashboardCard.WeatherRecords],
        UserRole.Farmer =>
        [DashboardCard.Farms, DashboardCard.DemandEntries, DashboardCard.Varieties, DashboardCard.WeatherRecords],
        UserRole.Buyer =>
        [DashboardCard.DemandEntries, DashboardCard.Varieties, DashboardCard.MarketPrices, DashboardCard.WeatherRecords],
        _ => []
    };
}

public sealed record DashboardOverview(
    int? Users,
    int? Farms,
    int? DemandEntries,
    int? Varieties,
    int? MarketPrices,
    int? WeatherRecords,
    IReadOnlyCollection<string> VisibleCards);

public static class DashboardCard
{
    public const string Users = "users";
    public const string Farms = "farms";
    public const string DemandEntries = "demandEntries";
    public const string Varieties = "varieties";
    public const string MarketPrices = "marketPrices";
    public const string WeatherRecords = "weatherRecords";
}
