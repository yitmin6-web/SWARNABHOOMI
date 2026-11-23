using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Services;

public sealed record OfficerAssignmentDto(
    Guid Id,
    Guid UserId,
    string OfficerName,
    string OfficerEmail,
    string District,
    string? Province,
    string? AssignedBy,
    DateTime? AssignedDate,
    bool IsActive,
    DateTime CreatedAt);

public sealed record OfficerUserDto(
    Guid Id,
    string Name,
    string Email,
    string? District,
    string? Province);

public sealed record CreateOfficerAssignmentDto(
    Guid UserId,
    string District,
    string? Province = null);

public sealed record NotificationDto(
    Guid Id,
    Guid UserId,
    string Type,
    string Title,
    string Message,
    string? Category,
    string? Link,
    bool IsRead,
    DateTime? ReadAt,
    DateTime CreatedAt);

public sealed record NotificationListDto(
    int UnreadCount,
    IReadOnlyList<NotificationDto> Items);

public interface IGovernanceNotificationService
{
    Task<IReadOnlyList<OfficerAssignmentDto>> GetOfficerAssignmentsAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OfficerUserDto>> GetAssignableOfficersAsync(
        CancellationToken cancellationToken = default);

    Task<OfficerAssignmentDto> AssignOfficerDistrictAsync(
        CreateOfficerAssignmentDto dto,
        Guid assignedByUserId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveOfficerAssignmentAsync(
        Guid assignmentId,
        Guid actorUserId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetAssignedDistrictsForOfficerAsync(
        Guid officerUserId,
        CancellationToken cancellationToken = default);

    Task<NotificationListDto> GetNotificationsAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<bool> MarkNotificationReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default);

    Task<int> MarkAllNotificationsReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<int> GetUnreadNotificationCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task NotifyFieldVerificationDecisionAsync(
        Guid fieldId,
        bool approved,
        string? notes,
        CancellationToken cancellationToken = default);

    Task NotifyPurchaseRequestSubmittedAsync(
        Guid purchaseRequestId,
        Guid? targetFarmerId,
        string district,
        string varietyName,
        decimal quantityKg,
        CancellationToken cancellationToken = default);

    Task NotifyUpcomingHarvestWindowAsync(
        Guid farmerUserId,
        Guid cultivationId,
        string fieldName,
        string varietyName,
        DateTime expectedHarvestDate,
        CancellationToken cancellationToken = default);

    Task EnsureUpcomingHarvestNotificationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class GovernanceNotificationService(SWARNABHOOMIContext db) : IGovernanceNotificationService
{
    private sealed record NotificationPayload(
        string? Title,
        string? Message,
        string? Category,
        string? Link,
        string? ReferenceId);

    // -------------------------------------------------------------
    // Officer District Assignments
    // -------------------------------------------------------------

    public async Task<IReadOnlyList<OfficerAssignmentDto>> GetOfficerAssignmentsAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        var query = db.OfficerAssignments
            .AsNoTracking()
            .Include(oa => oa.User)
            .Where(oa => oa.IsActive);

        if (role == UserRole.Officer)
        {
            query = query.Where(oa => oa.UserId == userId);
        }
        else if (role is not (UserRole.SuperAdmin or UserRole.Developer or UserRole.Government))
        {
            throw new UnauthorizedAccessException("Unauthorized to access officer assignments.");
        }

        var list = await query
            .OrderBy(oa => oa.District)
            .ThenBy(oa => oa.User.FirstName)
            .ToListAsync(cancellationToken);

        return list.Select(oa => new OfficerAssignmentDto(
            oa.Id,
            oa.UserId,
            $"{oa.User.FirstName} {oa.User.LastName}".Trim(),
            oa.User.Email,
            oa.District,
            oa.Province,
            oa.AssignedBy,
            oa.AssignedDate,
            oa.IsActive,
            oa.CreatedAt
        )).ToList();
    }

    public async Task<IReadOnlyList<OfficerUserDto>> GetAssignableOfficersAsync(
        CancellationToken cancellationToken = default)
    {
        var officers = await db.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Officer && u.IsActive)
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .ToListAsync(cancellationToken);

        return officers.Select(u => new OfficerUserDto(
            u.Id,
            $"{u.FirstName} {u.LastName}".Trim(),
            u.Email,
            u.District,
            u.Province
        )).ToList();
    }

