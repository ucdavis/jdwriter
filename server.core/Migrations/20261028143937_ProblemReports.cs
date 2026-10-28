using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class ProblemReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProblemReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Page = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Browser = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Resolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemReports_AppUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_CreatedByUserId",
                table: "ProblemReports",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_Resolved_CreatedAt",
                table: "ProblemReports",
                columns: new[] { "Resolved", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProblemReports");
        }
    }
}
