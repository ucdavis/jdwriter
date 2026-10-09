using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class AuthoredJdSupervision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Leads",
                table: "AuthoredJds",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Supervises",
                table: "AuthoredJds",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupervisesCount",
                table: "AuthoredJds",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Leads",
                table: "AuthoredJds");

            migrationBuilder.DropColumn(
                name: "Supervises",
                table: "AuthoredJds");

            migrationBuilder.DropColumn(
                name: "SupervisesCount",
                table: "AuthoredJds");
        }
    }
}
