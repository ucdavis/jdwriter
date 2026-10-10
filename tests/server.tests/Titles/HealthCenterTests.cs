using FluentAssertions;
using Server.Core.Domain;
using Server.Core.Standards;
using Server.Core.Titles;

namespace Server.Tests.Titles;

/// <summary>
/// Health Center (HC) classes are their own classes: never the same class as the title without HC,
/// never built from the regular class's standard, and named as each other's twin when both exist.
/// Titles are real UC payroll titles from the public reference.
/// </summary>
public class HealthCenterTests
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
    ]);

    private static readonly Dictionary<string, ClassRef> NoSlugs = new();

    [Theory]
    [InlineData("ACCOUNTING MGR 2 HC", true)]
    [InlineData("Social Work HC Supervisor 2", true)]
    [InlineData("Accounting Manager 2", false)]
    [InlineData("Chemistry Lab Tech", false)]
    public void An_HC_token_marks_a_health_center_class(string title, bool hc) =>
        HealthCenter.IsHealthCenter(title).Should().Be(hc);

    [Fact]
    public void HC_is_part_of_the_class_identity_even_though_the_loose_search_key_drops_it()
    {
        TitleNormalizer.TitleKey("Accounting Manager 2 HC").Should().Be(TitleNormalizer.TitleKey("Accounting Manager 2"),
            "the ported loose key is for search, and stays as ported");
        HealthCenter.ClassKey("Accounting Manager 2 HC").Should().NotBe(HealthCenter.ClassKey("Accounting Manager 2"));
        HealthCenter.ClassKey("Accounting Manager 2").Should().Be(HealthCenter.ClassKey("ACCOUNTING MGR 2"));
    }

    [Fact]
    public void A_regular_class_names_its_health_center_twin_and_the_reverse()
    {
        var known = HealthCenter.ClassesByCode([("004845", "004845-accounting-manager-2-hc", "Accounting Manager 2 HC")]);
        var hc = HealthCenter.TwinOf("Accounting Manager 2", "000686", Index, known)!;
        hc.UcJobCode.Should().Be("004845");
        hc.HealthCenter.Should().BeTrue();
        hc.Slug.Should().Be("004845-accounting-manager-2-hc");
        hc.Title.Should().Be("Accounting Manager 2 HC", "named as JDWriter names the class");

        var regular = HealthCenter.TwinOf("Accounting Mgr 2 Hc", "004845", Index, NoSlugs)!;
        regular.UcJobCode.Should().Be("000686");
        regular.HealthCenter.Should().BeFalse();
        regular.Slug.Should().BeNull("the regular class has no class in JDWriter yet");
        regular.Title.Should().Be("Accounting Mgr 2", "from the payroll title, with no class to name it");
    }

    [Fact]
    public void A_twin_must_be_on_payroll_and_most_HC_classes_have_none()
    {
        HealthCenter.TwinOf("Accounting Manager 1 HC", "004844", Index, NoSlugs).Should().BeNull("000685 is matrix-only");
        HealthCenter.TwinOf("Social Work HC Supervisor 2", "006536", Index, NoSlugs).Should().BeNull();
    }

    [Fact]
    public void An_HC_class_never_falls_back_to_the_regular_class_standard()
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
