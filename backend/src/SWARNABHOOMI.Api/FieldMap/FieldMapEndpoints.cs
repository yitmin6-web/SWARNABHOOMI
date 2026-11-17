using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Api.FieldMap;

public static class FieldMapEndpoints
{
    public static IEndpointRouteBuilder MapFieldMapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var fieldMap = endpoints.MapGroup("/api/field-map").RequireAuthorization();

        fieldMap.MapGet("/", GetMap);
        fieldMap.MapGet("/farms", GetMyFarms);
        fieldMap.MapPost("/farms", CreateFarm);
        fieldMap.MapPost("/fields", CreateField);
        fieldMap.MapGet("/verification-queue", GetVerificationQueue);
        fieldMap.MapPost("/fields/{fieldId:guid}/verification", ReviewField);

        return endpoints;
    }

    private static async Task<IResult> GetMap(
        ClaimsPrincipal principal,
        SWARNABHOOMIContext db,
        CancellationToken cancellationToken)
    {
        var role = principal.FindFirstValue(ClaimTypes.Role);
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var query = db.Fields.AsNoTracking().Include(field => field.Farm).AsQueryable();

        if (role == "Farmer")
        {
            if (!Guid.TryParse(userId, out var farmerId))
            {
                return Results.Forbid();
            }

            query = query.Where(field => field.Farm.UserId == farmerId);
        }
        else if (role == "Officer")
        {
            if (!Guid.TryParse(userId, out var officerId))
            {
                return Results.Forbid();
            }

            var districts = await db.OfficerAssignments
                .AsNoTracking()
                .Where(assignment => assignment.UserId == officerId && assignment.IsActive)
                .Select(assignment => assignment.District)
                .Distinct()
                .ToListAsync(cancellationToken);

            query = query.Where(field =>
                field.Farm.District != null && districts.Contains(field.Farm.District));
        }
        else if (role is not ("Government" or "Buyer" or "SuperAdmin"))
        {
            return Results.Forbid();
        }

        var fields = await query
            .Select(field => new
            {
                field.Id,
                field.Name,
                FarmName = field.Farm.Name,
                field.AreaHectares,
                field.BoundaryGeoJson,
                field.Latitude,
                field.Longitude,
                field.Farm.District,
                field.Farm.Province,
                OwnerName = field.Farm.User.FirstName + " " + field.Farm.User.LastName,
                VerificationStatus = field.Verifications
                    .OrderByDescending(verification => verification.CreatedAt)
                    .Select(verification => verification.Status)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        if (role == "Buyer")
        {
            var regions = fields
                .GroupBy(field => field.District ?? "Unspecified")
                .Select(group => new FieldRegionSummary(
                    group.Key,
                    group.Count(),
                    group.Count(field => field.VerificationStatus == "Verified"),
                    group.Where(field => field.VerificationStatus == "Verified").Sum(field => field.AreaHectares)))
                .OrderBy(region => region.District)
                .ToArray();

            return Results.Ok(new FieldMapResponse("aggregate", [], regions));
        }

        var visibleFields = fields.Select(field => new FieldMapItem(
            field.Id,
            field.Name,
            field.FarmName,
            field.AreaHectares,
            field.BoundaryGeoJson,
            field.Latitude,
            field.Longitude,
            field.District,
            field.Province,
            field.OwnerName.Trim(),
            field.VerificationStatus ?? "Unverified"));

        return Results.Ok(new FieldMapResponse("fields", visibleFields, []));
    }

    private static async Task<IResult> GetMyFarms(
        ClaimsPrincipal principal,
        SWARNABHOOMIContext db,
        CancellationToken cancellationToken)
    {
        if (principal.FindFirstValue(ClaimTypes.Role) != "Farmer" ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var farmerId))
        {
            return Results.Forbid();
        }

        var farms = await db.Farms
            .AsNoTracking()
            .Where(farm => farm.UserId == farmerId)
            .OrderBy(farm => farm.Name)
            .Select(farm => new MyFarmResponse(farm.Id, farm.Name, farm.AreaHectares, farm.District, farm.Province))
            .ToListAsync(cancellationToken);

        return Results.Ok(farms);
    }

    private static async Task<IResult> CreateFarm(
        CreateFarmRequest request,
        ClaimsPrincipal principal,
        SWARNABHOOMIContext db,
        CancellationToken cancellationToken)
    {
        if (principal.FindFirstValue(ClaimTypes.Role) != "Farmer" ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var farmerId))
        {
            return Results.Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 120 ||
            request.AreaHectares <= 0 ||
            request.AreaHectares > 100_000 ||
            (request.District?.Length ?? 0) > 120 ||
            (request.Province?.Length ?? 0) > 120)
        {
            return Results.BadRequest(new { error = "Provide a farm name, positive area, and valid location details." });
        }

        var farm = new Farm
        {
            UserId = farmerId,
            Name = request.Name.Trim(),
            AreaHectares = request.AreaHectares,
            District = request.District?.Trim(),
            Province = request.Province?.Trim()
        };
        db.Farms.Add(farm);
        db.AuditLogs.Add(CreateAuditLog(farmerId, "FarmCreated", nameof(Farm), farm.Id));
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/field-map/farms/{farm.Id}",
            new MyFarmResponse(farm.Id, farm.Name, farm.AreaHectares, farm.District, farm.Province));
    }

    private static async Task<IResult> CreateField(
        CreateFieldRequest request,
        ClaimsPrincipal principal,
        SWARNABHOOMIContext db,
        CancellationToken cancellationToken)
    {
        if (principal.FindFirstValue(ClaimTypes.Role) != "Farmer" ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var farmerId))
        {
            return Results.Forbid();
        }

        if (request.FarmId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 120)
        {
            return Results.BadRequest(new { error = "Provide a field name, farm, and valid GeoJSON polygon." });
        }

        if (!GeoJsonPolygon.TryMeasure(request.BoundaryGeoJson, out var boundary, out var geometryError))
        {
            return Results.BadRequest(new { error = geometryError });
        }

        var farm = await db.Farms.SingleOrDefaultAsync(
            candidate => candidate.Id == request.FarmId && candidate.UserId == farmerId,
            cancellationToken);
        if (farm is null)
        {
            return Results.NotFound(new { error = "Farm not found." });
        }

        if (farm.AreaHectares > 0 && boundary.AreaHectares > farm.AreaHectares)
        {
            return Results.BadRequest(new { error = "The field boundary area cannot exceed the registered farm area." });
        }

        var existingBoundaries = await db.Fields
            .AsNoTracking()
            .Where(field => field.FarmId == farm.Id && field.BoundaryGeoJson != null)
            .Select(field => new { field.Name, field.BoundaryGeoJson })
            .ToListAsync(cancellationToken);

        var possibleOverlap = existingBoundaries
            .Where(existing => GeoJsonPolygon.TryMeasure(existing.BoundaryGeoJson, out _, out _))
            .Select(existing =>
            {
                GeoJsonPolygon.TryMeasure(existing.BoundaryGeoJson, out var existingBoundary, out _);
                return new { existing.Name, Boundary = existingBoundary };
            })
            .FirstOrDefault(existing => boundary.OverlapsBounds(existing.Boundary));

        var field = new Field
        {
            FarmId = farm.Id,
            Name = request.Name.Trim(),
            AreaHectares = boundary.AreaHectares,
            Latitude = boundary.CenterLatitude,
            Longitude = boundary.CenterLongitude,
            BoundaryGeoJson = request.BoundaryGeoJson
        };
        db.Fields.Add(field);
        db.FieldVerifications.Add(new FieldVerification
        {
            FieldId = field.Id,
            Status = "Pending",
            CreatedBy = farmerId.ToString(),
            ReviewNotes = possibleOverlap is null
                ? null
                : $"Potential overlap with field '{possibleOverlap.Name}' requires reviewer attention."
        });
        db.AuditLogs.Add(CreateAuditLog(farmerId, "FieldSubmitted", nameof(Field), field.Id));
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/field-map/fields/{field.Id}", new
        {
            field.Id,
            field.Name,
            field.AreaHectares,
            verificationStatus = "Pending",
            possibleOverlap = possibleOverlap is not null
        });
    }

    private static async Task<IResult> GetVerificationQueue(
        ClaimsPrincipal principal,
        SWARNABHOOMIContext db,
        CancellationToken cancellationToken)
    {
        var role = principal.FindFirstValue(ClaimTypes.Role);
        if (role is not ("Officer" or "Government") ||
            (role == "Officer" && !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out _)))
        {
            return Results.Forbid();
        }

        var query = db.FieldVerifications
            .AsNoTracking()
            .Where(verification => verification.Status == "Pending")
            .Include(verification => verification.Field)
            .ThenInclude(field => field.Farm)
            .ThenInclude(farm => farm.User)
            .AsQueryable();

        if (role == "Officer")
        {
            var officerId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var districts = await db.OfficerAssignments
                .AsNoTracking()
                .Where(assignment => assignment.UserId == officerId && assignment.IsActive)
                .Select(assignment => assignment.District)
                .Distinct()
                .ToListAsync(cancellationToken);
            query = query.Where(verification =>
                verification.Field.Farm.District != null && districts.Contains(verification.Field.Farm.District));
        }

        var queue = await query
            .OrderBy(verification => verification.CreatedAt)
            .Select(verification => new
            {
                verification.Id,
                fieldId = verification.FieldId,
                fieldName = verification.Field.Name,
                farmName = verification.Field.Farm.Name,
                ownerName = verification.Field.Farm.User.FirstName + " " + verification.Field.Farm.User.LastName,
                verification.Field.AreaHectares,
                verification.Field.BoundaryGeoJson,
                district = verification.Field.Farm.District,
                verification.ReviewNotes,
                submittedAt = verification.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(queue);
    }

    private static async Task<IResult> ReviewField(
        Guid fieldId,
        ReviewFieldRequest request,
        ClaimsPrincipal principal,
        SWARNABHOOMIContext db,
        CancellationToken cancellationToken)
    {
        var role = principal.FindFirstValue(ClaimTypes.Role);
        if (role is not ("Officer" or "Government") ||
            !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var reviewerId))
        {
            return Results.Forbid();
        }

        if (request.Status is not ("Verified" or "Returned" or "Rejected") ||
            (request.Status is "Returned" or "Rejected" && string.IsNullOrWhiteSpace(request.Notes)) ||
            (request.Notes?.Length ?? 0) > 2000)
        {
            return Results.BadRequest(new { error = "Choose Verified, Returned, or Rejected; include a reason for a return or rejection." });
        }

        var field = await db.Fields
            .Include(candidate => candidate.Farm)
            .SingleOrDefaultAsync(candidate => candidate.Id == fieldId, cancellationToken);
        if (field is null)
        {
            return Results.NotFound();
        }

        if (role == "Officer")
        {
            var isAssigned = await db.OfficerAssignments.AnyAsync(
                assignment => assignment.UserId == reviewerId &&
                    assignment.IsActive &&
                    assignment.District == field.Farm.District,
                cancellationToken);
            if (!isAssigned)
            {
                return Results.Forbid();
            }
        }

        var verification = new FieldVerification
        {
            FieldId = fieldId,
            Status = request.Status,
            ReviewerId = reviewerId.ToString(),
            ReviewedAt = DateTime.UtcNow,
            ReviewNotes = request.Notes?.Trim(),
            CreatedBy = reviewerId.ToString()
        };
        db.FieldVerifications.Add(verification);
        db.AuditLogs.Add(CreateAuditLog(
            reviewerId,
            $"Field{request.Status}",
            nameof(Field),
            fieldId,
            request.Notes?.Trim()));
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { fieldId, verification.Status, verification.ReviewedAt, verification.ReviewNotes });
    }

    private static AuditLog CreateAuditLog(
        Guid actorId,
        string action,
        string entityType,
        Guid entityId,
        string? reason = null) =>
        new()
        {
            Actor = actorId.ToString(),
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Result = "Success",
            Reason = reason
        };
}

