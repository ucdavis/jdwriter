using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class TwoRolesAndAdminWhitelist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUserRoles");

            migrationBuilder.AddColumn<string>(
                name: "LoginId",
                table: "AppUsers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LoginId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GrantedByUserId = table.Column<int>(type: "int", nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminGrants_AppUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_LoginId",
                table: "AppUsers",
                column: "LoginId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminGrants_GrantedByUserId",
                table: "AdminGrants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminGrants_LoginId",
                table: "AdminGrants",
                column: "LoginId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminGrants");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_LoginId",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "LoginId",
                table: "AppUsers");

            migrationBuilder.CreateTable(
                name: "AppUserRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppUserId = table.Column<int>(type: "int", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppUserRoles_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUserRoles_AppUserId_Role",
                table: "AppUserRoles",
                columns: new[] { "AppUserId", "Role" },
                unique: true);
        }
    }
}
