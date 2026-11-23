using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Core.Enums;

namespace SWARNABHOOMI.Infrastructure.Data;

public static class DbSeeder
{
    public static void Seed(SWARNABHOOMIContext context)
    {
        if (!context.Seasons.Any())
        {
            context.Seasons.AddRange(
                new Season
                {
                    Name = "2026/2027 Maha",
                    Year = 2026,
                    District = "National",
                    PlantingStart = new DateOnly(2026, 9, 1),
                    PlantingEnd = new DateOnly(2027, 2, 28),
                    Status = "Active",
                    ApprovedBy = "Department of Agriculture",
                    ApprovedAt = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc)
                },
                new Season
                {
                    Name = "2027 Yala",
                    Year = 2027,
                    District = "National",
                    PlantingStart = new DateOnly(2027, 3, 1),
                    PlantingEnd = new DateOnly(2027, 8, 31),
                    Status = "Active",
                    ApprovedBy = "Department of Agriculture",
                    ApprovedAt = new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new Season
                {
                    Name = "2026 Yala",
                    Year = 2026,
                    District = "National",
                    PlantingStart = new DateOnly(2026, 3, 1),
                    PlantingEnd = new DateOnly(2026, 8, 31),
                    Status = "Closed",
                    ApprovedBy = "Department of Agriculture",
                    ApprovedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    ClosedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                    ClosureReason = "Season completed"
                }
            );
            context.SaveChanges();
        }

        if (!context.ResourceRuleVersions.Any())
        {
            var defaultPayload = new
            {
                versionName = "DOA Irrigated Paddy Standard (2026/2027)",
                seedPaddyKgPerHectare = 100m,
                ureaKgPerHectare = 225m,
                tspKgPerHectare = 55m,
                mopKgPerHectare = 60m,
                notes = "Standard national fertilizer and seed recommendation by Department of Agriculture Sri Lanka for irrigated lowlands."
            };

            context.ResourceRuleVersions.Add(new ResourceRuleVersion
            {
                Category = "PaddyInputStandard",
                ApplicabilityJson = System.Text.Json.JsonSerializer.Serialize(defaultPayload),
                MinimumValue = 50m,
                MaximumValue = 300m,
                Unit = "kg/ha",
                SourceReference = "Department of Agriculture Sri Lanka (DOA)",
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EffectiveTo = null,
                Status = "Active",
                Version = 1,
                ReviewedBy = "DOA Technical Division",
                ReviewedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ChangeReason = "Initial baseline standard"
            });
            context.SaveChanges();
        }

        if (context.Users.Any())
        {
            return;
        }

        var users = new[]
        {
            new User
            {
                Email = "farmer@swarnabhoomi.lk",
                FirstName = "Nimal",
                LastName = "Perera",
                PhoneNumber = "+94771234567",
                Role = UserRole.Farmer,
                IsActive = true,
                District = "Kurunegala",
                Province = "North Western"
            },
            new User
            {
                Email = "buyer@swarnabhoomi.lk",
                FirstName = "Sandun",
                LastName = "Fernando",
                PhoneNumber = "+94777654321",
                Role = UserRole.Buyer,
                IsActive = true,
                District = "Anuradhapura",
                Province = "North Central"
            }
        };

        context.Users.AddRange(users);

        var varieties = new[]
        {
            new RiceVariety
            {
                VarietyCode = "BG300",
                Name = "BG 300",
                Description = "Popular medium-duration rice variety",
                MaturityDays = 120,
                AverageYieldKgPerHectare = 5400m,
                IsActive = true
            },
            new RiceVariety
            {
                VarietyCode = "AT362",
                Name = "AT 362",
                Description = "High-yielding rice variety for wet zones",
                MaturityDays = 135,
                AverageYieldKgPerHectare = 6000m,
                IsActive = true
            },
            new RiceVariety
            {
                VarietyCode = "BW367",
                Name = "BW 367",
                Description = "Well-suited for dry-zone paddy cultivation",
                MaturityDays = 110,
                AverageYieldKgPerHectare = 5000m,
                IsActive = true
            }
        };

        context.RiceVarieties.AddRange(varieties);
        context.SaveChanges();

        var farmer = context.Users.Single(u => u.Email == "farmer@swarnabhoomi.lk");
        var buyer = context.Users.Single(u => u.Email == "buyer@swarnabhoomi.lk");

        context.Farms.AddRange(
            new Farm
            {
                UserId = farmer.Id,
                Name = "Parakrama Paddy Farm",
                Description = "Mixed paddy cultivation plot",
                AreaHectares = 12.5m,
                District = "Kurunegala",
                Province = "North Western",
                Address = "Maha-Illuppalama road",
                Latitude = 7.47,
                Longitude = 80.37
            }
        );

        var bg300 = context.RiceVarieties.Single(v => v.VarietyCode == "BG300");

        context.Demands.AddRange(
            new Demand
            {
                OwnerId = buyer.Id,
                District = "Anuradhapura",
                Season = "Yala 2027",
                PeriodStart = new DateOnly(2027, 3, 1),
                PeriodEnd = new DateOnly(2027, 6, 30),
                Status = "Submitted",
                Notes = "Need high-quality paddy stock for milling",
                DemandItems =
                {
                    new DemandItem
                    {
                        RiceVarietyId = bg300.Id,
                        Grade = "Grade A",
                        QuantityTonnes = 50m,
                        OfferPricePerKg = 0.075m,
                        Notes = "Minimum moisture requirement 14%"
                    }
                }
            }
        );

        context.MarketPriceObservations.AddRange(
            new MarketPriceObservation
            {
                Market = "Dambulla",
                RiceVarietyId = bg300.Id,
                Grade = "Grade A",
                PriceType = "Wholesale",
                Value = 71.50m,
                Currency = "LKR",
                Unit = "kg",
                ObservationDate = new DateOnly(2027, 2, 15),
                Source = "Provincial market bulletin"
            }
        );

        context.WeatherObservations.AddRange(
            new WeatherObservation
            {
                Location = "Kurunegala",
                Latitude = 7.47,
                Longitude = 80.36,
                ObservationDate = new DateOnly(2027, 2, 15),
                RainfallMm = 18.4m,
                TemperatureC = 29.6m,
                HumidityPercent = 70m,
                Provider = "OpenWeather"
            }
        );

        context.SaveChanges();
    }
}