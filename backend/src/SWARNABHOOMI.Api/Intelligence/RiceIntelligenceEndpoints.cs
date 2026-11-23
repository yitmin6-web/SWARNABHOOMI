using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Services;

namespace SWARNABHOOMI.Api.Intelligence;

public static class RiceIntelligenceEndpoints
{
    public static IEndpointRouteBuilder MapRiceIntelligenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var intelligence = endpoints.MapGroup("/api/intelligence").RequireAuthorization();

        intelligence.MapGet("/resource-rules", GetResourceRules);
        intelligence.MapPost("/resource-rules", CreateResourceRule);
        intelligence.MapGet("/forecasts", GetForecasts);
        intelligence.MapPost("/forecasts/recalculate", RecalculateForecasts);

        return endpoints;
    }

    private static async Task<IResult> GetResourceRules(
        IRiceIntelligenceService intelligenceService,
        CancellationToken cancellationToken)
    {
        var rules = await intelligenceService.GetResourceRulesAsync(cancellationToken);
        return Results.Ok(rules);
    }

    private static async Task<IResult> CreateResourceRule(
        CreateResourceRuleDto request,
        ClaimsPrincipal principal,
        IRiceIntelligenceService intelligenceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        if (role is not (UserRole.SuperAdmin or UserRole.Developer or UserRole.Government))
        {
            return Results.Forbid();
        }

        try
        {
            var rule = await intelligenceService.CreateResourceRuleAsync(request, userId, role, cancellationToken);
            return Results.Created($"/api/intelligence/resource-rules/{rule.Id}", rule);
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

    private static async Task<IResult> GetForecasts(
        [FromQuery] Guid? seasonId,
        [FromQuery] string? season,
        [FromQuery] string? province,
        [FromQuery] string? district,
        [FromQuery] Guid? varietyId,
        ClaimsPrincipal principal,
        IRiceIntelligenceService intelligenceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        var filter = new IntelligenceQueryFilter(
            SeasonId: seasonId,
            Season: season,
            Province: province,
            District: district,
            VarietyId: varietyId);

        var overview = await intelligenceService.GetIntelligenceOverviewAsync(
            filter,
            userId,
            role,
            cancellationToken);

        return Results.Ok(overview);
    }

    private static async Task<IResult> RecalculateForecasts(
        ClaimsPrincipal principal,
        IRiceIntelligenceService intelligenceService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        var count = await intelligenceService.RecalculateForecastsAsync(userId, role, cancellationToken);
        return Results.Ok(new { updatedCount = count });
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
