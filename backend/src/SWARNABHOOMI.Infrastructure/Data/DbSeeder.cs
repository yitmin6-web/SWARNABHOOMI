using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Core.Enums;

namespace SWARNABHOOMI.Infrastructure.Data;

public static class DbSeeder
{
    public static void Seed(SWARNABHOOMIContext context)
    {
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