using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Api.Members;

public static class MemberDetailsEndpoints
{
    public static IEndpointRouteBuilder MapMemberDetailsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var members = endpoints.MapGroup("/api/admin/members")
            .RequireAuthorization("SuperAdminOnly");

        members.MapGet("/{memberId:guid}", GetMemberDetails);
        return endpoints;
    }

    private static async Task<IResult> GetMemberDetails(
        Guid memberId,
        SWARNABHOOMIContext db,
        CancellationToken cancellationToken)
    {
        var account = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == memberId)
            .Select(user => new MemberAccountDetails(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.PhoneNumber,
                user.Role,
                user.IsActive,
                user.IsVerified,
                user.District,
                user.Province,
                user.CreatedAt,
                user.LastLoginAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return Results.NotFound(new { error = "Member not found." });
        }

        MemberProfileDetails? profile = null;
        if (account.Role == UserRole.Farmer)
        {
            profile = await db.FarmerProfiles
                .AsNoTracking()
                .Where(candidate => candidate.UserId == memberId)
                .Select(candidate => new MemberProfileDetails(
                    "Farmer",
                    candidate.Address,
                    candidate.TotalLandAreaHectares,
                    candidate.PrimaryVariety,
                    candidate.ExperienceYears,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null))
                .SingleOrDefaultAsync(cancellationToken);
        }
        else if (account.Role == UserRole.Buyer)
        {
            profile = await db.BuyerProfiles
                .AsNoTracking()
                .Where(candidate => candidate.UserId == memberId)
                .Select(candidate => new MemberProfileDetails(
                    "Buyer",
                    candidate.Address,
                    null,
                    null,
                    null,
                    candidate.BusinessName,
                    candidate.ContactPerson,
                    candidate.MonthlyDemandTonnes,
                    candidate.PreferredVarieties,
                    null,
                    null,
                    null,
                    null,
                    null))
                .SingleOrDefaultAsync(cancellationToken);
        }
        else if (account.Role == UserRole.Government)
        {
            profile = await db.GovernmentProfiles
                .AsNoTracking()
                .Where(candidate => candidate.UserId == memberId)
                .Select(candidate => new MemberProfileDetails(
                    "Government",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    candidate.Institution,
                    candidate.Department,
                    candidate.Designation,
                    candidate.District,
                    candidate.Province))
                .SingleOrDefaultAsync(cancellationToken);
        }

        var officerAssignments = await db.OfficerAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == memberId)
            .OrderByDescending(assignment => assignment.IsActive)
            .ThenBy(assignment => assignment.District)
            .Select(assignment => new MemberOfficerAssignment(
                assignment.District,
                assignment.Province,
                assignment.IsActive,
                assignment.AssignedDate))
            .ToListAsync(cancellationToken);

        var farmRows = await db.Farms
            .AsNoTracking()
            .Where(farm => farm.UserId == memberId)
            .OrderBy(farm => farm.Name)
            .Select(farm => new MemberFarmBase(
                farm.Id,
                farm.Name,
                farm.Description,
                farm.AreaHectares,
                farm.District,
                farm.Province,
                farm.Address,
                farm.CreatedAt))
            .ToListAsync(cancellationToken);

        var farmIds = farmRows.Select(farm => farm.Id).ToArray();
        var fields = new List<MemberField>();
        if (farmIds.Length > 0)
        {
            fields = await db.Fields
                .AsNoTracking()
                .Where(field => farmIds.Contains(field.FarmId))
                .OrderBy(field => field.Name)
                .Select(field => new MemberField(
                    field.Id,
                    field.FarmId,
                    field.Name,
                    field.AreaHectares,
                    field.Latitude,
                    field.Longitude,
                    field.Verifications
                        .OrderByDescending(verification => verification.CreatedAt)
                        .Select(verification => verification.Status)
                        .FirstOrDefault() ?? "Unverified"))
                .ToListAsync(cancellationToken);
        }
        var farms = farmRows
            .Select(farm => new MemberFarm(
                farm.Id,
                farm.Name,
                farm.Description,
                farm.AreaHectares,
                farm.District,
                farm.Province,
                farm.Address,
                farm.CreatedAt,
                fields.Where(field => field.FarmId == farm.Id).ToArray()))
            .ToArray();

        var cultivationRows = await db.CultivationRecords
            .AsNoTracking()
            .Where(record => record.FarmerId == memberId)
            .OrderByDescending(record => record.PlantingDate)
            .Select(record => new MemberCultivationBase(
                record.Id,
                record.Field.Name,
                record.Field.Farm.Name,
                record.RiceVariety.Name,
                record.Season,
                record.PlantingDate,
                record.ExpectedHarvestDate,
                record.AreaHectares,
                record.Status,
                record.Notes))
            .ToListAsync(cancellationToken);

        var cultivationIds = cultivationRows.Select(record => record.Id).ToArray();
        var forecasts = cultivationIds.Length == 0
            ? new List<MemberHarvestForecast>()
            : await db.HarvestForecasts
                .AsNoTracking()
                .Where(forecast => cultivationIds.Contains(forecast.CultivationId))
                .OrderByDescending(forecast => forecast.CreatedAt)
                .Select(forecast => new MemberHarvestForecast(
                    forecast.CultivationId,
                    forecast.TargetWindowStart,
                    forecast.TargetWindowEnd,
                    forecast.PredictedPaddyQuantityKg,
                    forecast.ConfidenceCategory))
                .ToListAsync(cancellationToken);
        var harvests = cultivationIds.Length == 0
            ? new List<MemberHarvestRecord>()
            : await db.HarvestRecords
                .AsNoTracking()
                .Where(harvest => cultivationIds.Contains(harvest.CultivationId))
                .OrderByDescending(harvest => harvest.ActualHarvestDate)
                .Select(harvest => new MemberHarvestRecord(
                    harvest.CultivationId,
                    harvest.ActualHarvestDate,
                    harvest.ActualPaddyQuantityKg,
                    harvest.Quality,
                    harvest.VerificationStatus))
                .ToListAsync(cancellationToken);
        var cultivations = cultivationRows
            .Select(record => new MemberCultivation(
                record.Id,
                record.FieldName,
                record.FarmName,
                record.VarietyName,
                record.Season,
                record.PlantingDate,
                record.ExpectedHarvestDate,
                record.AreaHectares,
                record.Status,
                record.Notes,
                forecasts.Where(forecast => forecast.CultivationId == record.Id).ToArray(),
                harvests.Where(harvest => harvest.CultivationId == record.Id).ToArray()))
            .ToArray();

        var demandRows = await db.Demands
            .AsNoTracking()
            .Where(demand => demand.OwnerId == memberId)
            .OrderByDescending(demand => demand.CreatedAt)
            .Select(demand => new MemberDemandBase(
                demand.Id,
                demand.District,
                demand.Season,
                demand.Status,
                demand.PeriodStart,
                demand.PeriodEnd,
                demand.Notes,
                demand.CreatedAt))
            .ToListAsync(cancellationToken);
        var demandIds = demandRows.Select(demand => demand.Id).ToArray();
        var demandItems = demandIds.Length == 0
            ? new List<MemberDemandItem>()
            : await db.DemandItems
                .AsNoTracking()
                .Where(item => demandIds.Contains(item.DemandId))
                .Select(item => new MemberDemandItem(
                    item.DemandId,
                    item.RiceVariety.Name,
                    item.Grade,
                    item.QuantityTonnes,
                    item.OfferPricePerKg))
                .ToListAsync(cancellationToken);
        var demands = demandRows
            .Select(demand => new MemberDemand(
                demand.Id,
                demand.District,
                demand.Season,
                demand.Status,
                demand.PeriodStart,
                demand.PeriodEnd,
                demand.Notes,
                demand.CreatedAt,
                demandItems.Where(item => item.DemandId == demand.Id).ToArray()))
            .ToArray();

        var purchaseRequests = await db.PurchaseRequests
            .AsNoTracking()
            .Where(request => request.BuyerId == memberId || request.FarmerId == memberId)
            .OrderByDescending(request => request.CreatedAt)
            .Select(request => new MemberPurchaseRequest(
                request.BuyerId == memberId ? "Buyer" : "Farmer",
                request.Buyer.FirstName + " " + request.Buyer.LastName,
                request.Farmer == null ? null : request.Farmer.FirstName + " " + request.Farmer.LastName,
                request.DemandItem.Demand.Season,
                request.DemandItem.RiceVariety.Name,
                request.DemandItem.Grade,
                request.QuantityKg,
                request.OfferPricePerKg,
                request.Status,
                request.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new MemberDetailsResponse(
            account,
            profile,
            officerAssignments,
            farms,
            cultivations,
            demands,
            purchaseRequests));
    }
}

