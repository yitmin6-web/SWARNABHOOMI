using System.Security.Claims;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Services;

namespace SWARNABHOOMI.Api.Cultivation;

public static class CultivationEndpoints
{
    public static IEndpointRouteBuilder MapCultivationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var seasons = endpoints.MapGroup("/api/seasons").RequireAuthorization();
        seasons.MapGet("/", GetSeasons);
        seasons.MapPost("/", CreateSeason);
        seasons.MapPut("/{id:guid}", UpdateSeason);

        var cultivations = endpoints.MapGroup("/api/cultivations").RequireAuthorization();
        cultivations.MapGet("/", GetCultivations);
        cultivations.MapPost("/", CreateCultivation);
        cultivations.MapPut("/{id:guid}/stage", UpdateGrowthStage);
        cultivations.MapPatch("/{id:guid}/stage", UpdateGrowthStage);

        return endpoints;
    }

    private static async Task<IResult> GetSeasons(
        ICultivationService cultivationService,
        CancellationToken cancellationToken)
    {
        var seasons = await cultivationService.GetSeasonsAsync(cancellationToken);
        return Results.Ok(seasons);
    }

    private static async Task<IResult> CreateSeason(
        CreateSeasonDto request,
        ClaimsPrincipal principal,
        ICultivationService cultivationService,
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
            var season = await cultivationService.CreateSeasonAsync(request, userId, role, cancellationToken);
            return Results.Created($"/api/seasons/{season.Id}", season);
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

    private static async Task<IResult> UpdateSeason(
        Guid id,
        UpdateSeasonDto request,
        ClaimsPrincipal principal,
        ICultivationService cultivationService,
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
            var season = await cultivationService.UpdateSeasonAsync(id, request, userId, role, cancellationToken);
            return Results.Ok(season);
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

    private static async Task<IResult> GetCultivations(
        [AsParameters] CultivationQueryFilter filter,
        ClaimsPrincipal principal,
        ICultivationService cultivationService,
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

        var records = await cultivationService.GetCultivationsAsync(filter, userId, role, cancellationToken);
        return Results.Ok(records);
    }

    private static async Task<IResult> CreateCultivation(
        CreateCultivationDto request,
        ClaimsPrincipal principal,
        ICultivationService cultivationService,
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
            var record = await cultivationService.CreateCultivationAsync(request, userId, role, cancellationToken);
            return Results.Created($"/api/cultivations/{record.Id}", record);
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

    private static async Task<IResult> UpdateGrowthStage(
        Guid id,
        UpdateGrowthStageDto request,
        ClaimsPrincipal principal,
        ICultivationService cultivationService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCaller(principal, out var userId, out var role))
        {
            return Results.Forbid();
        }

        try
        {
            var record = await cultivationService.UpdateGrowthStageAsync(id, request, userId, role, cancellationToken);
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
