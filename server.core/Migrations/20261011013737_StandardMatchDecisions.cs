using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class StandardMatchDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StandardMatchDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StandardTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UcJobCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsMatch = table.Column<bool>(type: "bit", nullable: false),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardMatchDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardMatchDecisions_AppUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StandardMatchDecisions_DecidedByUserId",
                table: "StandardMatchDecisions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StandardMatchDecisions_StandardTitle_UcJobCode",
                table: "StandardMatchDecisions",
                columns: new[] { "StandardTitle", "UcJobCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StandardMatchDecisions");
        }
    }
}
