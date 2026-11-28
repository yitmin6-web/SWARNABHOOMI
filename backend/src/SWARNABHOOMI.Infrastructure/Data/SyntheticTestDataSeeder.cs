using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SWARNABHOOMI.Core.Entities;
using SWARNABHOOMI.Core.Enums;

namespace SWARNABHOOMI.Infrastructure.Data;

/// <summary>
/// Deterministic, collision-free, idempotent synthetic data seeder for SWARNABHOOMI.
/// Populates RRDI 2016 Sri Lankan rice varieties, officers, farmers with non-overlapping GIS fields,
/// cultivations, forecasts, harvests, stocks, buyers, demands, purchase requests, and baseline observations.
/// </summary>
public static class SyntheticTestDataSeeder
{
    private const int DeterministicSeed = 20261128;
    private const string SentinelFarmerEmail = "farmer001@synth.swarnabhoomi.lk";
    private const string DemoPassword = "Demo@12345678";
    private const string ActiveSeasonName = "2026/2027 Maha";
    private const double EarthRadiusMetres = 6_371_008.8;

    private sealed record VarietySpec(
        string Code,
        string Name,
        int MaturityDays,
        decimal AverageYieldKgPerHectare,
        string Description);

    private sealed record DistrictAnchor(
        string District,
        string Province,
        double BaseLat,
        double BaseLng,
        int FarmerWeight,
        string[] PreferredVarietyNames,
        decimal AvgTempC,
        decimal AvgRainfallMm,
        decimal AvgHumidityPercent);

    private sealed record BuyerSpec(
        string BusinessName,
        string ContactFirst,
        string ContactLast,
        string District,
        string Province,
        string RegNumber,
        decimal MonthlyDemandTonnes,
        string PreferredVarieties);

    private static readonly VarietySpec[] RrdiVarieties =
    [
        // New 2016 RRDI releases
        new("BG252", "Bg 252", 85, 4500m, "Red Short Round (Samba); selection from Bg 250 for drought-prone areas, lodging resistant for mechanical harvesting; potential yield 9.95 t/ha."),
        new("LD253", "Ld 253", 85, 4800m, "White Long Slender (Nadu); At 04 selection with soft cooked rice and no seed discoloration in wet zone (DZ 5.5 t/ha, WZ 3.5-4.5 t/ha)."),
        new("BG374", "Bg 374", 105, 5300m, "White Intermediate Bold (Nadu); pedigree 12-38-01/Bg 360 for general cultivation; high yield potential 10.5 t/ha."),

        // Top 10 nationally cultivated varieties (RRDI 2016)
        new("BG352", "Bg 352", 105, 5200m, "White Long Grain Nadu; top nationally cultivated variety (16.4% national extent)."),
        new("BG300", "Bg 300", 90, 5000m, "White Long Grain Nadu; widely adapted 3-month variety (15.4% national extent)."),
        new("AT362", "At 362", 105, 5800m, "Red Long Grain Nadu; high-yielding variety popular across wet and dry zones (13.3% national extent)."),
        new("BG360", "Bg 360", 105, 4300m, "White Short Grain Keeri Samba; premium aromatic small-grain samba (8.0% national extent)."),
        new("BG358", "Bg 358", 105, 4800m, "White Short Grain Samba; high-milling recovery samba variety (7.3% national extent)."),
        new("BG94-1", "Bg 94-1", 105, 5100m, "White Long Grain Nadu; reliable irrigated dry-zone standard (5.6% national extent)."),
        new("BG366", "Bg 366", 105, 5400m, "White Long Grain Nadu; high-yielding irrigated variety (4.9% national extent)."),
        new("BG359", "Bg 359", 105, 5000m, "White Long Grain Nadu; adaptable lowland variety (4.6% national extent)."),
        new("AT308", "At 308", 90, 4900m, "White Short Grain Samba; short-duration samba variety (3.5% national extent)."),
        new("BW367", "Bw 367", 105, 4700m, "White Short Grain Samba; iron-toxicity tolerant samba variety (2.8% national extent)."),

        // Additional RRDI post-2005, regional & traditional heirloom varieties
        new("BG250", "Bg 250", 80, 4400m, "White Intermediate Bold; ultra-short duration drought and flood escape variety."),
        new("BG251", "Bg 251", 80, 4500m, "White Intermediate Bold; ultra-short duration variety suited for drought-prone tracts."),
        new("AT307", "At 307", 90, 4900m, "White Intermediate Bold; 3-month variety for general cultivation."),
        new("AT309", "At 309", 90, 4800m, "White Long Slender; 3-month variety suited for rice-based food products."),
        new("BG310", "Bg 310", 90, 4600m, "White Intermediate Bold; 3-month salinity-tolerant variety."),
        new("AT311", "At 311", 90, 4700m, "Red Long Slender (Neeroga); 3-month nutritious red slender variety."),
        new("BW364", "Bw 364", 105, 4600m, "Red Intermediate Bold; Wet Zone iron-toxicity tolerant variety."),
        new("LD365", "Ld 365", 105, 4300m, "Red Short Round; Wet Zone lowland cultivation variety."),
        new("LD368", "Ld 368", 105, 4400m, "Red Short Round; Wet Zone adapted variety."),
        new("BG369", "Bg 369", 105, 4500m, "White Long Medium; salinity-prone coastal paddy variety."),
        new("BG370", "Bg 370", 105, 4800m, "White Short Round; 3.5-month samba-type high-yielding variety."),
        new("LD371", "Ld 371", 105, 4300m, "White Short Round; Wet Zone adapted short-grain variety."),
        new("BW372", "Bw 372", 105, 4500m, "Red Intermediate Bold; Wet Zone iron-toxicity tolerant variety."),
        new("AT373", "At 373", 105, 4700m, "White Short Round (Ambalantota Suwanda Samba); fragrant southern samba."),
        new("BG379-2", "Bg 379-2", 125, 5500m, "White Long Grain; 4-month high-yielding Maha season variety."),
        new("BG403", "Bg 403", 125, 5200m, "White Long Grain; 4-month irrigated Maha season variety."),
        new("BG406", "Bg 406", 125, 4900m, "Red Intermediate Bold; 4-month variety widely grown in Northern region."),
        new("BG407H", "Bg 407H", 125, 6500m, "White Intermediate Bold; first RRDI hybrid rice variety."),
        new("LD408", "Ld 408", 125, 4800m, "Red Intermediate Bold; 4-month Wet Zone lowland variety."),
        new("BG450", "Bg 450", 135, 4900m, "White Short Grain; 4.5-month long-duration samba variety."),
        new("BG454", "Bg 454", 135, 5100m, "White Intermediate Bold; 4.5-month Maha season variety."),
        new("BG455", "Bg 455", 135, 4700m, "Red Intermediate Bold; 4.5-month flood-prone lowland variety."),
        new("SUWADEL", "Suwadel", 105, 3200m, "Traditional heirloom White Short Grain; prized fragrant heritage rice."),
        new("KALUHEENATI", "Kalu Heenati", 105, 3100m, "Traditional heirloom Red Long Grain; nutrient-rich heritage variety."),
        new("PACHCHAPERUMAL", "Pachchaperumal", 90, 3300m, "Traditional heirloom Red Long Grain; short-duration heritage variety."),
        new("MADATHAWALU", "Madathawalu", 105, 3000m, "Traditional heirloom Red Long Grain; hardy heritage paddy."),
        new("KURULUTHUDA", "Kuruluthuda", 125, 3100m, "Traditional heirloom Red Long Grain; heritage lowland rice."),
        new("MODDAIKARUPPAN", "Moddaikaruppan", 125, 3400m, "Traditional heirloom Red Long Grain; Northern Jaffna peninsula heritage rice."),
        new("MURUNKAN", "Murunkan", 165, 3200m, "Traditional heirloom Red Long Grain; long-duration Northern heritage variety.")
    ];