    public async Task<OfficerAssignmentDto> AssignOfficerDistrictAsync(
        CreateOfficerAssignmentDto dto,
        Guid assignedByUserId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role is not (UserRole.SuperAdmin or UserRole.Developer or UserRole.Government))
        {
            throw new UnauthorizedAccessException("Unauthorized to assign officer districts.");
        }

        if (dto.UserId == Guid.Empty || string.IsNullOrWhiteSpace(dto.District))
        {
            throw new ArgumentException("A valid officer and district are required.");
        }

        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == dto.UserId, cancellationToken);

        if (user == null || user.Role != UserRole.Officer)
        {
            throw new InvalidOperationException("Target user not found or is not registered as an Agrarian Officer.");
        }

        var district = dto.District.Trim();
        var province = string.IsNullOrWhiteSpace(dto.Province) ? user.Province : dto.Province.Trim();

        var existing = await db.OfficerAssignments
            .FirstOrDefaultAsync(oa => oa.UserId == dto.UserId &&
                                       oa.District.ToLower() == district.ToLower() &&
                                       oa.IsActive,
                                 cancellationToken);

        if (existing != null)
        {
            return new OfficerAssignmentDto(
                existing.Id,
                existing.UserId,
                $"{user.FirstName} {user.LastName}".Trim(),
                user.Email,
                existing.District,
                existing.Province,
                existing.AssignedBy,
                existing.AssignedDate,
                existing.IsActive,
                existing.CreatedAt
            );
        }

        var assignment = new OfficerAssignment
        {
            UserId = dto.UserId,
            District = district,
            Province = province,
            AssignedBy = assignedByUserId.ToString(),
            AssignedDate = DateTime.UtcNow,
            IsActive = true,
            CreatedBy = assignedByUserId.ToString()
        };

        db.OfficerAssignments.Add(assignment);

        db.AuditLogs.Add(new AuditLog
        {
            Actor = assignedByUserId.ToString(),
            Action = "OfficerDistrictAssigned",
            EntityType = nameof(OfficerAssignment),
            EntityId = assignment.Id.ToString(),
            Result = "Success",
            Reason = $"Assigned officer {user.FirstName} {user.LastName} to district {district} ({province})"
        });

        await db.SaveChangesAsync(cancellationToken);

        return new OfficerAssignmentDto(
            assignment.Id,
            assignment.UserId,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.Email,
            assignment.District,
            assignment.Province,
            assignment.AssignedBy,
            assignment.AssignedDate,
            assignment.IsActive,
            assignment.CreatedAt
        );
    }

    public async Task<bool> RemoveOfficerAssignmentAsync(
        Guid assignmentId,
        Guid actorUserId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role is not (UserRole.SuperAdmin or UserRole.Developer or UserRole.Government))
        {
            throw new UnauthorizedAccessException("Unauthorized to remove officer assignments.");
        }

        var assignment = await db.OfficerAssignments
            .Include(oa => oa.User)
            .SingleOrDefaultAsync(oa => oa.Id == assignmentId, cancellationToken);

        if (assignment == null)
        {
            return false;
        }

        assignment.IsActive = false;
        assignment.UpdatedAt = DateTime.UtcNow;
        assignment.UpdatedBy = actorUserId.ToString();

        db.AuditLogs.Add(new AuditLog
        {
            Actor = actorUserId.ToString(),
            Action = "OfficerDistrictUnassigned",
            EntityType = nameof(OfficerAssignment),
            EntityId = assignment.Id.ToString(),
            Result = "Success",
            Reason = $"Deactivated officer assignment for {assignment.User?.FirstName} {assignment.User?.LastName} in {assignment.District}"
        });

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<string>> GetAssignedDistrictsForOfficerAsync(
        Guid officerUserId,
        CancellationToken cancellationToken = default)
    {
        return await db.OfficerAssignments
            .AsNoTracking()
            .Where(oa => oa.UserId == officerUserId && oa.IsActive && !string.IsNullOrEmpty(oa.District))
            .Select(oa => oa.District)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    // -------------------------------------------------------------
    // In-App Notifications
    // -------------------------------------------------------------

    public async Task<NotificationListDto> GetNotificationsAsync(
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        // 1. If Farmer, run idempotent 14-day upcoming harvest check first
        if (role == UserRole.Farmer)
        {
            await EnsureUpcomingHarvestNotificationsAsync(userId, cancellationToken);
        }

        // 2. Fetch latest 30 notifications
        var notifications = await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(30)
            .ToListAsync(cancellationToken);

        var unreadCount = await db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);

        var dtoList = notifications.Select(MapToDto).ToList();

        return new NotificationListDto(unreadCount, dtoList);
    }

    public async Task<bool> MarkNotificationReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var notification = await db.Notifications
            .SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);

        if (notification == null)
        {
            return false;
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            notification.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<int> MarkAllNotificationsReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var unread = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
            n.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return unread.Count;
    }

    public async Task<int> GetUnreadNotificationCountAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);
    }

    // -------------------------------------------------------------
    // Automated Event Triggers
    // -------------------------------------------------------------

    public async Task NotifyFieldVerificationDecisionAsync(
        Guid fieldId,
        bool approved,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var field = await db.Fields
            .AsNoTracking()
            .Include(f => f.Farm)
            .SingleOrDefaultAsync(f => f.Id == fieldId, cancellationToken);

        if (field == null)
        {
            return;
        }

        var farmerUserId = field.Farm.UserId;
        var title = approved ? "Field Verification Approved" : "Field Verification Update";
        var statusLabel = approved ? "Verified" : "Rejected";
        var message = approved
            ? $"Your field '{field.Name}' ({field.AreaHectares:N2} ha) in {field.Farm.District ?? "your farm"} has been verified and approved by the Agrarian Services Officer."
            : $"Your field '{field.Name}' verification status is '{statusLabel}'. {(string.IsNullOrWhiteSpace(notes) ? "" : $"Officer note: {notes.Trim()}")}";

        var payload = JsonSerializer.Serialize(new NotificationPayload(
            title,
            message,
            "Field Verification",
            "/field-map",
            fieldId.ToString()
        ));

        var notification = new Notification
        {
            UserId = farmerUserId,
            Type = "FieldVerification",
            MessageKey = title,
            Payload = payload,
            IsRead = false,
            DeliveryStatus = "Delivered",
            CreatedBy = "System"
        };

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyPurchaseRequestSubmittedAsync(
        Guid purchaseRequestId,
        Guid? targetFarmerId,
        string district,
        string varietyName,
        decimal quantityKg,
        CancellationToken cancellationToken = default)
    {
        var targetUserIds = new HashSet<Guid>();

        if (targetFarmerId.HasValue && targetFarmerId.Value != Guid.Empty)
        {
            targetUserIds.Add(targetFarmerId.Value);
        }
        else
        {
            // Open pool request: find farmers in the district who cultivate or store this variety
            var districtLower = district.Trim().ToLower();

            var matchingStockFarmers = await db.StockRecords
                .AsNoTracking()
                .Where(s => s.RiceVariety.Name.ToLower() == varietyName.ToLower() &&
                            s.Quantity > 0 &&
                            s.CreatedBy != null)
                .Select(s => s.CreatedBy!)
                .ToListAsync(cancellationToken);

            foreach (var createdBy in matchingStockFarmers)
            {
                if (Guid.TryParse(createdBy, out var farmerId))
                {
                    targetUserIds.Add(farmerId);
                }
            }

            var matchingCultivationFarmers = await db.CultivationRecords
                .AsNoTracking()
                .Where(cr => cr.Field.Farm.District != null &&
                            cr.Field.Farm.District.ToLower() == districtLower &&
                            cr.RiceVariety.Name.ToLower() == varietyName.ToLower() &&
                            cr.Status != "Cancelled")
                .Select(cr => cr.FarmerId)
                .Take(20)
                .ToListAsync(cancellationToken);

            foreach (var farmerId in matchingCultivationFarmers)
            {
                targetUserIds.Add(farmerId);
            }
        }

        if (targetUserIds.Count == 0)
        {
            return;
        }

        var title = "Incoming Purchase Order";
        var message = $"A commercial buyer has submitted a purchase request for {quantityKg:N0} kg of {varietyName} in {district}. Review terms and accept in the Marketplace.";
        var payload = JsonSerializer.Serialize(new NotificationPayload(
            title,
            message,
            "Purchase Request",
            "/demands",
            purchaseRequestId.ToString()
        ));

        foreach (var farmerId in targetUserIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = farmerId,
                Type = "PurchaseRequest",
                MessageKey = title,
                Payload = payload,
                IsRead = false,
                DeliveryStatus = "Delivered",
                CreatedBy = "MarketplaceEngine"
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyUpcomingHarvestWindowAsync(
        Guid farmerUserId,
        Guid cultivationId,
        string fieldName,
        string varietyName,
        DateTime expectedHarvestDate,
        CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;
        var windowEnd = today.AddDays(14);
        if (expectedHarvestDate.Date < today || expectedHarvestDate.Date > windowEnd)
        {
            return;
        }

        var cultIdStr = cultivationId.ToString();

        // Idempotency check: don't create duplicate notification if one already exists recently
        var existing = await db.Notifications
            .AsNoTracking()
            .AnyAsync(n => n.UserId == farmerUserId &&
                           n.Type == "HarvestAlert" &&
                           n.Payload != null && n.Payload.Contains(cultIdStr) &&
                           (!n.IsRead || n.CreatedAt >= DateTime.UtcNow.AddDays(-14)),
                      cancellationToken);

        if (existing)
        {
            return;
        }

        var title = "Harvest Window Approaching";
        var daysLeft = Math.Max(0, (expectedHarvestDate.Date - today).Days);
        var daysText = daysLeft == 0 ? "today" : daysLeft == 1 ? "tomorrow" : $"in {daysLeft} days";
        var message = $"Your {varietyName} on field '{fieldName}' is expected to reach harvest maturity {daysText} ({expectedHarvestDate:MMM dd, yyyy}). Prepare harvesting equipment and log yield upon completion.";

        var payload = JsonSerializer.Serialize(new NotificationPayload(
            title,
            message,
            "Harvest Alert",
            "/cultivation",
            cultIdStr
        ));

        db.Notifications.Add(new Notification
        {
            UserId = farmerUserId,
            Type = "HarvestAlert",
            MessageKey = title,
            Payload = payload,
            IsRead = false,
            DeliveryStatus = "Delivered",
            CreatedBy = "RiceIntelligenceEngine"
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureUpcomingHarvestNotificationsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var windowEnd = today.AddDays(14);

        var upcomingCultivations = await db.CultivationRecords
            .AsNoTracking()
            .Include(cr => cr.Field)
                .ThenInclude(f => f.Farm)
            .Include(cr => cr.RiceVariety)
            .Where(cr => cr.FarmerId == userId &&
                         cr.Status != "Harvested" &&
                         cr.Status != "Cancelled" &&
                         cr.ExpectedHarvestDate.HasValue &&
                         cr.ExpectedHarvestDate.Value >= today &&
                         cr.ExpectedHarvestDate.Value <= windowEnd)
            .ToListAsync(cancellationToken);

        foreach (var cult in upcomingCultivations)
        {
            var harvestDate = cult.ExpectedHarvestDate!.Value;
            await NotifyUpcomingHarvestWindowAsync(
                userId,
                cult.Id,
                cult.Field.Name,
                cult.RiceVariety.Name,
                harvestDate.ToDateTime(TimeOnly.MinValue),
                cancellationToken);
        }
    }

    private static NotificationDto MapToDto(Notification n)
    {
        string title = n.MessageKey;
        string message = n.MessageKey;
        string? category = n.Type;
        string? link = null;

        if (!string.IsNullOrWhiteSpace(n.Payload) && n.Payload.TrimStart().StartsWith('{'))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<NotificationPayload>(n.Payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (payload != null)
                {
                    title = payload.Title ?? title;
                    message = payload.Message ?? message;
                    category = payload.Category ?? category;
                    link = payload.Link;
                }
            }
            catch
            {
                message = n.Payload;
            }
        }
        else if (!string.IsNullOrWhiteSpace(n.Payload))
        {
            message = n.Payload;
        }

        return new NotificationDto(
            n.Id,
            n.UserId,
            n.Type,
            title,
            message,
            category,
            link,
            n.IsRead,
            n.ReadAt,
            n.CreatedAt
        );
    }
}
