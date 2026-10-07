using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCensusDemographics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "census_demographics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CensusGeoid = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SurveyYear = table.Column<short>(type: "smallint", nullable: false),
                    Population = table.Column<int>(type: "int", nullable: true),
                    MedianHouseholdIncome = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    PovertyRate = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: true),
                    MedianAge = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    OwnerOccupiedPercent = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: true),
                    RenterOccupiedPercent = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: true),
                    MinorityPopulationPercent = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: true),
                    RawData = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_census_demographics", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_demographics_year",
                table: "census_demographics",
                column: "SurveyYear");

            migrationBuilder.CreateIndex(
                name: "uq_demographics_geoid_year",
                table: "census_demographics",
                columns: new[] { "CensusGeoid", "SurveyYear" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "census_demographics");
        }
    }
}