    // Coordinates anchored to inland Sri Lankan paddy-producing agricultural basins.
    private static readonly DistrictAnchor[] DistrictAnchors =
    [
        new("Anuradhapura", "North Central Province", 8.2750, 80.4250, 20, ["Bg 352", "Bg 300", "Bg 358", "Bg 360", "Bg 252", "Bg 374"], 30.8m, 14.5m, 72m),
        new("Polonnaruwa", "North Central Province", 7.9250, 81.0050, 18, ["Bg 352", "Bg 366", "Bg 360", "Bg 374", "Bg 300", "Bg 358"], 31.2m, 12.0m, 71m),
        new("Ampara", "Eastern Province", 7.3100, 81.6600, 18, ["At 362", "Bg 94-1", "Bg 352", "Bg 374", "Bg 300", "Bg 252"], 30.4m, 18.0m, 74m),
        new("Kurunegala", "North Western Province", 7.4950, 80.3400, 17, ["Bg 352", "Bg 300", "Bg 358", "Bg 359", "Bg 360", "At 308"], 29.8m, 16.5m, 75m),
        new("Hambantota", "Southern Province", 6.2350, 81.0750, 12, ["At 362", "Bg 94-1", "At 373", "Bg 352", "Bg 252", "At 308"], 30.9m, 9.5m, 70m),
        new("Batticaloa", "Eastern Province", 7.6650, 81.6400, 11, ["Bg 94-1", "At 362", "Bg 352", "Bg 369", "Bg 300"], 30.6m, 21.0m, 76m),
        new("Trincomalee", "Eastern Province", 8.5350, 81.1400, 9, ["Bg 352", "Bg 94-1", "Bg 300", "At 362", "Bg 250"], 30.7m, 15.0m, 74m),
        new("Galle", "Southern Province", 6.1350, 80.2450, 9, ["At 362", "Ld 368", "Ld 365", "Ld 253", "Bw 367", "Suwadel"], 28.4m, 28.5m, 82m),
        new("Matara", "Southern Province", 6.0250, 80.5450, 8, ["At 362", "Ld 368", "Ld 253", "Bw 364", "Suwadel"], 28.6m, 24.0m, 81m),
        new("Kandy", "Central Province", 7.2650, 80.6150, 7, ["Bg 352", "Bg 300", "Bg 359", "Bw 367", "Kalu Heenati"], 26.2m, 22.0m, 79m),
        new("Monaragala", "Uva Province", 6.8750, 81.3350, 6, ["Bg 300", "Bg 352", "Bg 252", "At 362", "Bg 251"], 29.9m, 11.0m, 72m),
        new("Badulla", "Uva Province", 6.9750, 81.0550, 5, ["Bg 300", "Bg 352", "Bg 359", "Bg 374", "Pachchaperumal"], 25.8m, 19.5m, 77m),
        new("Ratnapura", "Sabaragamuwa Province", 6.7250, 80.3850, 4, ["At 362", "Ld 365", "Bw 364", "Ld 253", "Bw 372"], 28.1m, 31.0m, 83m),
        new("Mannar", "Northern Province", 8.9450, 79.9650, 3, ["Bg 352", "Bg 406", "Bg 300", "Bg 252", "Murunkan"], 31.4m, 8.0m, 68m),
        new("Jaffna", "Northern Province", 9.6550, 80.1250, 3, ["Bg 406", "Moddaikaruppan", "Bg 300", "Bg 369", "Murunkan"], 31.1m, 10.5m, 71m)
    ];

    private static readonly (string District, string Province)[] OfficerJurisdictions =
    [
        ("Anuradhapura", "North Central Province"),
        ("Polonnaruwa", "North Central Province"),
        ("Ampara", "Eastern Province"),
        ("Kurunegala", "North Western Province"),
        ("Hambantota", "Southern Province"),
        ("Batticaloa", "Eastern Province"),
        ("Galle", "Southern Province"),
        ("Matara", "Southern Province"),
        ("Trincomalee", "Eastern Province"),
        ("Kandy", "Central Province")
    ];

    private static readonly string[] SriLankanFirstNames =
    [
        "Nimal", "Kamal", "Sunil", "Ruwan", "Saman", "Pradeep", "Chaminda", "Tharindu",
        "Dilshan", "Asanka", "Mahesh", "Kasun", "Nuwan", "Chathura", "Dinesh", "Upul",
        "Bandara", "Rohan", "Lalith", "Anura", "Sanjeewa", "Gayan", "Harsha", "Janaka",
        "Nadeesha", "Kumari", "Sanduni", "Malini", "Sewwandi", "Dilani", "Ayesha", "Nirosha",
        "Suresh", "Kanthi", "Jayantha", "Wijitha", "Ranjith", "Sarath", "Gamini", "Piyal",
        "Selvam", "Karthik", "Nadarajah", "Fathima", "Rifaz"
    ];

    private static readonly string[] SriLankanLastNames =
    [
        "Perera", "Fernando", "Bandara", "Dissanayake", "Jayasinghe", "Senanayake", "Wickramasinghe",
        "Rajapaksa", "Herath", "Ratnayake", "Gunasekara", "Weerasinghe", "Karunaratne", "Ekanayake",
        "Liyanage", "Ranasinghe", "Jayawardena", "Pathirana", "Samarasinghe", "Amarasinghe",
        "Premaratne", "Nanayakkara", "Abeysinghe", "Kularatne", "Wijesinghe", "Subasinghe",
        "Alwis", "De Silva", "Hettiarachchi", "Mendis", "Navaratne", "Thilakaratne",
        "Sivakumar", "Shanmugam", "Farook", "Marikkar"
    ];

