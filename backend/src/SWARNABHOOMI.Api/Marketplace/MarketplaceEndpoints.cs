using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Services;

namespace SWARNABHOOMI.Api.Marketplace;

public static class MarketplaceEndpoints
{
    public static IEndpointRouteBuilder MapMarketplaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var demands = endpoints.MapGroup("/api/demands").RequireAuthorization();
        demands.MapGet("/", GetDemands);
        demands.MapPost("/", CreateDemand);
        demands.MapPut("/{id:guid}/status", UpdateDemandStatus);

        var marketplace = endpoints.MapGroup("/api/marketplace").RequireAuthorization();
        marketplace.MapGet("/supply", GetMarketplaceSupply);

        var purchaseRequests = endpoints.MapGroup("/api/purchase-requests").RequireAuthorization();
        purchaseRequests.MapGet("/", GetPurchaseRequests);
        purchaseRequests.MapPost("/", CreatePurchaseRequest);
        purchaseRequests.MapPut("/{id:guid}/status", UpdatePurchaseRequestStatus);

        return endpoints;
    }

    private static async Task<IResult> GetDemands(
        [FromQuery] string? district,
        [FromQuery] string? season,
        [FromQuery] string? status,
        [FromQuery] Guid? varietyId,
        ClaimsPrincipal principal,
        IMarketplaceService marketplaceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        var filter = new DemandQueryFilter(
            District: district,
            Season: season,
            Status: status,
            VarietyId: varietyId);

        var demands = await marketplaceService.GetDemandsAsync(filter, userId, role, cancellationToken);
        return Results.Ok(demands);
    }

    private static async Task<IResult> CreateDemand(
        CreateDemandDto request,
        ClaimsPrincipal principal,
        IMarketplaceService marketplaceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        if (role is not (UserRole.Buyer or UserRole.SuperAdmin or UserRole.Developer))
        {
            return Results.Forbid();
        }

        try
        {
            var created = await marketplaceService.CreateDemandAsync(request, userId, role, cancellationToken);
            return Results.Created($"/api/demands/{created.Id}", created);
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

    private static async Task<IResult> UpdateDemandStatus(
        Guid id,
        UpdateDemandStatusDto request,
        ClaimsPrincipal principal,
        IMarketplaceService marketplaceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        try
        {
            var updated = await marketplaceService.UpdateDemandStatusAsync(id, request, userId, role, cancellationToken);
            return Results.Ok(updated);
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

    private static async Task<IResult> GetMarketplaceSupply(
        [FromQuery] string? province,
        [FromQuery] string? district,
        [FromQuery] Guid? varietyId,
        ClaimsPrincipal principal,
        IMarketplaceService marketplaceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        var filter = new MarketplaceSupplyFilter(
            Province: province,
            District: district,
            VarietyId: varietyId);

        var overview = await marketplaceService.GetMarketplaceSupplyAsync(filter, userId, role, cancellationToken);
        return Results.Ok(overview);
    }

    private static async Task<IResult> GetPurchaseRequests(
        [FromQuery] string? status,
        [FromQuery] string? district,
        [FromQuery] Guid? varietyId,
        ClaimsPrincipal principal,
        IMarketplaceService marketplaceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        var filter = new PurchaseRequestFilter(
            Status: status,
            District: district,
            VarietyId: varietyId);

        var requests = await marketplaceService.GetPurchaseRequestsAsync(filter, userId, role, cancellationToken);
        return Results.Ok(requests);
    }

    private static async Task<IResult> CreatePurchaseRequest(
        CreatePurchaseRequestDto request,
        ClaimsPrincipal principal,
        IMarketplaceService marketplaceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        if (role is not (UserRole.Buyer or UserRole.SuperAdmin or UserRole.Developer))
        {
            return Results.Forbid();
        }

        try
        {
            var created = await marketplaceService.CreatePurchaseRequestAsync(request, userId, role, cancellationToken);
            return Results.Created($"/api/purchase-requests/{created.Id}", created);
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

    private static async Task<IResult> UpdatePurchaseRequestStatus(
        Guid id,
        UpdatePurchaseRequestStatusDto request,
        ClaimsPrincipal principal,
        IMarketplaceService marketplaceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        try
        {
            var updated = await marketplaceService.UpdatePurchaseRequestStatusAsync(id, request, userId, role, cancellationToken);
            return Results.Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
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
