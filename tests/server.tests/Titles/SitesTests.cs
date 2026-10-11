using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Titles;

/// <summary>
/// Site-specific classes — Health Center (HC) and Student Health Center (SHS) — are their own
/// classes: never the same class as the title without the marker, never built from the regular
/// class's standard, and named as each other's twin when both exist. Real UC payroll titles.
/// </summary>
public class SitesTests
{
    private static TitleCode Tc(string code, string title, string source = "both") => new()
    {
        Code = code,
        Title = title,
        Source = source,
        TitleKey = TitleNormalizer.TitleKey(title),
        TitleCodeKey = TitleNormalizer.TitleCodeKey(title),
    };

    private static readonly TitleCodeIndex Index = new([
        Tc("000686", "ACCOUNTING MGR 2"),
        Tc("004845", "ACCOUNTING MGR 2 HC"),
        Tc("000685", "ACCOUNTING MGR 1", "matrix"),
        Tc("004844", "ACCOUNTING MGR 1 HC"),
        Tc("006536", "SOCIAL WORK HC SUPV 2"),
        Tc("006569", "CLIN LAB SUPV 2"),
        Tc("009367", "CLIN LAB SHS SUPV 2"),
        Tc("005680", "HC ADM SHS MGR 1"),
        Tc("009536", "ANML HEALTH TCHN 2"),
        Tc("004751", "ANML HEALTH TCHN 2 VMTH"),
    ]);

    private static readonly Dictionary<string, ClassRef> None = new();

    [Theory]
    [InlineData("ACCOUNTING MGR 2 HC", Sites.HealthCenter)]
    [InlineData("Social Work HC Supervisor 2", Sites.HealthCenter)]
    [InlineData("Clin Lab SHS Supv 2", Sites.StudentHealth)]
    [InlineData("HC ADM SHS MGR 1", Sites.StudentHealth)]
    [InlineData("Animal Health Technician 2 VMTH", Sites.VetMedHospital)]
    [InlineData("Accounting Manager 2", null)]
    public void A_site_marker_names_the_site(string title, string? site) =>
        Sites.SiteOf(title).Should().Be(site);

    [Fact]
    public void The_site_is_part_of_the_class_identity_even_though_the_loose_search_key_drops_HC()
    {
        TitleNormalizer.TitleKey("Accounting Manager 2 HC").Should().Be(TitleNormalizer.TitleKey("Accounting Manager 2"),
            "the ported loose key is for search, and stays as ported");
        Sites.ClassKey("Accounting Manager 2 HC").Should().NotBe(Sites.ClassKey("Accounting Manager 2"));
        Sites.ClassKey("Clin Lab SHS Supv 2").Should().NotBe(Sites.ClassKey("Clin Lab Supv 2"));
        Sites.ClassKey("Accounting Manager 2").Should().Be(Sites.ClassKey("ACCOUNTING MGR 2"));
    }

    [Fact]
    public void Exempt_and_non_exempt_versions_are_separate_classes_but_not_sites()
    {
        Sites.ClassKey("Sra 2").Should().NotBe(Sites.ClassKey("Sra 2 Nex"));
        Sites.ClassKey("Staff Research Associate 2").Should().Be(Sites.ClassKey("SRA 2"));
        Sites.SiteOf("Sra 2 Nex").Should().BeNull("NEX is an FLSA status, not a site");
    }

    [Fact]
    public void A_regular_class_names_its_site_twins_and_the_reverse()
    {
        var known = Sites.ClassesByCode([("004845", "004845-accounting-manager-2-hc", "Accounting Manager 2 HC")]);
        var hc = Sites.TwinsOf("Accounting Manager 2", "000686", Index, known).Should().ContainSingle().Subject;
        hc.UcJobCode.Should().Be("004845");
        hc.Site.Should().Be(Sites.HealthCenter);
        hc.Slug.Should().Be("004845-accounting-manager-2-hc");
        hc.Title.Should().Be("Accounting Manager 2 HC", "named as JDWriter names the class");

        var regular = Sites.TwinsOf("Accounting Mgr 2 HC", "004845", Index, None).Should().ContainSingle().Subject;
        regular.UcJobCode.Should().Be("000686");
        regular.Site.Should().BeNull();
        regular.Title.Should().Be("Accounting Mgr 2", "from the payroll title, with no class to name it");
    }

    [Fact]
    public void Student_health_classes_pair_the_same_way()
    {
        var shs = Sites.TwinsOf("Clinical Laboratory Supervisor 2", "006569", Index, None).Should().ContainSingle().Subject;
        shs.UcJobCode.Should().Be("009367");
        shs.Site.Should().Be(Sites.StudentHealth);
        shs.Title.Should().Be("Clin Lab SHS Supv 2");

        Sites.TwinsOf("Clin Lab SHS Supv 2", "009367", Index, None).Single().UcJobCode.Should().Be("006569");
    }

    [Fact]
    public void Veterinary_hospital_classes_pair_the_same_way()
    {
        var known = Sites.ClassesByCode([("004751", "004751-animal-health-technician-2-vmth", "Animal Health Technician 2 VMTH")]);
        var vmth = Sites.TwinsOf("Animal Health Technician 2", "009536", Index, known).Should().ContainSingle().Subject;
        vmth.Site.Should().Be(Sites.VetMedHospital);
        vmth.Title.Should().Be("Animal Health Technician 2 VMTH");

        Sites.TwinsOf("Anml Health Tchn 2 VMTH", "004751", Index, None).Single().Title.Should().Be("Anml Health Tchn 2");
    }

    [Fact]
    public void A_twin_must_be_on_payroll_and_most_site_classes_have_none()
    {
        Sites.TwinsOf("Accounting Manager 1 HC", "004844", Index, None).Should().BeEmpty("000685 is matrix-only");
        Sites.TwinsOf("Social Work HC Supervisor 2", "006536", Index, None).Should().BeEmpty();
        Sites.TwinsOf("HC ADM SHS MGR 1", "005680", Index, None).Should().BeEmpty();
    }

    [Fact]
    public void A_site_class_never_falls_back_to_the_regular_class_standard()
    {
        var standards = new StandardsIndex([new ClassStandardRecord { LongTitle = "Accounting Manager 2" }]);

        standards.ForTitle("Accounting Mgr 2").Should().NotBeNull();
        standards.ForTitle("Accounting Mgr 2 HC").Should().BeNull();
    }

    [Theory]
    [InlineData("Financial Analyst 2", 10)]
    [InlineData("Financial Analyst 3 CX", 10)]
    [InlineData("Financial Analyst 4", 30)]
    [InlineData("Sra 5", 30)]
    [InlineData("Acad Achievement Supv 2", 30)]
    [InlineData("Administrative Manager 1", 30)]
    [InlineData("Lab Ast 1", 10)]
    [InlineData("Farm Laborer", 10)]
    public void Senior_and_supervisory_classes_may_customize_more(string title, int target) =>
        Customization.TargetPct(title).Should().Be(target);
}