    private static readonly BuyerSpec[] BuyerCatalog =
    [
        new("Araliya Rice Mills (Pvt) Ltd", "Dudley", "Sirisena", "Polonnaruwa", "North Central Province", "PV-10421", 420m, "Bg 352, Bg 360, Bg 358"),
        new("Nipuna Paddy & Rice Processing", "Channa", "Weerasinghe", "Polonnaruwa", "North Central Province", "PV-11804", 350m, "Bg 352, Bg 366, Bg 374"),
        new("New Rathna Rice Producers", "Lakshman", "Senanayake", "Anuradhapura", "North Central Province", "PV-12088", 290m, "Bg 300, Bg 352, Bg 358"),
        new("Lanka Sathosa Grain Procurement", "Nalin", "Jayasinghe", "Colombo", "Western Province", "GOV-4401", 600m, "Bg 352, Bg 300, At 362"),
        new("Cargills Agri Supply Chain", "Roshani", "De Silva", "Colombo", "Western Province", "PQ-2219", 480m, "Bg 360, At 362, Suwadel"),
        new("Keells Fresh & Staples Sourcing", "Shehan", "Fernando", "Gampaha", "Western Province", "PQ-3105", 410m, "Bg 360, Bg 358, Ld 253"),
        new("Ruhunu Agro Grain Traders", "Upali", "Pathirana", "Matara", "Southern Province", "BR-5512", 180m, "At 362, Ld 253, Suwadel"),
        new("Galle Southern Paddy Co-op", "Priyantha", "Nanayakkara", "Galle", "Southern Province", "COOP-774", 160m, "At 362, Ld 368, Bw 367"),
        new("Digamadulla Rice Millers Union", "Hameed", "Farook", "Ampara", "Eastern Province", "COOP-892", 340m, "At 362, Bg 94-1, Bg 352"),
        new("Eastern Golden Harvest Mills", "Rifaz", "Marikkar", "Ampara", "Eastern Province", "PV-18902", 260m, "Bg 94-1, Bg 374, Bg 252"),
        new("Wayamba Rice & Flour Complex", "Asela", "Herath", "Kurunegala", "North Western Province", "PV-19311", 310m, "Bg 300, Bg 352, Bg 358"),
        new("Deduru Oya Grain Collective", "Sumith", "Ekanayake", "Kurunegala", "North Western Province", "COOP-619", 195m, "Bg 359, At 308, Bg 360"),
        new("Kandyan Heritage Rice Exports", "Damith", "Ratnayake", "Kandy", "Central Province", "EXP-3302", 120m, "Suwadel, Kalu Heenati, Bg 360"),
        new("Central Province Co-op Wholesale", "Nirmala", "Bandara", "Kandy", "Central Province", "COOP-408", 210m, "Bg 352, Bg 300, Bg 358"),
        new("Valikamam Paddy Marketing Board", "Selvam", "Sivakumar", "Jaffna", "Northern Province", "COOP-951", 150m, "Bg 406, Moddaikaruppan, Bg 300"),
        new("Walawe Basin Millers", "Chinthaka", "Abeysinghe", "Hambantota", "Southern Province", "PV-21044", 240m, "At 362, At 373, Bg 94-1"),
        new("Kelani Valley Rice Distributors", "Dinesh", "Mendis", "Gampaha", "Western Province", "PV-21560", 275m, "Bg 352, Bg 360, At 362"),
        new("Pettah Wholesale Grain Exchange", "Malinga", "Wijesinghe", "Colombo", "Western Province", "BR-7780", 380m, "Bg 352, Bg 300, Bg 358"),
        new("Uva Dry Zone Grain Traders", "Sagara", "Dissanayake", "Monaragala", "Uva Province", "BR-8120", 145m, "Bg 252, Bg 300, At 362"),
        new("Mahaweli Rice Marketing Society", "Jagath", "Gunasekara", "Polonnaruwa", "North Central Province", "COOP-1024", 285m, "Bg 352, Bg 374, Bg 366")
    ];

    /// <summary>
    /// Seeds RRDI varieties and synthetic users/records idempotently.
    /// </summary>
    public static async Task SeedAsync(
        SWARNABHOOMIContext db,
        Func<User, string, string> hashPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(hashPassword);

        var varietyLookup = await UpsertRrdiVarietiesAsync(db, cancellationToken);

        var passwordHash = hashPassword(new User { Email = SentinelFarmerEmail }, DemoPassword);
        await EnsureGovernmentAnalystAsync(db, passwordHash, cancellationToken);

        var alreadySeeded = await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == SentinelFarmerEmail, cancellationToken);

        if (alreadySeeded)
        {
            return;
        }

        var useTransaction = db.Database.IsRelational();
        await using var transaction = useTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var rng = new Random(DeterministicSeed);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var nowUtc = DateTime.UtcNow;

            var officersByDistrict = await SeedOfficersAsync(db, passwordHash, nowUtc, cancellationToken);
            var weatherByDistrict = await SeedBaselineWeatherAndPricesAsync(db, varietyLookup, today, cancellationToken);
            var (farmers, fieldsWithContext) = await SeedFarmersAndFieldsAsync(
                db,
                rng,
                passwordHash,
                officersByDistrict,
                nowUtc,
                cancellationToken);

            var (harvestedLots, activeCultivations) = await SeedCultivationsForecastsAndHarvestsAsync(
                db,
                rng,
                varietyLookup,
                weatherByDistrict,
                fieldsWithContext,
                today,
                nowUtc,
                cancellationToken);

            var buyers = await SeedBuyersAsync(db, passwordHash, nowUtc, cancellationToken);
            var demandItems = await SeedDemandsAsync(db, rng, buyers, varietyLookup, today, cancellationToken);

