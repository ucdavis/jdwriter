using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class StableCorpusNamesAndChangeStamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "JobStandards",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "ComplianceRules",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "ClassProfiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CorpusKey",
                table: "AuthoredJds",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            // JDs added in the app were named by local row ids ("import/document-12"), which mean a
            // different JD in each environment. Rename them to names that travel — the hash of the
            // original's words, or the draft's permanent key — everywhere a name is recorded: the
            // corpus, each class's list of source files, and its coverage rows.
            migrationBuilder.Sql("""
                CREATE TABLE #CorpusNames (OldName nvarchar(450) NOT NULL PRIMARY KEY, NewName nvarchar(450) NOT NULL);

                INSERT INTO #CorpusNames (OldName, NewName)
                SELECT 'import/document-' + CAST(u.Id AS nvarchar(20)), 'import/' + LEFT(u.Sha256, 16)
                FROM CorpusUploads u
                WHERE EXISTS (SELECT 1 FROM JobDescriptions j WHERE j.SourceFile = 'import/document-' + CAST(u.Id AS nvarchar(20)));

                INSERT INTO #CorpusNames (OldName, NewName)
                SELECT 'classify/submission-' + CAST(u.Id AS nvarchar(20)), 'classify/' + LEFT(u.Sha256, 16)
                FROM CorpusUploads u
                WHERE EXISTS (SELECT 1 FROM JobDescriptions j WHERE j.SourceFile = 'classify/submission-' + CAST(u.Id AS nvarchar(20)));

                INSERT INTO #CorpusNames (OldName, NewName)
                SELECT 'authored/JD-' + CAST(a.Id AS nvarchar(20)), 'authored/' + LOWER(REPLACE(CONVERT(nvarchar(36), a.CorpusKey), '-', ''))
                FROM AuthoredJds a
                WHERE EXISTS (SELECT 1 FROM JobDescriptions j WHERE j.SourceFile = 'authored/JD-' + CAST(a.Id AS nvarchar(20)));

                UPDATE j SET SourceFile = n.NewName FROM JobDescriptions j JOIN #CorpusNames n ON j.SourceFile = n.OldName;
                UPDATE f SET SourceFile = n.NewName FROM ProfileSourceFiles f JOIN #CorpusNames n ON f.SourceFile = n.OldName;
                UPDATE c SET SourceFile = n.NewName FROM JdCoverages c JOIN #CorpusNames n ON c.SourceFile = n.OldName;

                DROP TABLE #CorpusNames;

                UPDATE ClassProfiles SET UpdatedAt = LastIngestedAt WHERE UpdatedAt IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "JobStandards");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ComplianceRules");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ClassProfiles");

            migrationBuilder.DropColumn(
                name: "CorpusKey",
                table: "AuthoredJds");
        }
    }
}