public sealed record CreateFarmRequest(
    string Name,
    decimal AreaHectares,
    string? District,
    string? Province);

public sealed record CreateFieldRequest(Guid FarmId, string Name, string BoundaryGeoJson);

public sealed record ReviewFieldRequest(string Status, string? Notes);

public sealed record MyFarmResponse(
    Guid Id,
    string Name,
    decimal AreaHectares,
    string? District,
    string? Province);

public sealed record FieldMapItem(
    Guid Id,
    string Name,
    string FarmName,
    decimal AreaHectares,
    string? BoundaryGeoJson,
    double? Latitude,
    double? Longitude,
    string? District,
    string? Province,
    string OwnerName,
    string VerificationStatus);

public sealed record FieldRegionSummary(
    string District,
    int TotalFields,
    int VerifiedFields,
    decimal VerifiedAreaHectares);

public sealed record FieldMapResponse(
    string Visibility,
    IEnumerable<FieldMapItem> Fields,
    IEnumerable<FieldRegionSummary> Regions);

internal sealed record MeasuredBoundary(
    decimal AreaHectares,
    double CenterLatitude,
    double CenterLongitude,
    double MinimumLatitude,
    double MaximumLatitude,
    double MinimumLongitude,
    double MaximumLongitude)
{
    public bool OverlapsBounds(MeasuredBoundary other) =>
        MinimumLatitude <= other.MaximumLatitude &&
        MaximumLatitude >= other.MinimumLatitude &&
        MinimumLongitude <= other.MaximumLongitude &&
        MaximumLongitude >= other.MinimumLongitude;
}