            await SeedPurchaseRequestsAndNotificationsAsync(
                db,
                rng,
                buyers,
                farmers,
                demandItems,
                harvestedLots,
                activeCultivations,
                today,
                nowUtc,
                cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    private static async Task<Dictionary<string, RiceVariety>> UpsertRrdiVarietiesAsync(
        SWARNABHOOMIContext db,
        CancellationToken cancellationToken)
    {
        var existing = await db.RiceVarieties.ToListAsync(cancellationToken);
        var byNormalizedKey = new Dictionary<string, RiceVariety>(StringComparer.OrdinalIgnoreCase);

        foreach (var variety in existing)
        {
            byNormalizedKey.TryAdd(NormalizeVarietyKey(variety.VarietyCode), variety);
            byNormalizedKey.TryAdd(NormalizeVarietyKey(variety.Name), variety);
        }

        var addedAny = false;
        foreach (var spec in RrdiVarieties)
        {
            var normCode = NormalizeVarietyKey(spec.Code);
            var normName = NormalizeVarietyKey(spec.Name);

            if (byNormalizedKey.TryGetValue(normCode, out var matched) ||
                byNormalizedKey.TryGetValue(normName, out matched))
            {
                byNormalizedKey.TryAdd(normCode, matched);
                byNormalizedKey.TryAdd(normName, matched);
                continue;
            }

            var entity = new RiceVariety
            {
                VarietyCode = spec.Code,
                Name = spec.Name,
                MaturityDays = spec.MaturityDays,
                AverageYieldKgPerHectare = spec.AverageYieldKgPerHectare,
                Description = spec.Description,
                IsActive = true,
                CreatedBy = nameof(SyntheticTestDataSeeder)
            };

            db.RiceVarieties.Add(entity);
            byNormalizedKey[normCode] = entity;
            byNormalizedKey[normName] = entity;
            addedAny = true;
        }

        if (addedAny)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return byNormalizedKey;
    }

    private static async Task EnsureGovernmentAnalystAsync(
        SWARNABHOOMIContext db,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        var hasActiveGovernmentUser = await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Role == UserRole.Government && u.IsActive, cancellationToken);

        if (hasActiveGovernmentUser)
        {
            return;
        }

        var govUser = new User
        {
            Email = "gov.director@synth.swarnabhoomi.lk",
            PasswordHash = passwordHash,
            FirstName = "Chandrika",
            LastName = "Wickramasinghe",
            PhoneNumber = "+94 11 269 4001",
            Role = UserRole.Government,
            IsEmailConfirmed = true,
            IsPhoneConfirmed = true,
            IsActive = true,
            IsVerified = true,
            District = "Colombo",
            Province = "Western Province",
            CreatedBy = nameof(SyntheticTestDataSeeder)
        };

        db.Users.Add(govUser);
        db.GovernmentProfiles.Add(new GovernmentProfile
        {
            UserId = govUser.Id,
            Institution = "Department of Agriculture Sri Lanka",
            Department = "Rice Research and Development Institute (RRDI)",
            Designation = "Director of National Paddy Analytics",
            District = "Colombo",
            Province = "Western Province",
            CreatedBy = nameof(SyntheticTestDataSeeder)
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, User>> SeedOfficersAsync(
        SWARNABHOOMIContext db,
        string passwordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var officersByDistrict = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < OfficerJurisdictions.Length; i++)
        {
            var number = i + 1;
            var (district, province) = OfficerJurisdictions[i];
            var firstName = SriLankanFirstNames[(i * 3 + 5) % SriLankanFirstNames.Length];
            var lastName = SriLankanLastNames[(i * 5 + 2) % SriLankanLastNames.Length];

            var officer = new User
            {
                Email = $"officer{number:D2}@synth.swarnabhoomi.lk",
                PasswordHash = passwordHash,
                FirstName = firstName,
                LastName = lastName,
                PhoneNumber = $"+94 71 800 {number:D4}",
                Role = UserRole.Officer,
                IsEmailConfirmed = true,
                IsPhoneConfirmed = true,
                IsActive = true,
                IsVerified = true,
                District = district,
                Province = province,
                CreatedBy = nameof(SyntheticTestDataSeeder)
            };

            db.Users.Add(officer);
            db.OfficerAssignments.Add(new OfficerAssignment
            {
                UserId = officer.Id,
                District = district,
                Province = province,
                AssignedBy = "Department of Agriculture",
                AssignedDate = nowUtc.AddDays(-60),
                IsActive = true,
                CreatedBy = nameof(SyntheticTestDataSeeder)
            });

            officersByDistrict[district] = officer;
        }

        await db.SaveChangesAsync(cancellationToken);
        return officersByDistrict;
    }

    private static async Task<Dictionary<string, WeatherObservation>> SeedBaselineWeatherAndPricesAsync(
        SWARNABHOOMIContext db,
        Dictionary<string, RiceVariety> varietyLookup,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var weatherByDistrict = new Dictionary<string, WeatherObservation>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in DistrictAnchors)
        {
            var weather = new WeatherObservation
            {
                Location = anchor.District,
                Latitude = anchor.BaseLat,
                Longitude = anchor.BaseLng,
                ObservationDate = today,
                RainfallMm = anchor.AvgRainfallMm,
                TemperatureC = anchor.AvgTempC,
                HumidityPercent = anchor.AvgHumidityPercent,
                Provider = "Open-Meteo",
                CreatedBy = nameof(SyntheticTestDataSeeder)
            };
            db.WeatherObservations.Add(weather);
            weatherByDistrict[anchor.District] = weather;
        }

        var priceSchedules = new (string VarietyName, decimal WholesaleLkr, decimal RetailLkr, string[] Markets)[]
        {
            ("Bg 352", 118.50m, 215.00m, ["Dambulla", "Polonnaruwa", "Anuradhapura", "Pettah"]),
            ("Bg 300", 116.00m, 210.00m, ["Kurunegala", "Anuradhapura", "Dambulla", "Pettah"]),
            ("At 362", 122.00m, 222.00m, ["Ampara", "Hambantota", "Galle", "Matara"]),
            ("Bg 360", 158.00m, 275.00m, ["Polonnaruwa", "Dambulla", "Pettah", "Kandy"]),
            ("Bg 358", 134.00m, 240.00m, ["Anuradhapura", "Kurunegala", "Pettah"]),
            ("Bg 94-1", 115.00m, 208.00m, ["Ampara", "Batticaloa", "Trincomalee"]),
            ("Bg 374", 121.00m, 218.00m, ["Polonnaruwa", "Anuradhapura", "Ampara"]),
            ("Ld 253", 124.00m, 225.00m, ["Galle", "Matara", "Ratnapura"]),
            ("Bg 252", 126.00m, 228.00m, ["Hambantota", "Monaragala", "Anuradhapura"]),
            ("Suwadel", 188.00m, 340.00m, ["Galle", "Kandy", "Colombo"])
        };

        foreach (var entry in priceSchedules)
        {
            var variety = ResolveVariety(varietyLookup, entry.VarietyName);
            foreach (var market in entry.Markets)
            {
                db.MarketPriceObservations.Add(new MarketPriceObservation
                {
                    Market = market,
                    RiceVarietyId = variety.Id,
                    Grade = "Grade A",
                    PriceType = "Wholesale",
                    Value = entry.WholesaleLkr,
                    Currency = "LKR",
                    Unit = "kg",
                    ObservationDate = today.AddDays(-1),
                    PublicationDate = today.AddDays(-1),
                    Source = "CBSL Daily Price Report",
                    CreatedBy = nameof(SyntheticTestDataSeeder)
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return weatherByDistrict;
    }

    private sealed record SeededFieldContext(
        User Farmer,
        Farm Farm,
        Field Field,
        string VerificationStatus,
        DistrictAnchor Anchor,
        User? AssignedOfficer);

    private static async Task<(List<User> Farmers, List<SeededFieldContext> Fields)> SeedFarmersAndFieldsAsync(
        SWARNABHOOMIContext db,
        Random rng,
        string passwordHash,
        Dictionary<string, User> officersByDistrict,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var districtPool = BuildWeightedDistrictList();
        var districtGridCounters = DistrictAnchors.ToDictionary(a => a.District, _ => 0, StringComparer.OrdinalIgnoreCase);

        var farmers = new List<User>(150);
        var fieldContexts = new List<SeededFieldContext>(240);

        for (var i = 1; i <= 150; i++)
        {
            var anchor = districtPool[(i - 1) % districtPool.Count];
            var firstName = SriLankanFirstNames[(i * 7 + 1) % SriLankanFirstNames.Length];
            var lastName = SriLankanLastNames[(i * 11 + 3) % SriLankanLastNames.Length];
            var primaryVariety = anchor.PreferredVarietyNames[i % anchor.PreferredVarietyNames.Length];
            var nic = $"{1968 + (i % 32)}{100 + i:D3}{10000 + i:D5}";

            var farmer = new User
            {
                Email = $"farmer{i:D3}@synth.swarnabhoomi.lk",
                PasswordHash = passwordHash,
                FirstName = firstName,
                LastName = lastName,
                PhoneNumber = $"+94 77 100 {i:D4}",
                Role = UserRole.Farmer,
                IsEmailConfirmed = true,
                IsPhoneConfirmed = true,
                IsActive = true,
                IsVerified = true,
                District = anchor.District,
                Province = anchor.Province,
                CreatedBy = nameof(SyntheticTestDataSeeder)
            };
            db.Users.Add(farmer);
            farmers.Add(farmer);

            // 1 or 2 fields per farmer (~225 total fields across 150 farms)
            var fieldCount = (i % 2 == 0) ? 2 : 1;
            var generatedFields = new List<(Field Field, string Status)>(fieldCount);
            decimal farmTotalArea = 0m;

            var farm = new Farm
            {
                UserId = farmer.Id,
                Name = $"{lastName} {anchor.District} Paddy Tract {i:D3}",
                Description = $"Irrigated lowland paddy holding in {anchor.District}",
                District = anchor.District,
                Province = anchor.Province,
                Address = $"Tract {(i % 12) + 1}, {anchor.District}",
                Latitude = anchor.BaseLat,
                Longitude = anchor.BaseLng,
                CreatedBy = nameof(SyntheticTestDataSeeder)
            };

            for (var fIndex = 0; fIndex < fieldCount; fIndex++)
            {
                var cellIndex = districtGridCounters[anchor.District]++;
                var row = cellIndex / 6;
                var col = cellIndex % 6;

                // Deterministic non-overlapping 0.0035 degree (~388m) grid cell
                var cellCenterLat = anchor.BaseLat + (row * 0.0035);
                var cellCenterLng = anchor.BaseLng + (col * 0.0035);

                // Target 0.40 - 3.80 ha (~64m to ~195m square), strictly inside the 388m cell
                var targetAreaHa = 0.40 + rng.NextDouble() * 3.40;
                var (geoJson, measuredAreaHa) = BuildRectanglePolygon(cellCenterLat, cellCenterLng, targetAreaHa);
                farmTotalArea += measuredAreaHa;

                var field = new Field
                {
                    FarmId = farm.Id,
                    Name = fieldCount == 1 ? $"Liyadda A-{i:D3}" : $"Liyadda {(char)('A' + fIndex)}-{i:D3}",
                    AreaHectares = measuredAreaHa,
                    Latitude = Math.Round(cellCenterLat, 6),
                    Longitude = Math.Round(cellCenterLng, 6),
                    BoundaryGeoJson = geoJson,
                    CreatedBy = farmer.Id.ToString()
                };

                // ~75% Verified, ~20% Pending, ~5% Rejected
                var roll = rng.Next(100);
                var verifStatus = roll < 75 ? "Verified" : (roll < 95 ? "Pending" : "Rejected");
                generatedFields.Add((field, verifStatus));
            }

            farm.AreaHectares = Math.Round(farmTotalArea + 0.15m, 4);
            farm.Latitude = generatedFields[0].Field.Latitude;
            farm.Longitude = generatedFields[0].Field.Longitude;
            db.Farms.Add(farm);

            db.FarmerProfiles.Add(new FarmerProfile
            {
                UserId = farmer.Id,
                NationalId = nic,
                Address = farm.Address,
                TotalLandAreaHectares = farm.AreaHectares,
                PrimaryVariety = primaryVariety,
                ExperienceYears = (5 + (i % 25)).ToString(CultureInfo.InvariantCulture),
                CreatedBy = nameof(SyntheticTestDataSeeder)
            });

            officersByDistrict.TryGetValue(anchor.District, out var assignedOfficer);

            foreach (var (field, verifStatus) in generatedFields)
            {
                db.Fields.Add(field);

                var reviewNotes = verifStatus switch
                {
                    "Verified" => "GIS boundary verified against agrarian tract survey.",
                    "Rejected" => "Boundary overlaps irrigation canal — please redraw.",
                    _ => null
                };

                db.FieldVerifications.Add(new FieldVerification
                {
                    FieldId = field.Id,
                    Status = verifStatus,
                    ReviewerId = verifStatus == "Pending" ? null : assignedOfficer?.Id.ToString() ?? "AgrarianOfficer",
                    ReviewedAt = verifStatus == "Pending" ? null : nowUtc.AddDays(-rng.Next(5, 45)),
                    ReviewNotes = reviewNotes,
                    CreatedBy = farmer.Id.ToString()
                });

                if (verifStatus == "Rejected")
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId = farmer.Id,
                        Type = "FieldVerification",
                        MessageKey = "Field Verification Update",
                        Payload = JsonSerializer.Serialize(new
                        {
                            Title = "Field Verification Update",
                            Message = $"Field '{field.Name}' requires revision: {reviewNotes}",
                            Category = "Field Verification",
                            Link = "/field-map",
                            ReferenceId = field.Id.ToString()
                        }),
                        IsRead = false,
                        DeliveryStatus = "Delivered",
                        CreatedBy = "System"
                    });
                }

                fieldContexts.Add(new SeededFieldContext(farmer, farm, field, verifStatus, anchor, assignedOfficer));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (farmers, fieldContexts);
    }

    private sealed record SeededStockLot(User Farmer, StockRecord Stock, string District);

    private static async Task<(List<SeededStockLot> StockLots, List<CultivationRecord> ActiveCultivations)>
        SeedCultivationsForecastsAndHarvestsAsync(
            SWARNABHOOMIContext db,
            Random rng,
            Dictionary<string, RiceVariety> varietyLookup,
            Dictionary<string, WeatherObservation> weatherByDistrict,
            List<SeededFieldContext> fieldContexts,
            DateOnly today,
            DateTime nowUtc,
            CancellationToken cancellationToken)
    {
        var stockLots = new List<SeededStockLot>();
        var activeCultivations = new List<CultivationRecord>();

        for (var idx = 0; idx < fieldContexts.Count; idx++)
        {
            var ctx = fieldContexts[idx];
            var varietyName = ctx.Anchor.PreferredVarietyNames[idx % ctx.Anchor.PreferredVarietyNames.Length];
            var variety = ResolveVariety(varietyLookup, varietyName);

            // Distribute growth stages: 25% Sown/Vegetative, 30% Flowering, 20% Maturity (3-14d left), 25% Harvested
            var stageRoll = idx % 20;
            string status;
            DateOnly plantingDate;
            DateOnly expectedHarvestDate;

            if (stageRoll < 5)
            {
                status = stageRoll < 2 ? "Sown" : "Vegetative";
                var daysSincePlanting = rng.Next(15, 56);
                plantingDate = today.AddDays(-daysSincePlanting);
                expectedHarvestDate = plantingDate.AddDays(variety.MaturityDays);
            }
            else if (stageRoll < 11)
            {
                status = "Flowering";
                var daysSincePlanting = rng.Next(56, Math.Min(79, Math.Max(60, variety.MaturityDays - 15)));
                plantingDate = today.AddDays(-daysSincePlanting);
                expectedHarvestDate = plantingDate.AddDays(variety.MaturityDays);
            }
            else if (stageRoll < 15)
            {
                status = "Maturity";
                var daysUntilHarvest = rng.Next(3, 15);
                expectedHarvestDate = today.AddDays(daysUntilHarvest);
                plantingDate = expectedHarvestDate.AddDays(-variety.MaturityDays);
            }
            else
            {
                status = "Harvested";
                var daysSinceHarvest = rng.Next(2, 22);
                expectedHarvestDate = today.AddDays(-daysSinceHarvest);
                var cycleDays = Math.Max(variety.MaturityDays, 100 + rng.Next(0, 26));
                plantingDate = expectedHarvestDate.AddDays(-cycleDays);
            }

            var cultivation = new CultivationRecord
            {
                FarmerId = ctx.Farmer.Id,
                FieldId = ctx.Field.Id,
                RiceVarietyId = variety.Id,
                Season = ActiveSeasonName,
                PlantingDate = plantingDate,
                ExpectedHarvestDate = expectedHarvestDate,
                AreaHectares = ctx.Field.AreaHectares,
                Status = status,
                Notes = $"{ActiveSeasonName} cultivation of {variety.Name} in {ctx.Anchor.District}",
                CreatedBy = ctx.Farmer.Id.ToString()
            };
            db.CultivationRecords.Add(cultivation);

            var verificationMultiplier = ctx.VerificationStatus.ToLowerInvariant() switch
            {
                "verified" => 1.00m,
                "rejected" => 0.00m,
                _ => 0.90m
            };

            weatherByDistrict.TryGetValue(ctx.Anchor.District, out var weather);
            var weatherMultiplier = 1.00m;
            var weatherSummary = "Standard weather profile (1.00x)";
            if (weather is not null)
            {
                if (weather.RainfallMm > 150m || weather.TemperatureC > 38m || weather.TemperatureC < 18m)
                {
                    weatherMultiplier = 0.92m;
                    weatherSummary = $"Adverse climate anomaly ({weather.TemperatureC ?? 30:F1}°C, {weather.RainfallMm ?? 0:F0}mm) (-8%)";
                }
                else
                {
                    weatherSummary = $"Optimal climate condition ({weather.TemperatureC ?? 30:F1}°C, {weather.RainfallMm ?? 0:F0}mm) (1.00x)";
                }
            }

            var baseYieldKgPerHa = variety.AverageYieldKgPerHectare ?? 4500m;
            var predictedYieldKg = Math.Round(
                ctx.Field.AreaHectares * baseYieldKgPerHa * verificationMultiplier * weatherMultiplier,
                2);

            var confidence = verificationMultiplier switch
            {
                1.00m when weatherMultiplier == 1.00m => "High",
                0.90m => "Medium",
                _ => "Low"
            };

            db.HarvestForecasts.Add(new HarvestForecast
            {
                CultivationId = cultivation.Id,
                RunVersion = "v1.0",
                MethodVersion = "DOA-Yield-Model-v1",
                TargetWindowStart = expectedHarvestDate,
                TargetWindowEnd = expectedHarvestDate.AddDays(14),
                PredictedPaddyQuantityKg = predictedYieldKg,
                ConfidenceCategory = confidence,
                Assumptions = $"BaseYield={baseYieldKgPerHa}kg/ha;VerifMult={verificationMultiplier:F2} ({ctx.VerificationStatus});WeatherMult={weatherMultiplier:F2};{weatherSummary}",
                CreatedBy = "RiceIntelligenceEngine"
            });

            if (status == "Harvested")
            {
                var baselineForecast = predictedYieldKg > 0
                    ? predictedYieldKg
                    : Math.Round(ctx.Field.AreaHectares * baseYieldKgPerHa, 2);

                var accuracyFactor = 0.90m + (decimal)(rng.NextDouble() * 0.16); // 90% to 106%
                var actualKg = Math.Round(baselineForecast * accuracyFactor, 2);
                var moisture = Math.Round(13.5m + (decimal)(rng.NextDouble() * 3.0), 1);
                var grade = moisture <= 15.2m ? "Grade A" : "Grade B";
                var lossesKg = Math.Round(actualKg * 0.025m, 2);

                db.HarvestRecords.Add(new HarvestRecord
                {
                    CultivationId = cultivation.Id,
                    ActualHarvestDate = expectedHarvestDate,
                    ActualPaddyQuantityKg = actualKg,
                    Quality = $"{grade} ({moisture:F1}% moisture)",
                    LossesKg = lossesKg,
                    EvidenceReference = $"PMB-LOT-{ctx.Anchor.District[..3].ToUpperInvariant()}-{idx + 1:D4}",
                    VerificationStatus = ctx.VerificationStatus == "Verified" ? "Verified" : "Pending",
                    CreatedBy = ctx.Farmer.Id.ToString()
                });

                var netStockKg = Math.Round(Math.Max(250m, actualKg - lossesKg), 2);
                var stock = new StockRecord
                {
                    Location = ctx.Anchor.District,
                    RiceVarietyId = variety.Id,
                    Grade = grade,
                    CommodityStage = "Paddy",
                    Quantity = netStockKg,
                    Unit = "kg",
                    AsOfDate = expectedHarvestDate,
                    VerificationStatus = ctx.VerificationStatus == "Verified" ? "Verified" : "Pending",
                    CreatedBy = ctx.Farmer.Id.ToString()
                };
                db.StockRecords.Add(stock);
                stockLots.Add(new SeededStockLot(ctx.Farmer, stock, ctx.Anchor.District));
            }
            else
            {
                activeCultivations.Add(cultivation);

                if (status == "Maturity")
                {
                    var daysLeft = Math.Max(0, expectedHarvestDate.DayNumber - today.DayNumber);
                    var daysText = daysLeft == 0 ? "today" : daysLeft == 1 ? "tomorrow" : $"in {daysLeft} days";
                    db.Notifications.Add(new Notification
                    {
                        UserId = ctx.Farmer.Id,
                        Type = "HarvestAlert",
                        MessageKey = "Harvest Window Approaching",
                        Payload = JsonSerializer.Serialize(new
                        {
                            Title = "Harvest Window Approaching",
                            Message = $"Your {variety.Name} on field '{ctx.Field.Name}' is expected to reach harvest maturity {daysText} ({expectedHarvestDate:MMM dd, yyyy}). Prepare harvesting equipment and log yield upon completion.",
                            Category = "Harvest Alert",
                            Link = "/cultivation",
                            ReferenceId = cultivation.Id.ToString()
                        }),
                        IsRead = false,
                        DeliveryStatus = "Delivered",
                        CreatedBy = "RiceIntelligenceEngine",
                        CreatedAt = nowUtc
                    });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (stockLots, activeCultivations);
    }

    private static async Task<List<User>> SeedBuyersAsync(
        SWARNABHOOMIContext db,
        string passwordHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var buyers = new List<User>(BuyerCatalog.Length);

        for (var i = 0; i < BuyerCatalog.Length; i++)
        {
            var number = i + 1;
            var spec = BuyerCatalog[i];

            var buyer = new User
            {
                Email = $"buyer{number:D2}@synth.swarnabhoomi.lk",
                PasswordHash = passwordHash,
                FirstName = spec.ContactFirst,
                LastName = spec.ContactLast,
                PhoneNumber = $"+94 76 500 {number:D4}",
                Role = UserRole.Buyer,
                IsEmailConfirmed = true,
                IsPhoneConfirmed = true,
                IsActive = true,
                IsVerified = true,
                District = spec.District,
                Province = spec.Province,
                CreatedBy = nameof(SyntheticTestDataSeeder),
                CreatedAt = nowUtc
            };

            db.Users.Add(buyer);
            db.BuyerProfiles.Add(new BuyerProfile
            {
                UserId = buyer.Id,
                BusinessName = spec.BusinessName,
                BusinessRegistrationNumber = spec.RegNumber,
                Address = $"{spec.District}, {spec.Province}",
                ContactPerson = $"{spec.ContactFirst} {spec.ContactLast}",
                MonthlyDemandTonnes = spec.MonthlyDemandTonnes,
                PreferredVarieties = spec.PreferredVarieties,
                CreatedBy = nameof(SyntheticTestDataSeeder)
            });

            buyers.Add(buyer);
        }

        await db.SaveChangesAsync(cancellationToken);
        return buyers;
    }

    private static async Task<List<DemandItem>> SeedDemandsAsync(
        SWARNABHOOMIContext db,
        Random rng,
        List<User> buyers,
        Dictionary<string, RiceVariety> varietyLookup,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        string[] demandVarieties = ["Bg 352", "Bg 300", "At 362", "Bg 360", "Bg 358", "Bg 374", "Ld 253", "Bg 252", "Suwadel"];
        string[] targetDistricts = ["Anuradhapura", "Polonnaruwa", "Ampara", "Kurunegala", "Hambantota", "Batticaloa", "Galle", "Matara", "Trincomalee", "Kandy"];
        var allDemandItems = new List<DemandItem>();

        for (var i = 0; i < buyers.Count; i++)
        {
            var buyer = buyers[i];
            var demandCount = (i % 2 == 0) ? 2 : 1; // 30 active demands across 20 buyers

            for (var d = 0; d < demandCount; d++)
            {
                var targetDistrict = d == 0 && targetDistricts.Contains(buyer.District, StringComparer.OrdinalIgnoreCase)
                    ? buyer.District!
                    : targetDistricts[(i * 3 + d) % targetDistricts.Length];

                var demand = new Demand
                {
                    OwnerId = buyer.Id,
                    District = targetDistrict,
                    Season = ActiveSeasonName,
                    PeriodStart = today.AddDays(-10),
                    PeriodEnd = today.AddDays(60 + d * 25),
                    Status = d == 1 && i % 4 == 0 ? "PartiallyFulfilled" : "Open",
                    Notes = $"Commercial paddy procurement for {targetDistrict} milling and retail distribution",
                    CreatedBy = buyer.Id.ToString()
                };

                var itemCount = 2 + (i + d) % 2; // 2 to 3 items per demand
                for (var k = 0; k < itemCount; k++)
                {
                    var varietyName = demandVarieties[(i * 2 + d + k) % demandVarieties.Length];
                    var variety = ResolveVariety(varietyLookup, varietyName);
                    var qtyKg = rng.Next(2, 26) * 1000m; // 2,000 kg - 25,000 kg
                    var basePrice = varietyName switch
                    {
                        "Suwadel" => rng.Next(175, 196),
                        "Bg 360" => rng.Next(150, 176),
                        "Bg 358" => rng.Next(132, 152),
                        _ => rng.Next(115, 136)
                    };

                    var item = new DemandItem
                    {
                        DemandId = demand.Id,
                        RiceVarietyId = variety.Id,
                        Grade = k == 0 ? "Grade A" : "Grade B",
                        QuantityTonnes = Math.Round(qtyKg / 1000m, 3),
                        OfferPricePerKg = basePrice,
                        Notes = "Moisture <= 14.5%, clean winnowed paddy",
                        CreatedBy = buyer.Id.ToString()
                    };

                    demand.DemandItems.Add(item);
                    allDemandItems.Add(item);
                }

                db.Demands.Add(demand);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return allDemandItems;
    }

    private static async Task SeedPurchaseRequestsAndNotificationsAsync(
        SWARNABHOOMIContext db,
        Random rng,
        List<User> buyers,
        List<User> farmers,
        List<DemandItem> demandItems,
        List<SeededStockLot> stockLots,
        List<CultivationRecord> activeCultivations,
        DateOnly today,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // 30 PurchaseRequests: ~45% Pending (14), ~25% Accepted (7), ~20% Fulfilled (6), ~10% Rejected (3)
        var statusSchedule = new string[30];
        for (var i = 0; i < 30; i++)
        {
            statusSchedule[i] = i switch
            {
                < 14 => "Pending",
                < 21 => "Accepted",
                < 27 => "Fulfilled",
                _ => "Rejected"
            };
        }

        for (var i = 0; i < 30; i++)
        {
            var status = statusSchedule[i];
            var demandItem = demandItems[(i * 3) % demandItems.Count];
            var matchingLot = stockLots.FirstOrDefault(s => s.Stock.RiceVarietyId == demandItem.RiceVarietyId)
                ?? (stockLots.Count > 0 ? stockLots[i % stockLots.Count] : null);

            var farmerId = matchingLot?.Farmer.Id ?? farmers[i % farmers.Count].Id;
            var qtyKg = matchingLot is not null
                ? Math.Round(Math.Clamp(matchingLot.Stock.Quantity * 0.55m, 1000m, 12000m), 2)
                : rng.Next(2, 10) * 1000m;
            var offerPrice = demandItem.OfferPricePerKg ?? 125m;

            var pr = new PurchaseRequest
            {
                BuyerId = buyers[i % buyers.Count].Id,
                FarmerId = farmerId,
                DemandItemId = demandItem.Id,
                QuantityKg = qtyKg,
                OfferPricePerKg = offerPrice,
                Status = status,
                Terms = "Payment within 3 business days upon weighbridge confirmation.",
                DeliveryTerms = $"Farm-gate collection or delivery to {demandItem.Demand.District} mill",
                CreatedBy = buyers[i % buyers.Count].Id.ToString(),
                CreatedAt = nowUtc.AddDays(-(30 - i))
            };
            db.PurchaseRequests.Add(pr);

            if (status == "Pending")
            {
                db.Notifications.Add(new Notification
                {
                    UserId = farmerId,
                    Type = "PurchaseRequest",
                    MessageKey = "Incoming Purchase Order",
                    Payload = JsonSerializer.Serialize(new
                    {
                        Title = "Incoming Purchase Order",
                        Message = $"A commercial buyer has submitted a purchase request for {qtyKg:N0} kg in {demandItem.Demand.District}. Review terms and accept in the Marketplace.",
                        Category = "Purchase Request",
                        Link = "/demands",
                        ReferenceId = pr.Id.ToString()
                    }),
                    IsRead = false,
                    DeliveryStatus = "Delivered",
                    CreatedBy = "MarketplaceEngine"
                });
            }
        }

        // Ensure farmers with cultivations maturing in <= 14 days who don't yet have a harvest alert get one
        var approachingCultivations = activeCultivations
            .Where(c => c.Status != "Maturity" &&
                        c.ExpectedHarvestDate.HasValue &&
                        c.ExpectedHarvestDate.Value >= today &&
                        c.ExpectedHarvestDate.Value <= today.AddDays(14))
            .Take(10);

        foreach (var cult in approachingCultivations)
        {
            var expectedDate = cult.ExpectedHarvestDate!.Value;
            db.Notifications.Add(new Notification
            {
                UserId = cult.FarmerId,
                Type = "HarvestAlert",
                MessageKey = "Harvest Window Approaching",
                Payload = JsonSerializer.Serialize(new
                {
                    Title = "Harvest Window Approaching",
                    Message = $"Your paddy cultivation is expected to reach harvest maturity on {expectedDate:MMM dd, yyyy}.",
                    Category = "Harvest Alert",
                    Link = "/cultivation",
                    ReferenceId = cult.Id.ToString()
                }),
                IsRead = false,
                DeliveryStatus = "Delivered",
                CreatedBy = "RiceIntelligenceEngine"
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static List<DistrictAnchor> BuildWeightedDistrictList()
    {
        var list = new List<DistrictAnchor>(150);
        foreach (var anchor in DistrictAnchors)
        {
            for (var i = 0; i < anchor.FarmerWeight; i++)
            {
                list.Add(anchor);
            }
        }
        return list;
    }

    private static (string GeoJson, decimal AreaHectares) BuildRectanglePolygon(
        double centerLat,
        double centerLng,
        double targetAreaHectares)
    {
        var sideMetres = Math.Sqrt(targetAreaHectares * 10_000d);
        var halfLatDeg = (sideMetres / 2d) / 111_320d;
        var metersPerDegLon = 111_320d * Math.Cos(centerLat * Math.PI / 180d);
        var halfLngDeg = (sideMetres / 2d) / metersPerDegLon;

        var minLat = Math.Round(centerLat - halfLatDeg, 6);
        var maxLat = Math.Round(centerLat + halfLatDeg, 6);
        var minLng = Math.Round(centerLng - halfLngDeg, 6);
        var maxLng = Math.Round(centerLng + halfLngDeg, 6);

        (double Longitude, double Latitude)[] ring =
        [
            (minLng, minLat),
            (maxLng, minLat),
            (maxLng, maxLat),
            (minLng, maxLat),
            (minLng, minLat)
        ];

        var areaSqM = ComputeRingAreaSquareMetres(ring);
        var measuredAreaHa = decimal.Round((decimal)(areaSqM / 10_000d), 4);

        var geoJson = JsonSerializer.Serialize(new
        {
            type = "Polygon",
            coordinates = new[]
            {
                ring.Select(p => new[] { p.Longitude, p.Latitude }).ToArray()
            }
        });

        return (geoJson, measuredAreaHa);
    }

    private static double ComputeRingAreaSquareMetres(IReadOnlyList<(double Longitude, double Latitude)> ring)
    {
        var total = 0d;
        for (var i = 0; i < ring.Count - 1; i++)
        {
            var current = ring[i];
            var next = ring[i + 1];
            total += ToRadians(next.Longitude - current.Longitude) *
                (2d + Math.Sin(ToRadians(current.Latitude)) + Math.Sin(ToRadians(next.Latitude)));
        }
        return Math.Abs(total) * EarthRadiusMetres * EarthRadiusMetres / 2d;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;

    private static RiceVariety ResolveVariety(Dictionary<string, RiceVariety> lookup, string varietyName)
    {
        if (lookup.TryGetValue(NormalizeVarietyKey(varietyName), out var variety))
        {
            return variety;
        }
        return lookup.Values.First();
    }

    private static string NormalizeVarietyKey(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
