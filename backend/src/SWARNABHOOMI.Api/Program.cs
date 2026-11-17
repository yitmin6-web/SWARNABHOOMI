using System.Threading.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using SWARNABHOOMI.Api.Assistant;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Api.Authentication;
using SWARNABHOOMI.Api.FieldMap;
using SWARNABHOOMI.Api.LiveData;
using SWARNABHOOMI.Api.Members;
using SWARNABHOOMI.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDataProtection()
    .SetApplicationName("SWARNABHOOMI");
builder.Services.AddHttpClient("ai-assistant", client =>
    client.Timeout = TimeSpan.FromMinutes(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false
    });
builder.Services.AddHttpClient("open-meteo", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SWARNABHOOMI/1.0");
});
builder.Services.AddHttpClient("cbsl-prices", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SWARNABHOOMI/1.0");
});
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "swarnabhoomi.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole(nameof(UserRole.SuperAdmin), nameof(UserRole.Developer)));
    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireRole(nameof(UserRole.SuperAdmin)));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 5,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(15)
            }));
    options.AddPolicy("registration", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 5,
                QueueLimit = 0,
                Window = TimeSpan.FromHours(1)
            }));
    options.AddPolicy("ai-assistant", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 20,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(15)
            }));
});

builder.Services.AddDbContext<SWARNABHOOMIContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseNpgsql(connectionString);
    }
    else if (builder.Environment.IsDevelopment())
    {
        var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "swarnabhoomi.db")}");
    }
    else
    {
        options.UseInMemoryDatabase("SWARNABHOOMI");
    }
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "SWARNABHOOMI API", status = "running" }));
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    timestampUtc = DateTime.UtcNow
}));

app.MapAuthEndpoints();
app.MapFieldMapEndpoints();
app.MapAssistantEndpoints();
app.MapMemberDetailsEndpoints();

var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/dashboard", async (SWARNABHOOMIContext db) =>
{
    var summary = new
    {
        users = await db.Users.CountAsync(user => user.IsActive),
        farms = await db.Farms.CountAsync(),
        demandEntries = await db.Demands.CountAsync(),
        varieties = await db.RiceVarieties.CountAsync(),
        marketPrices = await db.MarketPriceObservations.CountAsync(),
        weatherRecords = await db.WeatherObservations.CountAsync()
    };

    return Results.Ok(summary);
});

api.MapGet("/users", async (SWARNABHOOMIContext db) =>
    Results.Ok(await db.Users
        .AsNoTracking()
        .OrderBy(u => u.Email)
        .Select(u => new
        {
            u.Id,
            u.Email,
            u.FirstName,
            u.LastName,
            u.PhoneNumber,
            u.Role,
            u.IsActive,
            u.IsVerified,
            u.District,
            u.Province,
            u.CreatedAt
        })
        .ToListAsync()))
    .RequireAuthorization("AdminOnly");

api.MapPost("/users", async (
    CreateUserRequest request,
    SWARNABHOOMIContext db,
    IPasswordHasher<User> passwordHasher) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) ||
        request.Email.Length > 254 ||
        string.IsNullOrWhiteSpace(request.Password) ||
        request.Password.Length < 12 ||
        request.Password.Length > 256 ||
        string.IsNullOrWhiteSpace(request.FirstName) ||
        string.IsNullOrWhiteSpace(request.LastName) ||
        !Enum.IsDefined(request.Role) ||
        request.Role == UserRole.SuperAdmin)
    {
        return Results.BadRequest(new
        {
            error = "Provide an email, first and last name, a non-root role, and a password between 12 and 256 characters."
        });
    }

    var email = request.Email.Trim();
    var normalizedEmail = email.ToUpperInvariant();
    var exists = await db.Users.AnyAsync(u => u.Email.ToUpper() == normalizedEmail);
    if (exists)
    {
        return Results.Conflict(new { error = "A user with this email already exists." });
    }

    var user = new User
    {
        Email = email,
        FirstName = request.FirstName.Trim(),
        LastName = request.LastName.Trim(),
        PhoneNumber = request.PhoneNumber ?? string.Empty,
        Role = request.Role,
        IsActive = request.IsActive,
        IsVerified = request.IsActive,
        District = request.District,
        Province = request.Province
    };
    user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        user.Id,
        user.Email,
        user.FirstName,
        user.LastName,
        user.Role,
        user.CreatedAt
    });
})
    .RequireAuthorization("SuperAdminOnly");

