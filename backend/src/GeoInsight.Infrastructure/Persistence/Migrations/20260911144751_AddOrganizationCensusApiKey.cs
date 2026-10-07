using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoInsight.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationCensusApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CensusApiKeyEncrypted",
                table: "organizations",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CensusApiKeyEncrypted",
                table: "organizations");
        }
    }
}
