using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class CorpusUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorpusUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UcJobCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OriginalUcJobCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    UcJobTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IngestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorpusUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CorpusUploads_AppUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorpusUploads_Sha256",
                table: "CorpusUploads",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorpusUploads_Status_UcJobCode",
                table: "CorpusUploads",
                columns: new[] { "Status", "UcJobCode" });

            migrationBuilder.CreateIndex(
                name: "IX_CorpusUploads_UploadedByUserId",
                table: "CorpusUploads",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorpusUploads");
        }
    }
}
