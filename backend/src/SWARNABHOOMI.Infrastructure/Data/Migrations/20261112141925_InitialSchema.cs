using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SWARNABHOOMI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Provider = table.Column<string>(maxLength: 100, nullable: false),
                    Dataset = table.Column<string>(maxLength: 150, nullable: false),
                    LicenseTermsNote = table.Column<string>(maxLength: 1000, nullable: true),
                    LastSuccessfulImport = table.Column<DateTime>(nullable: true),
                    ImportBatch = table.Column<string>(maxLength: 100, nullable: true),
                    SourceTimestamp = table.Column<DateTime>(nullable: true),
                    ValidationResults = table.Column<string>(maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiceVarieties",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    VarietyCode = table.Column<string>(maxLength: 50, nullable: false),
                    Name = table.Column<string>(maxLength: 200, nullable: false),
                    Description = table.Column<string>(maxLength: 500, nullable: true),
                    MaturityDays = table.Column<int>(nullable: false),
                    AverageYieldKgPerHectare = table.Column<decimal>(nullable: true),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiceVarieties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Email = table.Column<string>(maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(nullable: false),
                    FirstName = table.Column<string>(maxLength: 100, nullable: false),
                    LastName = table.Column<string>(maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(maxLength: 30, nullable: false),
                    Role = table.Column<int>(nullable: false),
                    IsEmailConfirmed = table.Column<bool>(nullable: false),
                    IsPhoneConfirmed = table.Column<bool>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    IsVerified = table.Column<bool>(nullable: false),
                    LastLoginAt = table.Column<DateTime>(nullable: true),
                    District = table.Column<string>(maxLength: 120, nullable: true),
                    Province = table.Column<string>(maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeatherObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Location = table.Column<string>(nullable: false),
                    Latitude = table.Column<double>(nullable: true),
                    Longitude = table.Column<double>(nullable: true),
                    ObservationDate = table.Column<DateOnly>(nullable: false),
                    RainfallMm = table.Column<decimal>(nullable: true),
                    TemperatureC = table.Column<decimal>(nullable: true),
                    HumidityPercent = table.Column<decimal>(nullable: true),
                    Provider = table.Column<string>(nullable: true),
                    ProviderMetadata = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketPriceObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Market = table.Column<string>(maxLength: 120, nullable: false),
                    RiceVarietyId = table.Column<Guid>(nullable: false),
                    Grade = table.Column<string>(maxLength: 50, nullable: false),
                    PriceType = table.Column<string>(maxLength: 30, nullable: false),
                    Value = table.Column<decimal>(nullable: false),
                    Currency = table.Column<string>(maxLength: 20, nullable: false),
                    Unit = table.Column<string>(maxLength: 20, nullable: false),
                    ObservationDate = table.Column<DateOnly>(nullable: false),
                    PublicationDate = table.Column<DateOnly>(nullable: true),
                    Source = table.Column<string>(maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketPriceObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketPriceObservations_RiceVarieties_RiceVarietyId",
                        column: x => x.RiceVarietyId,
                        principalTable: "RiceVarieties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Location = table.Column<string>(maxLength: 200, nullable: false),
                    RiceVarietyId = table.Column<Guid>(nullable: false),
                    Grade = table.Column<string>(maxLength: 50, nullable: false),
                    CommodityStage = table.Column<string>(maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(nullable: false),
                    Unit = table.Column<string>(maxLength: 20, nullable: false),
                    AsOfDate = table.Column<DateOnly>(nullable: false),
                    VerificationStatus = table.Column<string>(maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockRecords_RiceVarieties_RiceVarietyId",
                        column: x => x.RiceVarietyId,
                        principalTable: "RiceVarieties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Actor = table.Column<string>(nullable: true),
                    Action = table.Column<string>(maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(nullable: true),
                    Timestamp = table.Column<DateTime>(nullable: false),
                    Result = table.Column<string>(maxLength: 50, nullable: true),
                    Reason = table.Column<string>(maxLength: 500, nullable: true),
                    SafeMetadata = table.Column<string>(maxLength: 2000, nullable: true),
                    UserId = table.Column<Guid>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BuyerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    BusinessName = table.Column<string>(nullable: false),
                    BusinessRegistrationNumber = table.Column<string>(nullable: true),
                    Address = table.Column<string>(nullable: true),
                    ContactPerson = table.Column<string>(nullable: true),
                    MonthlyDemandTonnes = table.Column<decimal>(nullable: true),
                    PreferredVarieties = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuyerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuyerProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FarmerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    NationalId = table.Column<string>(nullable: false),
                    Address = table.Column<string>(nullable: true),
                    TotalLandAreaHectares = table.Column<decimal>(nullable: true),
                    PrimaryVariety = table.Column<string>(nullable: true),
                    ExperienceYears = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FarmerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FarmerProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GovernmentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    Institution = table.Column<string>(nullable: false),
                    Department = table.Column<string>(nullable: true),
                    Designation = table.Column<string>(nullable: true),
                    District = table.Column<string>(nullable: true),
                    Province = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernmentProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernmentProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    Type = table.Column<string>(maxLength: 50, nullable: false),
                    MessageKey = table.Column<string>(maxLength: 200, nullable: false),
                    Payload = table.Column<string>(maxLength: 2000, nullable: true),
                    IsRead = table.Column<bool>(nullable: false),
                    ReadAt = table.Column<DateTime>(nullable: true),
                    DeliveryStatus = table.Column<string>(maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfficerAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    District = table.Column<string>(nullable: false),
                    Province = table.Column<string>(nullable: true),
                    AssignedBy = table.Column<string>(nullable: true),
                    AssignedDate = table.Column<DateTime>(nullable: true),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficerAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfficerAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Demands",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    OwnerId = table.Column<Guid>(nullable: false),
                    District = table.Column<string>(maxLength: 120, nullable: false),
                    Season = table.Column<string>(maxLength: 50, nullable: false),
                    PeriodStart = table.Column<DateOnly>(nullable: false),
                    PeriodEnd = table.Column<DateOnly>(nullable: false),
                    Status = table.Column<string>(maxLength: 30, nullable: false),
                    Notes = table.Column<string>(maxLength: 1000, nullable: true),
                    BuyerProfileId = table.Column<Guid>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Demands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Demands_BuyerProfiles_BuyerProfileId",
                        column: x => x.BuyerProfileId,
                        principalTable: "BuyerProfiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Demands_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Farms",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    Description = table.Column<string>(nullable: true),
                    AreaHectares = table.Column<decimal>(nullable: false),
                    District = table.Column<string>(nullable: true),
                    Province = table.Column<string>(nullable: true),
                    Address = table.Column<string>(nullable: true),
                    Latitude = table.Column<double>(nullable: true),
                    Longitude = table.Column<double>(nullable: true),
                    FarmerProfileId = table.Column<Guid>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Farms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Farms_FarmerProfiles_FarmerProfileId",
                        column: x => x.FarmerProfileId,
                        principalTable: "FarmerProfiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Farms_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DemandItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    DemandId = table.Column<Guid>(nullable: false),
                    RiceVarietyId = table.Column<Guid>(nullable: false),
                    Grade = table.Column<string>(maxLength: 50, nullable: false),
                    QuantityTonnes = table.Column<decimal>(nullable: false),
                    OfferPricePerKg = table.Column<decimal>(nullable: true),
                    Notes = table.Column<string>(maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemandItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemandItems_Demands_DemandId",
                        column: x => x.DemandId,
                        principalTable: "Demands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DemandItems_RiceVarieties_RiceVarietyId",
                        column: x => x.RiceVarietyId,
                        principalTable: "RiceVarieties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Fields",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    FarmId = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    AreaHectares = table.Column<decimal>(nullable: false),
                    Latitude = table.Column<double>(nullable: true),
                    Longitude = table.Column<double>(nullable: true),
                    BoundaryGeoJson = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fields_Farms_FarmId",
                        column: x => x.FarmId,
                        principalTable: "Farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    BuyerId = table.Column<Guid>(nullable: false),
                    FarmerId = table.Column<Guid>(nullable: true),
                    DemandItemId = table.Column<Guid>(nullable: false),
                    QuantityKg = table.Column<decimal>(nullable: false),
                    OfferPricePerKg = table.Column<decimal>(nullable: false),
                    Status = table.Column<string>(maxLength: 30, nullable: false),
                    Terms = table.Column<string>(maxLength: 1000, nullable: true),
                    DeliveryTerms = table.Column<string>(maxLength: 500, nullable: true),
                    BuyerProfileId = table.Column<Guid>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_BuyerProfiles_BuyerProfileId",
                        column: x => x.BuyerProfileId,
                        principalTable: "BuyerProfiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_DemandItems_DemandItemId",
                        column: x => x.DemandItemId,
                        principalTable: "DemandItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Users_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Users_FarmerId",
                        column: x => x.FarmerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CultivationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    FarmerId = table.Column<Guid>(nullable: false),
                    FieldId = table.Column<Guid>(nullable: false),
                    RiceVarietyId = table.Column<Guid>(nullable: false),
                    Season = table.Column<string>(maxLength: 50, nullable: false),
                    PlantingDate = table.Column<DateOnly>(nullable: false),
                    ExpectedHarvestDate = table.Column<DateOnly>(nullable: true),
                    AreaHectares = table.Column<decimal>(nullable: false),
                    Notes = table.Column<string>(maxLength: 1000, nullable: true),
                    Status = table.Column<string>(maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CultivationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CultivationRecords_Fields_FieldId",
                        column: x => x.FieldId,
                        principalTable: "Fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CultivationRecords_RiceVarieties_RiceVarietyId",
                        column: x => x.RiceVarietyId,
                        principalTable: "RiceVarieties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CultivationRecords_Users_FarmerId",
                        column: x => x.FarmerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HarvestForecasts",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    CultivationId = table.Column<Guid>(nullable: false),
                    RunVersion = table.Column<string>(maxLength: 50, nullable: false),
                    TargetWindowStart = table.Column<DateOnly>(nullable: false),
                    TargetWindowEnd = table.Column<DateOnly>(nullable: false),
                    PredictedPaddyQuantityKg = table.Column<decimal>(nullable: false),
                    Assumptions = table.Column<string>(maxLength: 1000, nullable: true),
                    MethodVersion = table.Column<string>(maxLength: 50, nullable: false),
                    ConfidenceCategory = table.Column<string>(maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarvestForecasts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HarvestForecasts_CultivationRecords_CultivationId",
                        column: x => x.CultivationId,
                        principalTable: "CultivationRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HarvestRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    CultivationId = table.Column<Guid>(nullable: false),
                    ActualHarvestDate = table.Column<DateOnly>(nullable: false),
                    ActualPaddyQuantityKg = table.Column<decimal>(nullable: false),
                    Quality = table.Column<string>(maxLength: 50, nullable: true),
                    LossesKg = table.Column<decimal>(nullable: true),
                    EvidenceReference = table.Column<string>(maxLength: 500, nullable: true),
                    VerificationStatus = table.Column<string>(maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UpdatedAt = table.Column<DateTime>(nullable: true),
                    CreatedBy = table.Column<string>(nullable: true),
                    UpdatedBy = table.Column<string>(nullable: true),
                    IsDeleted = table.Column<bool>(nullable: false),
                    DeletedAt = table.Column<DateTime>(nullable: true),
                    DeletedBy = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarvestRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HarvestRecords_CultivationRecords_CultivationId",
                        column: x => x.CultivationId,
                        principalTable: "CultivationRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Actor",
                table: "AuditLogs",
                column: "Actor");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType",
                table: "AuditLogs",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BuyerProfiles_UserId",
                table: "BuyerProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CultivationRecords_FarmerId",
                table: "CultivationRecords",
                column: "FarmerId");

            migrationBuilder.CreateIndex(
                name: "IX_CultivationRecords_FieldId",
                table: "CultivationRecords",
                column: "FieldId");

            migrationBuilder.CreateIndex(
                name: "IX_CultivationRecords_RiceVarietyId",
                table: "CultivationRecords",
                column: "RiceVarietyId");

            migrationBuilder.CreateIndex(
                name: "IX_CultivationRecords_Season",
                table: "CultivationRecords",
                column: "Season");

            migrationBuilder.CreateIndex(
                name: "IX_DataSources_Dataset",
                table: "DataSources",
                column: "Dataset");

            migrationBuilder.CreateIndex(
                name: "IX_DataSources_Provider",
                table: "DataSources",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_DemandItems_DemandId",
                table: "DemandItems",
                column: "DemandId");

            migrationBuilder.CreateIndex(
                name: "IX_DemandItems_RiceVarietyId",
                table: "DemandItems",
                column: "RiceVarietyId");

            migrationBuilder.CreateIndex(
                name: "IX_Demands_BuyerProfileId",
                table: "Demands",
                column: "BuyerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Demands_District",
                table: "Demands",
                column: "District");

            migrationBuilder.CreateIndex(
                name: "IX_Demands_OwnerId",
                table: "Demands",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Demands_Status",
                table: "Demands",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FarmerProfiles_NationalId",
                table: "FarmerProfiles",
                column: "NationalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FarmerProfiles_UserId",
                table: "FarmerProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Farms_District",
                table: "Farms",
                column: "District");

            migrationBuilder.CreateIndex(
                name: "IX_Farms_FarmerProfileId",
                table: "Farms",
                column: "FarmerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Farms_UserId",
                table: "Farms",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Fields_FarmId",
                table: "Fields",
                column: "FarmId");

            migrationBuilder.CreateIndex(
                name: "IX_GovernmentProfiles_UserId",
                table: "GovernmentProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HarvestForecasts_ConfidenceCategory",
                table: "HarvestForecasts",
                column: "ConfidenceCategory");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestForecasts_CultivationId",
                table: "HarvestForecasts",
                column: "CultivationId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestRecords_CultivationId",
                table: "HarvestRecords",
                column: "CultivationId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestRecords_VerificationStatus",
                table: "HarvestRecords",
                column: "VerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceObservations_Market",
                table: "MarketPriceObservations",
                column: "Market");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceObservations_ObservationDate",
                table: "MarketPriceObservations",
                column: "ObservationDate");

            migrationBuilder.CreateIndex(
                name: "IX_MarketPriceObservations_RiceVarietyId",
                table: "MarketPriceObservations",
                column: "RiceVarietyId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IsRead",
                table: "Notifications",
                column: "IsRead");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type",
                table: "Notifications",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId",
                table: "Notifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OfficerAssignments_District",
                table: "OfficerAssignments",
                column: "District");

            migrationBuilder.CreateIndex(
                name: "IX_OfficerAssignments_UserId",
                table: "OfficerAssignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_BuyerId",
                table: "PurchaseRequests",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_BuyerProfileId",
                table: "PurchaseRequests",
                column: "BuyerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_DemandItemId",
                table: "PurchaseRequests",
                column: "DemandItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_FarmerId",
                table: "PurchaseRequests",
                column: "FarmerId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_Status",
                table: "PurchaseRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RiceVarieties_IsActive",
                table: "RiceVarieties",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_RiceVarieties_VarietyCode",
                table: "RiceVarieties",
                column: "VarietyCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockRecords_AsOfDate",
                table: "StockRecords",
                column: "AsOfDate");

            migrationBuilder.CreateIndex(
                name: "IX_StockRecords_Location",
                table: "StockRecords",
                column: "Location");

            migrationBuilder.CreateIndex(
                name: "IX_StockRecords_RiceVarietyId",
                table: "StockRecords",
                column: "RiceVarietyId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_District",
                table: "Users",
                column: "District");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsActive",
                table: "Users",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role",
                table: "Users",
                column: "Role");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "DataSources");

            migrationBuilder.DropTable(
                name: "GovernmentProfiles");

            migrationBuilder.DropTable(
                name: "HarvestForecasts");

            migrationBuilder.DropTable(
                name: "HarvestRecords");

            migrationBuilder.DropTable(
                name: "MarketPriceObservations");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OfficerAssignments");

            migrationBuilder.DropTable(
                name: "PurchaseRequests");

            migrationBuilder.DropTable(
                name: "StockRecords");

            migrationBuilder.DropTable(
                name: "WeatherObservations");

            migrationBuilder.DropTable(
                name: "CultivationRecords");

            migrationBuilder.DropTable(
                name: "DemandItems");

            migrationBuilder.DropTable(
                name: "Fields");

            migrationBuilder.DropTable(
                name: "Demands");

            migrationBuilder.DropTable(
                name: "RiceVarieties");

            migrationBuilder.DropTable(
                name: "Farms");

            migrationBuilder.DropTable(
                name: "BuyerProfiles");

            migrationBuilder.DropTable(
                name: "FarmerProfiles");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