internal static class GeoJsonPolygon
{
    private const double EarthRadiusMetres = 6_371_008.8;
    private const double CoordinateTolerance = 1e-8;

    public static bool TryMeasure(string? geoJson, out MeasuredBoundary boundary, out string? error)
    {
        boundary = default!;
        error = null;
        if (string.IsNullOrWhiteSpace(geoJson) || geoJson.Length > 250_000)
        {
            error = "Provide a GeoJSON polygon smaller than 250 KB.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(geoJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.GetString() != "Polygon" ||
                !root.TryGetProperty("coordinates", out var coordinates) ||
                coordinates.ValueKind != JsonValueKind.Array ||
                coordinates.GetArrayLength() == 0)
            {
                error = "Field boundaries must be GeoJSON Polygon geometries.";
                return false;
            }

            var rings = new List<List<(double Longitude, double Latitude)>>();
            foreach (var ringElement in coordinates.EnumerateArray())
            {
                if (ringElement.ValueKind != JsonValueKind.Array || ringElement.GetArrayLength() < 4)
                {
                    error = "Each polygon ring must contain at least four coordinates and be closed.";
                    return false;
                }

                var ring = new List<(double Longitude, double Latitude)>();
                foreach (var point in ringElement.EnumerateArray())
                {
                    if (point.ValueKind != JsonValueKind.Array ||
                        point.GetArrayLength() < 2 ||
                        !point[0].TryGetDouble(out var longitude) ||
                        !point[1].TryGetDouble(out var latitude) ||
                        !double.IsFinite(longitude) ||
                        !double.IsFinite(latitude) ||
                        longitude is < -180 or > 180 ||
                        latitude is < -90 or > 90)
                    {
                        error = "Polygon coordinates must be finite longitude/latitude pairs in valid ranges.";
                        return false;
                    }

                    ring.Add((longitude, latitude));
                }

                if (Math.Abs(ring[0].Longitude - ring[^1].Longitude) > CoordinateTolerance ||
                    Math.Abs(ring[0].Latitude - ring[^1].Latitude) > CoordinateTolerance ||
                    HasSelfIntersection(ring))
                {
                    error = "Polygon rings must be closed and must not self-intersect.";
                    return false;
                }

                rings.Add(ring);
            }

            var areaSquareMetres = RingArea(rings[0]);
            for (var index = 1; index < rings.Count; index++)
            {
                if (!Contains(rings[0], rings[index][0]) || RingsIntersect(rings[0], rings[index]))
                {
                    error = "Polygon holes must lie inside the field boundary without crossing it.";
                    return false;
                }

                areaSquareMetres -= RingArea(rings[index]);
            }

            if (areaSquareMetres <= 0)
            {
                error = "The field polygon must have a positive area.";
                return false;
            }

            var points = rings[0].Take(rings[0].Count - 1).ToArray();
            boundary = new MeasuredBoundary(
                decimal.Round((decimal)(areaSquareMetres / 10_000), 4),
                points.Average(point => point.Latitude),
                points.Average(point => point.Longitude),
                points.Min(point => point.Latitude),
                points.Max(point => point.Latitude),
                points.Min(point => point.Longitude),
                points.Max(point => point.Longitude));

            if (boundary.AreaHectares <= 0)
            {
                error = "The field polygon is too small to represent a positive area.";
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Field boundary is not valid GeoJSON.";
            return false;
        }
        catch (InvalidOperationException)
        {
            error = "Field boundary must contain a GeoJSON Polygon.";
            return false;
        }
    }

