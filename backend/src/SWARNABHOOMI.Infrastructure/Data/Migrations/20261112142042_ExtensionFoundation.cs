using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SWARNABHOOMI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExtensionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiAssistantConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    BaseUrl = table.Column<string>(maxLength: 500, nullable: false),
                    Model = table.Column<string>(maxLength: 200, nullable: false),
                    EncryptedApiKey = table.Column<string>(maxLength: 4000, nullable: false),
                    IsEnabled = table.Column<bool>(nullable: false),
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
                    table.PrimaryKey("PK_AiAssistantConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FieldVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    FieldId = table.Column<Guid>(nullable: false),
                    Status = table.Column<string>(maxLength: 30, nullable: false),
                    ReviewerId = table.Column<string>(maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTime>(nullable: true),
                    ReviewNotes = table.Column<string>(maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_FieldVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldVerifications_Fields_FieldId",
                        column: x => x.FieldId,
                        principalTable: "Fields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceRuleVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Category = table.Column<string>(maxLength: 40, nullable: false),
                    ApplicabilityJson = table.Column<string>(maxLength: 4000, nullable: false),
                    MinimumValue = table.Column<decimal>(nullable: true),
                    MaximumValue = table.Column<decimal>(nullable: true),
                    Unit = table.Column<string>(maxLength: 40, nullable: false),
                    SourceReference = table.Column<string>(maxLength: 500, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(nullable: false),
                    EffectiveTo = table.Column<DateOnly>(nullable: true),
                    Status = table.Column<string>(maxLength: 30, nullable: false),
                    Version = table.Column<int>(nullable: false),
                    ReviewedBy = table.Column<string>(maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTime>(nullable: true),
                    ChangeReason = table.Column<string>(maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_ResourceRuleVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(maxLength: 100, nullable: false),
                    Year = table.Column<int>(nullable: false),
                    District = table.Column<string>(maxLength: 120, nullable: true),
                    AgrarianServiceCentre = table.Column<string>(maxLength: 120, nullable: true),
                    PlantingStart = table.Column<DateOnly>(nullable: false),
                    PlantingEnd = table.Column<DateOnly>(nullable: false),
                    WaterScheduleReference = table.Column<string>(maxLength: 500, nullable: true),
                    ApprovedVarietyClassesJson = table.Column<string>(maxLength: 4000, nullable: false),
                    Status = table.Column<string>(maxLength: 30, nullable: false),
                    ApprovedBy = table.Column<string>(maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTime>(nullable: true),
                    ClosedAt = table.Column<DateTime>(nullable: true),
                    ClosureReason = table.Column<string>(maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_Seasons", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAssistantConfigurations_CreatedAt",
                table: "AiAssistantConfigurations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FieldVerifications_FieldId_CreatedAt",
                table: "FieldVerifications",
                columns: new[] { "FieldId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceRuleVersions_Category_Status_EffectiveFrom_EffectiveTo",
                table: "ResourceRuleVersions",
                columns: new[] { "Category", "Status", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_Year_District_AgrarianServiceCentre_Status",
                table: "Seasons",
                columns: new[] { "Year", "District", "AgrarianServiceCentre", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAssistantConfigurations");

            migrationBuilder.DropTable(
                name: "FieldVerifications");

            migrationBuilder.DropTable(
                name: "ResourceRuleVersions");

            migrationBuilder.DropTable(
                name: "Seasons");
        }
    }
}
