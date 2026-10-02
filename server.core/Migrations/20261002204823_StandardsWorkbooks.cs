using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class StandardsWorkbooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StandardsWorkbooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    StandardsFound = table.Column<int>(type: "int", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StandardsWorkbooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StandardsWorkbooks_AppUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StandardsWorkbooks_Sha256",
                table: "StandardsWorkbooks",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StandardsWorkbooks_UploadedByUserId",
                table: "StandardsWorkbooks",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StandardsWorkbooks");
        }
    }
}