public sealed record MemberAccountDetails(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string PhoneNumber,
    UserRole Role,
    bool IsActive,
    bool IsVerified,
    string? District,
    string? Province,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

public sealed record MemberProfileDetails(
    string Type,
    string? Address,
    decimal? TotalLandAreaHectares,
    string? PrimaryVariety,
    string? ExperienceYears,
    string? BusinessName,
    string? ContactPerson,
    decimal? MonthlyDemandTonnes,
    string? PreferredVarieties,
    string? Institution,
    string? Department,
    string? Designation,
    string? District,
    string? Province);

public sealed record MemberOfficerAssignment(
    string District,
    string? Province,
    bool IsActive,
    DateTime? AssignedDate);

public sealed record MemberFarm(
    Guid Id,
    string Name,
    string? Description,
    decimal AreaHectares,
    string? District,
    string? Province,
    string? Address,
    DateTime CreatedAt,
    IReadOnlyList<MemberField> Fields);

public sealed record MemberFarmBase(
    Guid Id,
    string Name,
    string? Description,
    decimal AreaHectares,
    string? District,
    string? Province,
    string? Address,
    DateTime CreatedAt);

public sealed record MemberField(
    Guid Id,
    Guid FarmId,
    string Name,
    decimal AreaHectares,
    double? Latitude,
    double? Longitude,
    string VerificationStatus);

public sealed record MemberCultivationBase(
    Guid Id,
    string FieldName,
    string FarmName,
    string VarietyName,
    string Season,
    DateOnly PlantingDate,
    DateOnly? ExpectedHarvestDate,
    decimal AreaHectares,
    string Status,
    string? Notes);

public sealed record MemberCultivation(
    Guid Id,
    string FieldName,
    string FarmName,
    string VarietyName,
    string Season,
    DateOnly PlantingDate,
    DateOnly? ExpectedHarvestDate,
    decimal AreaHectares,
    string Status,
    string? Notes,
    IReadOnlyList<MemberHarvestForecast> Forecasts,
    IReadOnlyList<MemberHarvestRecord> Harvests);

public sealed record MemberHarvestForecast(
    Guid CultivationId,
    DateOnly TargetWindowStart,
    DateOnly TargetWindowEnd,
    decimal PredictedPaddyQuantityKg,
    string ConfidenceCategory);

public sealed record MemberHarvestRecord(
    Guid CultivationId,
    DateOnly ActualHarvestDate,
    decimal ActualPaddyQuantityKg,
    string? Quality,
    string VerificationStatus);

public sealed record MemberDemandBase(
    Guid Id,
    string District,
    string Season,
    string Status,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Notes,
    DateTime CreatedAt);

public sealed record MemberDemand(
    Guid Id,
    string District,
    string Season,
    string Status,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<MemberDemandItem> Items);

public sealed record MemberDemandItem(
    Guid DemandId,
    string VarietyName,
    string Grade,
    decimal QuantityTonnes,
    decimal? OfferPricePerKg);

public sealed record MemberPurchaseRequest(
    string MemberRole,
    string BuyerName,
    string? FarmerName,
    string Season,
    string VarietyName,
    string Grade,
    decimal QuantityKg,
    decimal OfferPricePerKg,
    string Status,
    DateTime CreatedAt);

public sealed record MemberDetailsResponse(
    MemberAccountDetails Account,
    MemberProfileDetails? Profile,
    IReadOnlyList<MemberOfficerAssignment> OfficerAssignments,
    IReadOnlyList<MemberFarm> Farms,
    IReadOnlyList<MemberCultivation> Cultivations,
    IReadOnlyList<MemberDemand> Demands,
    IReadOnlyList<MemberPurchaseRequest> PurchaseRequests);
