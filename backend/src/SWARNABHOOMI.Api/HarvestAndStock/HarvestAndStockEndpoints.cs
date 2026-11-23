using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Services;

namespace SWARNABHOOMI.Api.HarvestAndStock;

public static class HarvestAndStockEndpoints
{
    public static IEndpointRouteBuilder MapHarvestAndStockEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var harvests = endpoints.MapGroup("/api/harvests").RequireAuthorization();
        harvests.MapGet("/", GetHarvests);
        harvests.MapPost("/", LogHarvest);

        var stocks = endpoints.MapGroup("/api/stocks").RequireAuthorization();
        stocks.MapGet("/", GetStocks);
        stocks.MapPost("/", UpsertStock);

        return endpoints;
    }

    private static async Task<IResult> GetHarvests(
        [FromQuery] Guid? seasonId,
        [FromQuery] string? season,
        [FromQuery] string? province,
        [FromQuery] string? district,
        [FromQuery] Guid? varietyId,
        ClaimsPrincipal principal,
        IHarvestAndStockService harvestService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        if (role == UserRole.Buyer)
        {
            return Results.Forbid();
        }

        var filter = new HarvestQueryFilter(
            SeasonId: seasonId,
            Season: season,
            Province: province,
            District: district,
            VarietyId: varietyId);

        var results = await harvestService.GetHarvestsAsync(filter, userId, role, cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> LogHarvest(
        LogHarvestDto request,
        ClaimsPrincipal principal,
        IHarvestAndStockService harvestService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        if (role is not (UserRole.Farmer or UserRole.SuperAdmin or UserRole.Developer))
        {
            return Results.Forbid();
        }

        try
        {
            var record = await harvestService.LogHarvestAsync(request, userId, role, cancellationToken);
            return Results.Created($"/api/harvests/{record.HarvestId}", record);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: 403);
        }
    }

    private static async Task<IResult> GetStocks(
        [FromQuery] string? province,
        [FromQuery] string? district,
        [FromQuery] Guid? varietyId,
        ClaimsPrincipal principal,
        IHarvestAndStockService harvestService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        var filter = new StockQueryFilter(
            Province: province,
            District: district,
            VarietyId: varietyId);

        var overview = await harvestService.GetStocksAsync(filter, userId, role, cancellationToken);
        return Results.Ok(overview);
    }

    private static async Task<IResult> UpsertStock(
        UpsertStockDto request,
        ClaimsPrincipal principal,
        IHarvestAndStockService harvestService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        if (role is not (UserRole.Farmer or UserRole.Officer or UserRole.SuperAdmin or UserRole.Developer))
        {
            return Results.Forbid();
        }

        try
        {
            var record = await harvestService.UpsertStockAsync(request, userId, role, cancellationToken);
            return Results.Ok(record);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: 403);
        }
    }

    private static bool TryGetCaller(ClaimsPrincipal principal, out Guid userId, out UserRole role)
    {
        userId = Guid.Empty;
        role = UserRole.Farmer;

        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var roleString = principal.FindFirstValue(ClaimTypes.Role);

        return Guid.TryParse(userIdString, out userId) &&
               Enum.TryParse(roleString, out role);
    }
}
