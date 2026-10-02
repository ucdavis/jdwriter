using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class CorpusOriginsAndContribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AddedAt",
                table: "JobDescriptions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuthoredJdId",
                table: "JobDescriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "JobDescriptions",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Export");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastIngestedAt",
                table: "ClassProfiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorpusNote",
                table: "AuthoredJds",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EnvelopeVerdict",
                table: "AuthoredJds",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InCorpus",
                table: "AuthoredJds",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_JobDescriptions_AuthoredJdId",
                table: "JobDescriptions",
                column: "AuthoredJdId");

            migrationBuilder.CreateIndex(
                name: "IX_JobDescriptions_UcJobCode_AddedAt",
                table: "JobDescriptions",
                columns: new[] { "UcJobCode", "AddedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_JobDescriptions_AuthoredJds_AuthoredJdId",
                table: "JobDescriptions",
                column: "AuthoredJdId",
                principalTable: "AuthoredJds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobDescriptions_AuthoredJds_AuthoredJdId",
                table: "JobDescriptions");

            migrationBuilder.DropIndex(
                name: "IX_JobDescriptions_AuthoredJdId",
                table: "JobDescriptions");

            migrationBuilder.DropIndex(
                name: "IX_JobDescriptions_UcJobCode_AddedAt",
                table: "JobDescriptions");

            migrationBuilder.DropColumn(
                name: "AddedAt",
                table: "JobDescriptions");

            migrationBuilder.DropColumn(
                name: "AuthoredJdId",
                table: "JobDescriptions");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "JobDescriptions");

            migrationBuilder.DropColumn(
                name: "LastIngestedAt",
                table: "ClassProfiles");

            migrationBuilder.DropColumn(
                name: "CorpusNote",
                table: "AuthoredJds");

            migrationBuilder.DropColumn(
                name: "EnvelopeVerdict",
                table: "AuthoredJds");

            migrationBuilder.DropColumn(
                name: "InCorpus",
                table: "AuthoredJds");
        }
    }
}
