using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class SavedJdStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnvelopeSource",
                table: "AuthoredJds",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "AuthoredJds",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "AuthoredJds",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<int>(
                name: "UnallocatedPct",
                table: "AuthoredJds",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnvelopeSource",
                table: "AuthoredJds");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "AuthoredJds");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AuthoredJds");

            migrationBuilder.DropColumn(
                name: "UnallocatedPct",
                table: "AuthoredJds");
        }
    }
}
