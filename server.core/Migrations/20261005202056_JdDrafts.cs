using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class JdDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssembledAt",
                table: "AuthoredJds",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftState",
                table: "AuthoredJds",
                type: "nvarchar(max)",
                nullable: true);

            // Every JD saved before drafts existed was saved by assembling it.
            migrationBuilder.Sql("UPDATE AuthoredJds SET AssembledAt = UpdatedAt WHERE AssembledAt IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssembledAt",
                table: "AuthoredJds");

            migrationBuilder.DropColumn(
                name: "DraftState",
                table: "AuthoredJds");
        }
    }
}
