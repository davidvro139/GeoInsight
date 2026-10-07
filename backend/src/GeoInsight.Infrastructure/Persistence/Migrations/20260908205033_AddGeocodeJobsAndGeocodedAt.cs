using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGeocodeJobsAndGeocodedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GeocodedAt",
                table: "dataset_records",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "geocode_jobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DatasetId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalItems = table.Column<int>(type: "int", nullable: false),
                    CompletedItems = table.Column<int>(type: "int", nullable: false),
                    SuccessfulItems = table.Column<int>(type: "int", nullable: false),
                    FailedItems = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HangfireJobId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_geocode_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_geocode_jobs_datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "idx_geocode_job_dataset",
                table: "geocode_jobs",
                column: "DatasetId");

            migrationBuilder.CreateIndex(
                name: "idx_geocode_job_status",
                table: "geocode_jobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "geocode_jobs");

            migrationBuilder.DropColumn(
                name: "GeocodedAt",
                table: "dataset_records");
        }
    }
}
