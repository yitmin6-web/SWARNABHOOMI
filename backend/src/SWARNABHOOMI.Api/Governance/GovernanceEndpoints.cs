using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Services;

namespace SWARNABHOOMI.Api.Governance;

public static class GovernanceEndpoints
{
    public static IEndpointRouteBuilder MapGovernanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var governance = endpoints.MapGroup("/api").RequireAuthorization();

        // Officer District Assignments
        governance.MapGet("/officer-assignments", GetOfficerAssignments);
        governance.MapPost("/officer-assignments", AssignOfficerDistrict);
        governance.MapDelete("/officer-assignments/{id:guid}", RemoveOfficerAssignment);

        // In-App Notifications
        governance.MapGet("/notifications", GetNotifications);
        governance.MapPut("/notifications/{id:guid}/read", MarkNotificationRead);
        governance.MapPut("/notifications/read-all", MarkAllNotificationsRead);

        return endpoints;
    }

    private static async Task<IResult> GetOfficerAssignments(
        ClaimsPrincipal principal,
        IGovernanceNotificationService governanceService,
        CancellationToken cancellationToken)
    {
        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Results.Unauthorized();
        }

        var roleString = principal.FindFirstValue(ClaimTypes.Role);
        if (!Enum.TryParse<UserRole>(roleString, out var role))
        {
            return Results.Forbid();
        }

        if (role is not (UserRole.Officer or UserRole.Government or UserRole.SuperAdmin or UserRole.Developer))
        {
            return Results.Forbid();
        }

        var assignments = await governanceService.GetOfficerAssignmentsAsync(userId, role, cancellationToken);
        var assignableOfficers = (role is UserRole.Government or UserRole.SuperAdmin or UserRole.Developer)
            ? await governanceService.GetAssignableOfficersAsync(cancellationToken)
            : [];

        return Results.Ok(new
        {
            assignments,
            assignableOfficers
        });
    }

    private static async Task<IResult> AssignOfficerDistrict(
        [FromBody] CreateOfficerAssignmentDto request,
        ClaimsPrincipal principal,
        IGovernanceNotificationService governanceService,
        CancellationToken cancellationToken)
    {
        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Results.Unauthorized();
        }

        var roleString = principal.FindFirstValue(ClaimTypes.Role);
        if (!Enum.TryParse<UserRole>(roleString, out var role))
        {
            return Results.Forbid();
        }

        if (role is not (UserRole.Government or UserRole.SuperAdmin or UserRole.Developer))
        {
            return Results.Forbid();
        }

        if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.District))
        {
            return Results.BadRequest(new { error = "Select an officer and specify a district." });
        }

        try
        {
            var assignment = await governanceService.AssignOfficerDistrictAsync(
                request,
                userId,
                role,
                cancellationToken);

            return Results.Ok(assignment);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> RemoveOfficerAssignment(
        Guid id,
        ClaimsPrincipal principal,
        IGovernanceNotificationService governanceService,
        CancellationToken cancellationToken)
    {
        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Results.Unauthorized();
        }

        var roleString = principal.FindFirstValue(ClaimTypes.Role);
        if (!Enum.TryParse<UserRole>(roleString, out var role))
        {
            return Results.Forbid();
        }

        if (role is not (UserRole.Government or UserRole.SuperAdmin or UserRole.Developer))
        {
            return Results.Forbid();
        }

        var removed = await governanceService.RemoveOfficerAssignmentAsync(
            id,
            userId,
            role,
            cancellationToken);

        if (!removed)
        {
            return Results.NotFound(new { error = "Officer assignment not found." });
        }

        return Results.NoContent();
    }

    private static async Task<IResult> GetNotifications(
        ClaimsPrincipal principal,
        IGovernanceNotificationService governanceService,
        CancellationToken cancellationToken)
    {
        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Results.Unauthorized();
        }

        var roleString = principal.FindFirstValue(ClaimTypes.Role);
        if (!Enum.TryParse<UserRole>(roleString, out var role))
        {
            return Results.Forbid();
        }

        var result = await governanceService.GetNotificationsAsync(userId, role, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> MarkNotificationRead(
        Guid id,
        ClaimsPrincipal principal,
        IGovernanceNotificationService governanceService,
        CancellationToken cancellationToken)
    {
        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Results.Unauthorized();
        }

        var updated = await governanceService.MarkNotificationReadAsync(userId, id, cancellationToken);
        if (!updated)
        {
            return Results.NotFound(new { error = "Notification not found." });
        }

        return Results.NoContent();
    }

    private static async Task<IResult> MarkAllNotificationsRead(
        ClaimsPrincipal principal,
        IGovernanceNotificationService governanceService,
        CancellationToken cancellationToken)
    {
        var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdString, out var userId))
        {
            return Results.Unauthorized();
        }

        var count = await governanceService.MarkAllNotificationsReadAsync(userId, cancellationToken);
        return Results.Ok(new { count });
    }
}
