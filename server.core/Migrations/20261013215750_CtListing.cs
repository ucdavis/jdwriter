using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class CtListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CtTitles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UcJobCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    JobFamily = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    JobFunction = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LevelCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    LastUpdated = table.Column<DateOnly>(type: "date", nullable: true),
                    OpOnly = table.Column<bool>(type: "bit", nullable: false),
                    Flsa = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PersonnelProgram = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    JobStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CtTitles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CtTitles_UcJobCode",
                table: "CtTitles",
                column: "UcJobCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CtTitles");
        }
    }
}
