using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class SalaryRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SalaryRanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Scale = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Grade = table.Column<int>(type: "int", nullable: false),
                    PayType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Minimum = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    FirstQuartile = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    Midpoint = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    ThirdQuartile = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    Maximum = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryRanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryRanges_Scale_Grade_PayType",
                table: "SalaryRanges",
                columns: new[] { "Scale", "Grade", "PayType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalaryRanges");
        }
    }
}
