using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.core.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NameIdentifier = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IamId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClassProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UcJobCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CtJobFamily = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CtJobFunction = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PersonnelProgram = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CorpusSize = table.Column<int>(type: "int", nullable: false),
                    RepresentativeSummary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EnvelopeSource = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    GeneratedNote = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComplianceRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Replacement = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobDescriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceFile = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    BusinessUnit = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Division = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DepartmentName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DepartmentCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    JdNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    UcPathPositionNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    UcJobTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UcJobCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OriginalUcJobCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    WorkingTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CtJobFamily = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CtJobFunction = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PersonnelProgram = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SalaryGrade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FlsaStatus = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    UnionCode = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Supervises = table.Column<bool>(type: "bit", nullable: true),
                    Leads = table.Column<bool>(type: "bit", nullable: true),
                    ReportsToPositionNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    WorksOutdoorsOver50pct = table.Column<bool>(type: "bit", nullable: true),
                    JobSummary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PemPopulated = table.Column<bool>(type: "bit", nullable: false),
                    DriversLicenseRequired = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobDescriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobStandards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LongTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PersProg = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Flsa = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Union = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    GenericScope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomScope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TitleKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    TitleCodeKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobStandards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Supersessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ToCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ToTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Supersessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TitleCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TitleKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    TitleCodeKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Function = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Family = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppUserRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppUserId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "AuthoredJds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassProfileId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    WorkingTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Department = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UcJobCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SalaryGrade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FlsaStatus = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    BargainingUnit = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    JobSummary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthoredJds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthoredJds_AppUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AuthoredJds_ClassProfiles_ClassProfileId",
                        column: x => x.ClassProfileId,
                        principalTable: "ClassProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConsolidatedFunctions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassProfileId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    MeanPct = table.Column<double>(type: "float", nullable: false),
                    TemplatePct = table.Column<double>(type: "float", nullable: false),
                    MinPct = table.Column<double>(type: "float", nullable: false),
                    MaxPct = table.Column<double>(type: "float", nullable: false),
                    Prevalence = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidatedFunctions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidatedFunctions_ClassProfiles_ClassProfileId",
                        column: x => x.ClassProfileId,
                        principalTable: "ClassProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConsolidatedQuals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassProfileId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Freq = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidatedQuals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidatedQuals_ClassProfiles_ClassProfileId",
                        column: x => x.ClassProfileId,
                        principalTable: "ClassProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoverageReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassProfileId = table.Column<int>(type: "int", nullable: false),
                    N = table.Column<int>(type: "int", nullable: false),
                    MeanCoverage = table.Column<double>(type: "float", nullable: false),
                    WellCoveredPct = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoverageReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoverageReports_ClassProfiles_ClassProfileId",
                        column: x => x.ClassProfileId,
                        principalTable: "ClassProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobEnvelopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassProfileId = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeStatement = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobEnvelopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobEnvelopes_ClassProfiles_ClassProfileId",
                        column: x => x.ClassProfileId,
                        principalTable: "ClassProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileDistributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassProfileId = table.Column<int>(type: "int", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Consensus = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Agreement = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileDistributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileDistributions_ClassProfiles_ClassProfileId",
                        column: x => x.ClassProfileId,
                        principalTable: "ClassProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileDroppedItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassProfileId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileDroppedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileDroppedItems_ClassProfiles_ClassProfileId",
                        column: x => x.ClassProfileId,
                        principalTable: "ClassProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileFunctions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassProfileId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PctMin = table.Column<double>(type: "float", nullable: false),
                    PctMax = table.Column<double>(type: "float", nullable: false),
                    PctMean = table.Column<double>(type: "float", nullable: false),
                    PctN = table.Column<int>(type: "int", nullable: false),
                    Prevalence = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileFunctions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileFunctions_ClassProfiles_ClassProfileId",
                        column: x => x.ClassProfileId,
                        principalTable: "ClassProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileQualItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassProfileId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Freq = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileQualItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileQualItems_ClassProfiles_ClassProfileId",
                        column: x => x.ClassProfileId,
                        principalTable: "ClassProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileSourceFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClassProfileId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    SourceFile = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileSourceFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileSourceFiles_ClassProfiles_ClassProfileId",
                        column: x => x.ClassProfileId,
                        principalTable: "ClassProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JdPemEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobDescriptionId = table.Column<int>(type: "int", nullable: false),
                    Axis = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RowName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Band = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JdPemEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JdPemEntries_JobDescriptions_JobDescriptionId",
                        column: x => x.JobDescriptionId,
                        principalTable: "JobDescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JdQualificationItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobDescriptionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JdQualificationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JdQualificationItems_JobDescriptions_JobDescriptionId",
                        column: x => x.JobDescriptionId,
                        principalTable: "JobDescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JdResponsibilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobDescriptionId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Pct = table.Column<int>(type: "int", nullable: true),
                    FunctionName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JdResponsibilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JdResponsibilities_JobDescriptions_JobDescriptionId",
                        column: x => x.JobDescriptionId,
                        principalTable: "JobDescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobStandardItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobStandardId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobStandardItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobStandardItems_JobStandards_JobStandardId",
                        column: x => x.JobStandardId,
                        principalTable: "JobStandards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthoredJdListItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthoredJdId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthoredJdListItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthoredJdListItems_AuthoredJds_AuthoredJdId",
                        column: x => x.AuthoredJdId,
                        principalTable: "AuthoredJds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthoredJdResponsibilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthoredJdId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    FunctionName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PctTime = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthoredJdResponsibilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthoredJdResponsibilities_AuthoredJds_AuthoredJdId",
                        column: x => x.AuthoredJdId,
                        principalTable: "AuthoredJds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComplianceEdits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthoredJdId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Before = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    After = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceEdits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplianceEdits_AuthoredJds_AuthoredJdId",
                        column: x => x.AuthoredJdId,
                        principalTable: "AuthoredJds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConsolidatedFunctionMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConsolidatedFunctionId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidatedFunctionMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidatedFunctionMembers_ConsolidatedFunctions_ConsolidatedFunctionId",
                        column: x => x.ConsolidatedFunctionId,
                        principalTable: "ConsolidatedFunctions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConsolidatedFunctionSampleDuties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConsolidatedFunctionId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidatedFunctionSampleDuties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidatedFunctionSampleDuties_ConsolidatedFunctions_ConsolidatedFunctionId",
                        column: x => x.ConsolidatedFunctionId,
                        principalTable: "ConsolidatedFunctions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConsolidatedQualMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConsolidatedQualId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsolidatedQualMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsolidatedQualMembers_ConsolidatedQuals_ConsolidatedQualId",
                        column: x => x.ConsolidatedQualId,
                        principalTable: "ConsolidatedQuals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JdCoverages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CoverageReportId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    SourceFile = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CoveredPct = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JdCoverages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JdCoverages_CoverageReports_CoverageReportId",
                        column: x => x.CoverageReportId,
                        principalTable: "CoverageReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnvelopeListItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobEnvelopeId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvelopeListItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnvelopeListItems_JobEnvelopes_JobEnvelopeId",
                        column: x => x.JobEnvelopeId,
                        principalTable: "JobEnvelopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnvelopeResponsibilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobEnvelopeId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    FunctionName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PctTime = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvelopeResponsibilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnvelopeResponsibilities_JobEnvelopes_JobEnvelopeId",
                        column: x => x.JobEnvelopeId,
                        principalTable: "JobEnvelopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileDistributionValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileDistributionId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileDistributionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileDistributionValues_ProfileDistributions_ProfileDistributionId",
                        column: x => x.ProfileDistributionId,
                        principalTable: "ProfileDistributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileFunctionSampleDuties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileFunctionId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileFunctionSampleDuties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileFunctionSampleDuties_ProfileFunctions_ProfileFunctionId",
                        column: x => x.ProfileFunctionId,
                        principalTable: "ProfileFunctions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JdDuties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JdResponsibilityId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JdDuties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JdDuties_JdResponsibilities_JdResponsibilityId",
                        column: x => x.JdResponsibilityId,
                        principalTable: "JdResponsibilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthoredJdDuties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthoredJdResponsibilityId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthoredJdDuties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthoredJdDuties_AuthoredJdResponsibilities_AuthoredJdResponsibilityId",
                        column: x => x.AuthoredJdResponsibilityId,
                        principalTable: "AuthoredJdResponsibilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JdCoverageUncovereds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JdCoverageId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Pct = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JdCoverageUncovereds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JdCoverageUncovereds_JdCoverages_JdCoverageId",
                        column: x => x.JdCoverageId,
                        principalTable: "JdCoverages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnvelopeDuties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EnvelopeResponsibilityId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvelopeDuties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnvelopeDuties_EnvelopeResponsibilities_EnvelopeResponsibilityId",
                        column: x => x.EnvelopeResponsibilityId,
                        principalTable: "EnvelopeResponsibilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUserRoles_AppUserId_Role",
                table: "AppUserRoles",
                columns: new[] { "AppUserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_NameIdentifier",
                table: "AppUsers",
                column: "NameIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthoredJdDuties_AuthoredJdResponsibilityId_Ordinal",
                table: "AuthoredJdDuties",
                columns: new[] { "AuthoredJdResponsibilityId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthoredJdListItems_AuthoredJdId_Ordinal",
                table: "AuthoredJdListItems",
                columns: new[] { "AuthoredJdId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthoredJdResponsibilities_AuthoredJdId",
                table: "AuthoredJdResponsibilities",
                column: "AuthoredJdId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthoredJds_ClassProfileId",
                table: "AuthoredJds",
                column: "ClassProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthoredJds_CreatedByUserId",
                table: "AuthoredJds",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassProfiles_Slug",
                table: "ClassProfiles",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassProfiles_UcJobCode",
                table: "ClassProfiles",
                column: "UcJobCode");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceEdits_AuthoredJdId",
                table: "ComplianceEdits",
                column: "AuthoredJdId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceRules_Key",
                table: "ComplianceRules",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidatedFunctionMembers_ConsolidatedFunctionId_Ordinal",
                table: "ConsolidatedFunctionMembers",
                columns: new[] { "ConsolidatedFunctionId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidatedFunctions_ClassProfileId",
                table: "ConsolidatedFunctions",
                column: "ClassProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidatedFunctionSampleDuties_ConsolidatedFunctionId_Ordinal",
                table: "ConsolidatedFunctionSampleDuties",
                columns: new[] { "ConsolidatedFunctionId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidatedQualMembers_ConsolidatedQualId_Ordinal",
                table: "ConsolidatedQualMembers",
                columns: new[] { "ConsolidatedQualId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsolidatedQuals_ClassProfileId",
                table: "ConsolidatedQuals",
                column: "ClassProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_CoverageReports_ClassProfileId",
                table: "CoverageReports",
                column: "ClassProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnvelopeDuties_EnvelopeResponsibilityId_Ordinal",
                table: "EnvelopeDuties",
                columns: new[] { "EnvelopeResponsibilityId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_EnvelopeListItems_JobEnvelopeId_Ordinal",
                table: "EnvelopeListItems",
                columns: new[] { "JobEnvelopeId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_EnvelopeResponsibilities_JobEnvelopeId",
                table: "EnvelopeResponsibilities",
                column: "JobEnvelopeId");

            migrationBuilder.CreateIndex(
                name: "IX_JdCoverages_CoverageReportId",
                table: "JdCoverages",
                column: "CoverageReportId");

            migrationBuilder.CreateIndex(
                name: "IX_JdCoverageUncovereds_JdCoverageId",
                table: "JdCoverageUncovereds",
                column: "JdCoverageId");

            migrationBuilder.CreateIndex(
                name: "IX_JdDuties_JdResponsibilityId_Ordinal",
                table: "JdDuties",
                columns: new[] { "JdResponsibilityId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_JdPemEntries_JobDescriptionId",
                table: "JdPemEntries",
                column: "JobDescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_JdQualificationItems_JobDescriptionId_Ordinal",
                table: "JdQualificationItems",
                columns: new[] { "JobDescriptionId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_JdResponsibilities_JobDescriptionId_Ordinal",
                table: "JdResponsibilities",
                columns: new[] { "JobDescriptionId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_JobDescriptions_SourceFile",
                table: "JobDescriptions",
                column: "SourceFile",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobDescriptions_UcJobCode",
                table: "JobDescriptions",
                column: "UcJobCode");

            migrationBuilder.CreateIndex(
                name: "IX_JobEnvelopes_ClassProfileId",
                table: "JobEnvelopes",
                column: "ClassProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobStandardItems_JobStandardId_Ordinal",
                table: "JobStandardItems",
                columns: new[] { "JobStandardId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_JobStandards_Code",
                table: "JobStandards",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_JobStandards_TitleCodeKey",
                table: "JobStandards",
                column: "TitleCodeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobStandards_TitleKey",
                table: "JobStandards",
                column: "TitleKey");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileDistributions_ClassProfileId_Field",
                table: "ProfileDistributions",
                columns: new[] { "ClassProfileId", "Field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileDistributionValues_ProfileDistributionId",
                table: "ProfileDistributionValues",
                column: "ProfileDistributionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileDroppedItems_ClassProfileId_Ordinal",
                table: "ProfileDroppedItems",
                columns: new[] { "ClassProfileId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileFunctions_ClassProfileId",
                table: "ProfileFunctions",
                column: "ClassProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileFunctionSampleDuties_ProfileFunctionId_Ordinal",
                table: "ProfileFunctionSampleDuties",
                columns: new[] { "ProfileFunctionId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileQualItems_ClassProfileId_Ordinal",
                table: "ProfileQualItems",
                columns: new[] { "ClassProfileId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileSourceFiles_ClassProfileId",
                table: "ProfileSourceFiles",
                column: "ClassProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Supersessions_FromCode",
                table: "Supersessions",
                column: "FromCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TitleCodes_Code",
                table: "TitleCodes",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_TitleCodes_TitleCodeKey",
                table: "TitleCodes",
                column: "TitleCodeKey");

            migrationBuilder.CreateIndex(
                name: "IX_TitleCodes_TitleKey",
                table: "TitleCodes",
                column: "TitleKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUserRoles");

            migrationBuilder.DropTable(
                name: "AuthoredJdDuties");

            migrationBuilder.DropTable(
                name: "AuthoredJdListItems");

            migrationBuilder.DropTable(
                name: "ComplianceEdits");

            migrationBuilder.DropTable(
                name: "ComplianceRules");

            migrationBuilder.DropTable(
                name: "ConsolidatedFunctionMembers");

            migrationBuilder.DropTable(
                name: "ConsolidatedFunctionSampleDuties");

            migrationBuilder.DropTable(
                name: "ConsolidatedQualMembers");

            migrationBuilder.DropTable(
                name: "EnvelopeDuties");

            migrationBuilder.DropTable(
                name: "EnvelopeListItems");

            migrationBuilder.DropTable(
                name: "JdCoverageUncovereds");

            migrationBuilder.DropTable(
                name: "JdDuties");

            migrationBuilder.DropTable(
                name: "JdPemEntries");

            migrationBuilder.DropTable(
                name: "JdQualificationItems");

            migrationBuilder.DropTable(
                name: "JobStandardItems");

            migrationBuilder.DropTable(
                name: "ProfileDistributionValues");

            migrationBuilder.DropTable(
                name: "ProfileDroppedItems");

            migrationBuilder.DropTable(
                name: "ProfileFunctionSampleDuties");

            migrationBuilder.DropTable(
                name: "ProfileQualItems");

            migrationBuilder.DropTable(
                name: "ProfileSourceFiles");

            migrationBuilder.DropTable(
                name: "Supersessions");

            migrationBuilder.DropTable(
                name: "TitleCodes");

            migrationBuilder.DropTable(
                name: "AuthoredJdResponsibilities");

            migrationBuilder.DropTable(
                name: "ConsolidatedFunctions");

            migrationBuilder.DropTable(
                name: "ConsolidatedQuals");

            migrationBuilder.DropTable(
                name: "EnvelopeResponsibilities");

            migrationBuilder.DropTable(
                name: "JdCoverages");

            migrationBuilder.DropTable(
                name: "JdResponsibilities");

            migrationBuilder.DropTable(
                name: "JobStandards");

            migrationBuilder.DropTable(
                name: "ProfileDistributions");

            migrationBuilder.DropTable(
                name: "ProfileFunctions");

            migrationBuilder.DropTable(
                name: "AuthoredJds");

            migrationBuilder.DropTable(
                name: "JobEnvelopes");

            migrationBuilder.DropTable(
                name: "CoverageReports");

            migrationBuilder.DropTable(
                name: "JobDescriptions");

            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "ClassProfiles");
        }
    }
}