    private static double RingArea(IReadOnlyList<(double Longitude, double Latitude)> ring)
    {
        var total = 0d;
        for (var index = 0; index < ring.Count - 1; index++)
        {
            var current = ring[index];
            var next = ring[index + 1];
            total += ToRadians(next.Longitude - current.Longitude) *
                (2 + Math.Sin(ToRadians(current.Latitude)) + Math.Sin(ToRadians(next.Latitude)));
        }

        return Math.Abs(total) * EarthRadiusMetres * EarthRadiusMetres / 2;
    }

    private static bool HasSelfIntersection(IReadOnlyList<(double Longitude, double Latitude)> ring)
    {
        var segmentCount = ring.Count - 1;
        for (var first = 0; first < segmentCount; first++)
        {
            for (var second = first + 1; second < segmentCount; second++)
            {
                if (second == first + 1 || (first == 0 && second == segmentCount - 1))
                {
                    continue;
                }

                if (SegmentsIntersect(ring[first], ring[first + 1], ring[second], ring[second + 1]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool RingsIntersect(
        IReadOnlyList<(double Longitude, double Latitude)> first,
        IReadOnlyList<(double Longitude, double Latitude)> second)
    {
        for (var firstIndex = 0; firstIndex < first.Count - 1; firstIndex++)
        {
            for (var secondIndex = 0; secondIndex < second.Count - 1; secondIndex++)
            {
                if (SegmentsIntersect(
                    first[firstIndex],
                    first[firstIndex + 1],
                    second[secondIndex],
                    second[secondIndex + 1]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(
        (double Longitude, double Latitude) a,
        (double Longitude, double Latitude) b,
        (double Longitude, double Latitude) c,
        (double Longitude, double Latitude) d)
    {
        var abC = Orientation(a, b, c);
        var abD = Orientation(a, b, d);
        var cdA = Orientation(c, d, a);
        var cdB = Orientation(c, d, b);
        return abC != abD && cdA != cdB;
    }

    private static int Orientation(
        (double Longitude, double Latitude) a,
        (double Longitude, double Latitude) b,
        (double Longitude, double Latitude) c)
    {
        var value = (b.Latitude - a.Latitude) * (c.Longitude - b.Longitude) -
            (b.Longitude - a.Longitude) * (c.Latitude - b.Latitude);
        return Math.Abs(value) <= CoordinateTolerance ? 0 : value > 0 ? 1 : 2;
    }

    private static bool Contains(
        IReadOnlyList<(double Longitude, double Latitude)> ring,
        (double Longitude, double Latitude) point)
    {
        var inside = false;
        for (int current = 0, previous = ring.Count - 2; current < ring.Count - 1; previous = current++)
        {
            var currentPoint = ring[current];
            var previousPoint = ring[previous];
            if ((currentPoint.Latitude > point.Latitude) != (previousPoint.Latitude > point.Latitude) &&
                point.Longitude <
                (previousPoint.Longitude - currentPoint.Longitude) *
                (point.Latitude - currentPoint.Latitude) /
                (previousPoint.Latitude - currentPoint.Latitude) +
                currentPoint.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
