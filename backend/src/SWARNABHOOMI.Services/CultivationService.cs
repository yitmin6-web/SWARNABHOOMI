using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Services;

public sealed record SeasonDto(
    Guid Id,
    string Name,
    int Year,
    string? District,
    string? AgrarianServiceCentre,
    DateOnly PlantingStart,
    DateOnly PlantingEnd,
    string Status,
    bool IsActive,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    DateTime? ClosedAt,
    string? ClosureReason,
    DateTime CreatedAt);

public sealed record CreateSeasonDto(
    string Name,
    int Year,
    string? District,
    string? AgrarianServiceCentre,
    DateOnly PlantingStart,
    DateOnly PlantingEnd,
    bool IsActive = true,
    string? Status = null);

public sealed record UpdateSeasonDto(
    string? Name,
    int? Year,
    string? District,
    DateOnly? PlantingStart,
    DateOnly? PlantingEnd,
    string? Status,
    bool? IsActive,
    string? ClosureReason);

public sealed record CultivationQueryFilter(
    Guid? SeasonId = null,
    string? Season = null,
    string? Province = null,
    string? District = null);

public sealed record CultivationRecordDto(
    Guid Id,
    Guid FarmerId,
    string FarmerName,
    Guid FieldId,
    string FieldName,
    Guid FarmId,
    string FarmName,
    string? District,
    string? Province,
    decimal AreaHectares,
    string FieldVerificationStatus,
    Guid RiceVarietyId,
    string RiceVarietyName,
    string VarietyCode,
    int MaturityDays,
    string Season,
    DateOnly PlantingDate,
    DateOnly ExpectedHarvestDate,
    string Status,
    string? Notes,
    DateTime CreatedAt);

public sealed record CreateCultivationDto(
    Guid FieldId,
    Guid RiceVarietyId,
    Guid? SeasonId,
    string? Season,
    DateOnly PlantingDate,
    DateOnly? ExpectedHarvestDate = null,
    decimal? AreaHectares = null,
    string? Status = "Planned",
    string? Notes = null);

public sealed record UpdateGrowthStageDto(
    string Stage,
    string? Notes = null);

public interface ICultivationService
{
    Task<IReadOnlyList<SeasonDto>> GetSeasonsAsync(
        CancellationToken cancellationToken = default);

    Task<SeasonDto> CreateSeasonAsync(
        CreateSeasonDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<SeasonDto> UpdateSeasonAsync(
        Guid seasonId,
        UpdateSeasonDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CultivationRecordDto>> GetCultivationsAsync(
        CultivationQueryFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<CultivationRecordDto> CreateCultivationAsync(
        CreateCultivationDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);

    Task<CultivationRecordDto> UpdateGrowthStageAsync(
        Guid id,
        UpdateGrowthStageDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default);
}

public sealed class CultivationService(SWARNABHOOMIContext db) : ICultivationService
{
    public async Task<IReadOnlyList<SeasonDto>> GetSeasonsAsync(CancellationToken cancellationToken = default)
    {
        var seasons = await db.Seasons
            .AsNoTracking()
            .OrderByDescending(s => s.Status == "Active")
            .ThenByDescending(s => s.PlantingStart)
            .ToListAsync(cancellationToken);

        return seasons.Select(MapSeasonToDto).ToList();
    }

    public async Task<SeasonDto> CreateSeasonAsync(
        CreateSeasonDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role is not (UserRole.SuperAdmin or UserRole.Developer or UserRole.Government))
        {
            throw new UnauthorizedAccessException("Only administrators and government officers can create cultivation seasons.");
        }

        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Trim().Length > 100)
        {
            throw new ArgumentException("Provide a valid season name up to 100 characters.");
        }

        if (dto.PlantingEnd <= dto.PlantingStart)
        {
            throw new ArgumentException("Season planting end date must be after planting start date.");
        }

        var trimmedName = dto.Name.Trim();
        var duplicate = await db.Seasons.AnyAsync(s => s.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException($"A season with name '{trimmedName}' already exists.");
        }

        var year = dto.Year > 0 ? dto.Year : dto.PlantingStart.Year;
        var status = dto.IsActive ? "Active" : (!string.IsNullOrWhiteSpace(dto.Status) ? dto.Status.Trim() : "Draft");

        var season = new Season
        {
            Name = trimmedName,
            Year = year,
            District = dto.District?.Trim(),
            AgrarianServiceCentre = dto.AgrarianServiceCentre?.Trim(),
            PlantingStart = dto.PlantingStart,
            PlantingEnd = dto.PlantingEnd,
            Status = status,
            ApprovedBy = status == "Active" ? userId.ToString() : null,
            ApprovedAt = status == "Active" ? DateTime.UtcNow : null,
            CreatedBy = userId.ToString()
        };

        db.Seasons.Add(season);
        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "SeasonCreated",
            EntityType = nameof(Season),
            EntityId = season.Id.ToString(),
            Result = "Success",
            Reason = $"Season {season.Name} created as {season.Status}"
        });