api.MapPost("/users/{id:guid}/approval", async (
    Guid id,
    ReviewRegistrationRequest request,
    SWARNABHOOMIContext db) =>
{
    var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == id);
    if (user is null || user.IsActive || user.IsVerified)
    {
        return Results.NotFound();
    }

    if (request.Approved)
    {
        user.IsActive = true;
        user.IsVerified = true;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    db.Users.Remove(user);
    await db.SaveChangesAsync();
    return Results.NoContent();
})
    .RequireAuthorization("AdminOnly");

api.MapPut("/users/{id:guid}/role", async (
    Guid id,
    ChangeUserRoleRequest request,
    ClaimsPrincipal principal,
    SWARNABHOOMIContext db) =>
{
    if (!Enum.IsDefined(request.Role) || request.Role == UserRole.SuperAdmin)
    {
        return Results.BadRequest(new { error = "Choose a valid non-root account role." });
    }

    var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(currentUserId, out var signedInUserId) || signedInUserId == id)
    {
        return Results.Forbid();
    }

    var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == id);
    if (user is null || user.Role == UserRole.SuperAdmin)
    {
        return Results.NotFound();
    }

    user.Role = request.Role;
    await db.SaveChangesAsync();
    return Results.NoContent();
})
    .RequireAuthorization("SuperAdminOnly");

api.MapGet("/rice-varieties", async (SWARNABHOOMIContext db) =>
    Results.Ok(await db.RiceVarieties
        .AsNoTracking()
        .OrderBy(v => v.Name)
        .Select(v => new
        {
            v.Id,
            v.VarietyCode,
            v.Name,
            v.Description,
            v.MaturityDays,
            v.AverageYieldKgPerHectare,
            v.IsActive
        })
        .ToListAsync()));

api.MapGet("/farms", async (SWARNABHOOMIContext db) =>
    Results.Ok(await db.Farms
        .AsNoTracking()
        .Include(f => f.User)
        .Select(f => new
        {
            f.Id,
            f.Name,
            f.District,
            f.Province,
            f.AreaHectares,
            f.Address,
            farmOwner = f.User.FirstName + " " + f.User.LastName,
            f.CreatedAt
        })
        .ToListAsync()));

api.MapPost("/farms", async (CreateFarmRequest request, SWARNABHOOMIContext db) =>
{
    var userExists = await db.Users.AnyAsync(u => u.Id == request.UserId);
    if (!userExists)
    {
        return Results.BadRequest(new { error = "User not found." });
    }

    var farm = new Farm
    {
        UserId = request.UserId,
        Name = request.Name,
        Description = request.Description,
        AreaHectares = request.AreaHectares,
        District = request.District,
        Province = request.Province,
        Address = request.Address,
        Latitude = request.Latitude,
        Longitude = request.Longitude
    };

    db.Farms.Add(farm);
    await db.SaveChangesAsync();

    return Results.Ok(new { farm.Id, farm.Name, farm.UserId, farm.CreatedAt });
})
    .RequireAuthorization("AdminOnly");

api.MapLiveDataEndpoints();

api.MapGet("/demands", async (SWARNABHOOMIContext db) =>
    Results.Ok(await db.Demands
        .AsNoTracking()
        .OrderByDescending(d => d.CreatedAt)
        .Select(d => new
        {
            d.Id,
            d.OwnerId,
            d.District,
            d.Season,
            d.Status,
            d.PeriodStart,
            d.PeriodEnd,
            d.Notes,
            d.CreatedAt
        })
        .ToListAsync()));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SWARNABHOOMIContext>();
    DatabaseInitializer.Migrate(db);
    DbSeeder.Seed(db);
    AuthEndpoints.EnsureBootstrapAdministrator(
        db,
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(),
        app.Configuration);
}

app.Run();

public record CreateUserRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    UserRole Role,
    bool IsActive,
    string? District,
    string? Province);

public record ReviewRegistrationRequest(bool Approved);

public record ChangeUserRoleRequest(UserRole Role);

public record CreateFarmRequest(
    Guid UserId,
    string Name,
    string? Description,
    decimal AreaHectares,
    string? District,
    string? Province,
    string? Address,
    double? Latitude,
    double? Longitude);
