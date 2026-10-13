using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class JdFitReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JdFitReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceFile = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UcJobCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JdFitReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JdFitReviews_AppUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JdFitReviews_ReviewedByUserId",
                table: "JdFitReviews",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_JdFitReviews_SourceFile",
                table: "JdFitReviews",
                column: "SourceFile",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JdFitReviews");
        }
    }
}