        await db.SaveChangesAsync(cancellationToken);
        return MapSeasonToDto(season);
    }

    public async Task<SeasonDto> UpdateSeasonAsync(
        Guid seasonId,
        UpdateSeasonDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role is not (UserRole.SuperAdmin or UserRole.Developer or UserRole.Government))
        {
            throw new UnauthorizedAccessException("Only administrators and government officers can update cultivation seasons.");
        }

        var season = await db.Seasons.SingleOrDefaultAsync(s => s.Id == seasonId, cancellationToken);
        if (season is null)
        {
            throw new KeyNotFoundException("Season not found.");
        }

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var trimmedName = dto.Name.Trim();
            if (trimmedName.Length > 100)
            {
                throw new ArgumentException("Season name cannot exceed 100 characters.");
            }

            var duplicate = await db.Seasons.AnyAsync(s => s.Id != seasonId && s.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
            if (duplicate)
            {
                throw new InvalidOperationException($"Another season with name '{trimmedName}' already exists.");
            }
            season.Name = trimmedName;
        }

        if (dto.Year.HasValue && dto.Year.Value > 2000)
        {
            season.Year = dto.Year.Value;
        }

        if (dto.District != null)
        {
            season.District = dto.District.Trim();
        }

        if (dto.PlantingStart.HasValue)
        {
            season.PlantingStart = dto.PlantingStart.Value;
        }

        if (dto.PlantingEnd.HasValue)
        {
            season.PlantingEnd = dto.PlantingEnd.Value;
        }

        if (season.PlantingEnd <= season.PlantingStart)
        {
            throw new ArgumentException("Season planting end date must be after planting start date.");
        }

        if (dto.IsActive.HasValue)
        {
            if (dto.IsActive.Value)
            {
                season.Status = "Active";
                season.ApprovedBy = userId.ToString();
                season.ApprovedAt = DateTime.UtcNow;
                season.ClosedAt = null;
                season.ClosureReason = null;
            }
            else
            {
                season.Status = "Closed";
                season.ClosedAt = DateTime.UtcNow;
                season.ClosureReason = dto.ClosureReason ?? "Deactivated by administrator";
            }
        }
        else if (!string.IsNullOrWhiteSpace(dto.Status))
        {
            season.Status = dto.Status.Trim();
            if (season.Status == "Closed")
            {
                season.ClosedAt = DateTime.UtcNow;
                season.ClosureReason = dto.ClosureReason ?? "Closed by administrator";
            }
            else if (season.Status == "Active")
            {
                season.ClosedAt = null;
                season.ClosureReason = null;
            }
        }

        season.UpdatedBy = userId.ToString();
        season.UpdatedAt = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "SeasonUpdated",
            EntityType = nameof(Season),
            EntityId = season.Id.ToString(),
            Result = "Success",
            Reason = $"Season {season.Name} status updated to {season.Status}"
        });

        await db.SaveChangesAsync(cancellationToken);
        return MapSeasonToDto(season);
    }

    public async Task<IReadOnlyList<CultivationRecordDto>> GetCultivationsAsync(
        CultivationQueryFilter filter,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role == UserRole.Buyer)
        {
            return Array.Empty<CultivationRecordDto>();
        }

        var query = db.CultivationRecords
            .AsNoTracking()
            .Include(cr => cr.Farmer)
            .Include(cr => cr.Field)
                .ThenInclude(f => f.Farm)
            .Include(cr => cr.Field)
                .ThenInclude(f => f.Verifications)
            .Include(cr => cr.RiceVariety)
            .AsQueryable();

        if (role == UserRole.Farmer)
        {
            query = query.Where(cr => cr.Field.Farm.UserId == userId);
        }
        else if (role == UserRole.Officer)
        {
            var assignedDistricts = await db.OfficerAssignments
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.IsActive)
                .Select(a => a.District)
                .Distinct()
                .ToListAsync(cancellationToken);

            query = query.Where(cr => cr.Field.Farm.District != null && assignedDistricts.Contains(cr.Field.Farm.District));
        }
        else if (role is not (UserRole.SuperAdmin or UserRole.Developer or UserRole.Government))
        {
            return Array.Empty<CultivationRecordDto>();
        }

        if (filter.SeasonId.HasValue && filter.SeasonId.Value != Guid.Empty)
        {
            var seasonObj = await db.Seasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == filter.SeasonId.Value, cancellationToken);
            if (seasonObj != null)
            {
                query = query.Where(cr => cr.Season == seasonObj.Name);
            }
        }
        else if (!string.IsNullOrWhiteSpace(filter.Season))
        {
            query = query.Where(cr => cr.Season == filter.Season.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Province))
        {
            query = query.Where(cr => cr.Field.Farm.Province == filter.Province.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.District))
        {
            query = query.Where(cr => cr.Field.Farm.District == filter.District.Trim());
        }

        var records = await query
            .OrderByDescending(cr => cr.PlantingDate)
            .Select(cr => new CultivationRecordDto(
                cr.Id,
                cr.FarmerId,
                (cr.Farmer.FirstName + " " + cr.Farmer.LastName).Trim(),
                cr.FieldId,
                cr.Field.Name,
                cr.Field.FarmId,
                cr.Field.Farm.Name,
                cr.Field.Farm.District,
                cr.Field.Farm.Province,
                cr.AreaHectares,
                cr.Field.Verifications
                    .OrderByDescending(v => v.CreatedAt)
                    .Select(v => v.Status)
                    .FirstOrDefault() ?? "Pending",
                cr.RiceVarietyId,
                cr.RiceVariety.Name,
                cr.RiceVariety.VarietyCode,
                cr.RiceVariety.MaturityDays,
                cr.Season,
                cr.PlantingDate,
                cr.ExpectedHarvestDate ?? cr.PlantingDate.AddDays(cr.RiceVariety.MaturityDays),
                cr.Status,
                cr.Notes,
                cr.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return records;
    }

    public async Task<CultivationRecordDto> CreateCultivationAsync(
        CreateCultivationDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (role is not (UserRole.Farmer or UserRole.SuperAdmin or UserRole.Developer))
        {
            throw new UnauthorizedAccessException("Only registered farmers can log cultivation records.");
        }

        if (dto.FieldId == Guid.Empty)
        {
            throw new ArgumentException("Select a valid field.");
        }

        if (dto.RiceVarietyId == Guid.Empty)
        {
            throw new ArgumentException("Select a valid rice variety.");
        }

        var field = await db.Fields
            .Include(f => f.Farm)
            .Include(f => f.Verifications)
            .SingleOrDefaultAsync(f => f.Id == dto.FieldId, cancellationToken);

        if (field is null)
        {
            throw new KeyNotFoundException("Field not found.");
        }

        if (role == UserRole.Farmer && field.Farm.UserId != userId)
        {
            throw new UnauthorizedAccessException("You can only log cultivation records for fields registered to your account.");
        }

        var variety = await db.RiceVarieties
            .AsNoTracking()
            .SingleOrDefaultAsync(v => v.Id == dto.RiceVarietyId && v.IsActive, cancellationToken);

        if (variety is null)
        {
            throw new ArgumentException("Selected rice variety is not found or is currently inactive.");
        }

        Season? season = null;
        if (dto.SeasonId.HasValue && dto.SeasonId.Value != Guid.Empty)
        {
            season = await db.Seasons
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == dto.SeasonId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(dto.Season))
        {
            var seasonName = dto.Season.Trim();
            season = await db.Seasons
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Name.ToLower() == seasonName.ToLower(), cancellationToken);
        }

        if (season is null)
        {
            throw new ArgumentException("Provide a valid active season.");
        }

        if (season.Status != "Active")
        {
            throw new InvalidOperationException($"Season '{season.Name}' is not currently active for cultivation logging.");
        }

        var existingDuplicate = await db.CultivationRecords
            .AsNoTracking()
            .AnyAsync(cr => cr.FieldId == field.Id &&
                            cr.Season == season.Name &&
                            cr.Status != "Cancelled" &&
                            cr.Status != "Harvested",
                      cancellationToken);

        if (existingDuplicate)
        {
            throw new InvalidOperationException($"An active cultivation record already exists for field '{field.Name}' in season '{season.Name}'.");
        }

        var areaHectares = dto.AreaHectares.HasValue && dto.AreaHectares.Value > 0
            ? Math.Round(dto.AreaHectares.Value, 4)
            : field.AreaHectares;

        if (field.AreaHectares > 0 && areaHectares > field.AreaHectares * 1.05m)
        {
            throw new ArgumentException($"Cultivated area ({areaHectares} ha) cannot exceed the registered field area ({field.AreaHectares} ha).");
        }

        var expectedHarvestDate = dto.ExpectedHarvestDate ?? dto.PlantingDate.AddDays(variety.MaturityDays);
        var status = string.IsNullOrWhiteSpace(dto.Status) ? "Planned" : dto.Status.Trim();

        var farmerId = role == UserRole.Farmer ? userId : field.Farm.UserId;
        var farmerUser = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == farmerId, cancellationToken);
        var farmerName = farmerUser != null ? $"{farmerUser.FirstName} {farmerUser.LastName}".Trim() : "Farmer";

        var record = new CultivationRecord
        {
            FarmerId = farmerId,
            FieldId = field.Id,
            RiceVarietyId = variety.Id,
            Season = season.Name,
            PlantingDate = dto.PlantingDate,
            ExpectedHarvestDate = expectedHarvestDate,
            AreaHectares = areaHectares,
            Notes = dto.Notes?.Trim(),
            Status = status,
            CreatedBy = userId.ToString()
        };

        db.CultivationRecords.Add(record);
        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "CultivationLogged",
            EntityType = nameof(CultivationRecord),
            EntityId = record.Id.ToString(),
            Result = "Success",
            Reason = $"Cultivation logged for field '{field.Name}', variety '{variety.Name}', season '{season.Name}'"
        });

        await db.SaveChangesAsync(cancellationToken);

        var verificationStatus = field.Verifications
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => v.Status)
            .FirstOrDefault() ?? "Pending";

        return new CultivationRecordDto(
            record.Id,
            record.FarmerId,
            farmerName,
            field.Id,
            field.Name,
            field.FarmId,
            field.Farm.Name,
            field.Farm.District,
            field.Farm.Province,
            record.AreaHectares,
            verificationStatus,
            variety.Id,
            variety.Name,
            variety.VarietyCode,
            variety.MaturityDays,
            record.Season,
            record.PlantingDate,
            record.ExpectedHarvestDate ?? expectedHarvestDate,
            record.Status,
            record.Notes,
            record.CreatedAt
        );
    }

    public async Task<CultivationRecordDto> UpdateGrowthStageAsync(
        Guid id,
        UpdateGrowthStageDto dto,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Stage))
        {
            throw new ArgumentException("Specify a valid growth stage.");
        }

        var validStages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Planned", "Sown", "Vegetative", "Flowering", "Maturity", "Harvested", "Cancelled"
        };

        var stage = dto.Stage.Trim();
        if (!validStages.Contains(stage))
        {
            throw new ArgumentException($"Invalid growth stage '{stage}'. Allowed stages: Planned, Sown, Vegetative, Flowering, Maturity, Harvested, Cancelled.");
        }

        var record = await db.CultivationRecords
            .Include(cr => cr.Farmer)
            .Include(cr => cr.Field)
                .ThenInclude(f => f.Farm)
            .Include(cr => cr.Field)
                .ThenInclude(f => f.Verifications)
            .Include(cr => cr.RiceVariety)
            .SingleOrDefaultAsync(cr => cr.Id == id, cancellationToken);

        if (record is null)
        {
            throw new KeyNotFoundException("Cultivation record not found.");
        }

        if (role == UserRole.Farmer)
        {
            if (record.Field.Farm.UserId != userId && record.FarmerId != userId)
            {
                throw new UnauthorizedAccessException("You can only update cultivation records for your own fields.");
            }
        }
        else if (role == UserRole.Officer)
        {
            var isAssigned = await db.OfficerAssignments
                .AsNoTracking()
                .AnyAsync(a => a.UserId == userId && a.IsActive && a.District == record.Field.Farm.District, cancellationToken);

            if (!isAssigned)
            {
                throw new UnauthorizedAccessException("Officers can only update records within their assigned district.");
            }
        }
        else if (role is not (UserRole.SuperAdmin or UserRole.Developer or UserRole.Government))
        {
            throw new UnauthorizedAccessException("You do not have permission to update this cultivation record.");
        }

        record.Status = stage;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            record.Notes = dto.Notes.Trim();
        }
        record.UpdatedAt = DateTime.UtcNow;
        record.UpdatedBy = userId.ToString();

        db.AuditLogs.Add(new AuditLog
        {
            Actor = userId.ToString(),
            Action = "GrowthStageUpdated",
            EntityType = nameof(CultivationRecord),
            EntityId = record.Id.ToString(),
            Result = "Success",
            Reason = $"Growth stage updated to {stage}"
        });

        await db.SaveChangesAsync(cancellationToken);

        var verificationStatus = record.Field.Verifications
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => v.Status)
            .FirstOrDefault() ?? "Pending";

        return new CultivationRecordDto(
            record.Id,
            record.FarmerId,
            (record.Farmer.FirstName + " " + record.Farmer.LastName).Trim(),
            record.FieldId,
            record.Field.Name,
            record.Field.FarmId,
            record.Field.Farm.Name,
            record.Field.Farm.District,
            record.Field.Farm.Province,
            record.AreaHectares,
            verificationStatus,
            record.RiceVarietyId,
            record.RiceVariety.Name,
            record.RiceVariety.VarietyCode,
            record.RiceVariety.MaturityDays,
            record.Season,
            record.PlantingDate,
            record.ExpectedHarvestDate ?? record.PlantingDate.AddDays(record.RiceVariety.MaturityDays),
            record.Status,
            record.Notes,
            record.CreatedAt
        );
    }

    private static SeasonDto MapSeasonToDto(Season s) => new(
        s.Id,
        s.Name,
        s.Year,
        s.District,
        s.AgrarianServiceCentre,
        s.PlantingStart,
        s.PlantingEnd,
        s.Status,
        s.Status == "Active",
        s.ApprovedBy,
        s.ApprovedAt,
        s.ClosedAt,
        s.ClosureReason,
        s.CreatedAt);
}
