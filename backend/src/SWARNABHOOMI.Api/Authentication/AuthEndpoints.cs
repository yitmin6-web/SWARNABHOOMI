using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Core.Enums;
using SWARNABHOOMI.Infrastructure.Data;

namespace SWARNABHOOMI.Api.Authentication;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth");

        auth.MapPost("/login", async (
            LoginRequest request,
            HttpContext httpContext,
            SWARNABHOOMIContext db,
            IPasswordHasher<User> passwordHasher,
            ILoggerFactory loggerFactory) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                request.Email.Length > 254 ||
                request.Password.Length > 256)
            {
                return Results.BadRequest(new { error = "Enter a valid email address and password." });
            }

            var email = request.Email.Trim();
            var normalizedEmail = email.ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(
                candidate => candidate.Email.ToUpper() == normalizedEmail);

            if (user is null || !user.IsActive)
            {
                return Results.Unauthorized();
            }

            PasswordVerificationResult verification;
            try
            {
                verification = string.IsNullOrWhiteSpace(user.PasswordHash)
                    ? PasswordVerificationResult.Failed
                    : passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            }
            catch (FormatException exception)
            {
                loggerFactory.CreateLogger("Authentication").LogWarning(
                    exception,
                    "Stored password hash for user {UserId} is malformed.",
                    user.Id);
                verification = PasswordVerificationResult.Failed;
            }

            if (verification == PasswordVerificationResult.Failed)
            {
                return Results.Unauthorized();
            }

            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            }

            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}".Trim()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            };
            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = request.RememberMe,
                    AllowRefresh = true
                });

            return Results.Ok(AuthUserResponse.FromUser(user));
        })
        .RequireRateLimiting("login");

        auth.MapPost("/register", async (
            RegistrationRequest request,
            SWARNABHOOMIContext db,
            IPasswordHasher<User> passwordHasher) =>
        {
            var email = request.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email) ||
                email.Length > 254 ||
                !new EmailAddressAttribute().IsValid(email) ||
                string.IsNullOrWhiteSpace(request.FirstName) ||
                request.FirstName.Trim().Length > 100 ||
                string.IsNullOrWhiteSpace(request.LastName) ||
                request.LastName.Trim().Length > 100 ||
                (request.PhoneNumber?.Length ?? 0) > 30 ||
                string.IsNullOrWhiteSpace(request.Password) ||
                request.Password.Length < 12 ||
                request.Password.Length > 256 ||
                request.Role is not (UserRole.Farmer or UserRole.Buyer or UserRole.Government))
            {
                return Results.BadRequest(new
                {
                    error = "Provide valid details, choose a supported account type, and use a password between 12 and 256 characters."
                });
            }

            var normalizedEmail = email.ToUpperInvariant();
            var exists = await db.Users.AnyAsync(user => user.Email.ToUpper() == normalizedEmail);
            if (exists)
            {
                return Results.Conflict(new { error = "An account with this email already exists." });
            }

            var user = new User
            {
                Email = email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                PhoneNumber = request.PhoneNumber?.Trim() ?? string.Empty,
                Role = request.Role,
                IsActive = false,
                IsVerified = false
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            return Results.Accepted(value: new
            {
                message = "Your account request was sent for administrator approval."
            });
        })
        .RequireRateLimiting("registration");

        auth.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = principal.FindFirstValue(ClaimTypes.Email);
            var name = principal.FindFirstValue(ClaimTypes.Name);
            var role = principal.FindFirstValue(ClaimTypes.Role);

            if (!Guid.TryParse(id, out var userId) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(role))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new AuthUserResponse(userId, email, name ?? email, role));
        })
        .RequireAuthorization();

        auth.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        })
        .RequireAuthorization();

        return endpoints;
    }

    public static void EnsureBootstrapAdministrator(
        SWARNABHOOMIContext db,
        IPasswordHasher<User> passwordHasher,
        IConfiguration configuration)
    {
        var emailSetting = configuration["Authentication:BootstrapAdmin:Email"];
        var password = configuration["Authentication:BootstrapAdmin:Password"];
        var hasEmail = !string.IsNullOrWhiteSpace(emailSetting);
        var hasPassword = !string.IsNullOrWhiteSpace(password);

        if (!hasEmail && !hasPassword)
        {
            return;
        }

        if (!hasEmail || !hasPassword)
        {
            throw new InvalidOperationException(
                "Both Authentication:BootstrapAdmin:Email and Authentication:BootstrapAdmin:Password must be configured together.");
        }

        var email = emailSetting!.Trim();
        if (email.Length > 254 || password!.Length < 12 || password.Length > 256)
        {
            throw new InvalidOperationException(
                "The bootstrap administrator email must be valid and the password must contain between 12 and 256 characters.");
        }

        var normalizedEmail = email.ToUpperInvariant();
        var admin = db.Users.SingleOrDefault(
            candidate => candidate.Email.ToUpper() == normalizedEmail);

        if (admin is null)
        {
            admin = new User
            {
                Email = email,
                FirstName = configuration["Authentication:BootstrapAdmin:FirstName"]?.Trim() is { Length: > 0 } firstName
                    ? firstName
                    : "System",
                LastName = configuration["Authentication:BootstrapAdmin:LastName"]?.Trim() is { Length: > 0 } lastName
                    ? lastName
                    : "Administrator",
                PhoneNumber = string.Empty,
                IsEmailConfirmed = true,
                IsActive = true,
                IsVerified = true,
                Role = UserRole.SuperAdmin
            };
            db.Users.Add(admin);
        }
        else
        {
            admin.Email = email;
            if (!string.IsNullOrWhiteSpace(configuration["Authentication:BootstrapAdmin:FirstName"]))
            {
                admin.FirstName = configuration["Authentication:BootstrapAdmin:FirstName"]!.Trim();
            }
            if (!string.IsNullOrWhiteSpace(configuration["Authentication:BootstrapAdmin:LastName"]))
            {
                admin.LastName = configuration["Authentication:BootstrapAdmin:LastName"]!.Trim();
            }
            admin.Role = UserRole.SuperAdmin;
            admin.IsActive = true;
            admin.IsEmailConfirmed = true;
            admin.IsVerified = true;
        }

        admin.PasswordHash = passwordHasher.HashPassword(admin, password);
        db.SaveChanges();
    }
}

public sealed record LoginRequest(string? Email, string? Password, bool RememberMe = false);

public sealed record RegistrationRequest(
    string? Email,
    string? Password,
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    UserRole Role);

public sealed record AuthUserResponse(Guid Id, string Email, string Name, string Role)
{
    public static AuthUserResponse FromUser(User user) =>
        new(user.Id, user.Email, $"{user.FirstName} {user.LastName}".Trim(), user.Role.ToString());
}
