using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Server.Core.Data;
using Server.Core.Domain;

namespace Server.Tests.Data;

/// <summary>
/// Guards on the shape of the schema rather than on behavior.
///
/// These exist because the invariants below are cheap to break silently while adding an entity
/// months from now, and each one would fail as corrupted data rather than as a crash: an ordered
/// list that loses its order, an enum that silently re-maps when a member is inserted, a
/// duplicate profile for one class. The migration itself cannot catch any of that.
/// </summary>
public class SchemaTests
{
    private static IModel Model()
    {
        using var ctx = TestDbContextFactory.CreateInMemory();
        return ctx.Model;
    }

    [Fact]
    public void Every_enum_is_persisted_as_a_string()
    {
        // Ints would make the database unreadable for the HR analysts who are meant to query it
        // directly, and — worse — inserting an enum member would silently re-map existing rows.
        var intBacked = new List<string>();

        foreach (var entity in Model().GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                var clr = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                if (clr.IsEnum && prop.GetProviderClrType() != typeof(string))
                {
                    intBacked.Add($"{entity.ClrType.Name}.{prop.Name}");
                }
            }
        }

        intBacked.Should().BeEmpty("enums must round-trip as their names");
    }

    [Fact]
    public void Every_ordered_list_table_carries_an_ordinal()
    {
        // SQL has no inherent row order. A duty list read back unordered is a scrambled job
        // description, which is a data-correctness bug that looks like a rendering bug.
        var missing = Model().GetEntityTypes()
            .Where(e => typeof(ITextItem).IsAssignableFrom(e.ClrType))
            .Where(e => e.FindProperty(nameof(ITextItem.Ordinal)) is null)
            .Select(e => e.ClrType.Name)
            .ToList();

        missing.Should().BeEmpty();
    }

    [Fact]
    public void Text_item_tables_are_separate_tables_with_a_real_foreign_key()
    {
        // The eight of these share one EF configuration, but they must NOT be collapsed into a
        // single polymorphic table: that would trade every real foreign key and cascade for an
        // untyped parent id.
        var textItems = Model().GetEntityTypes()
            .Where(e => typeof(ITextItem).IsAssignableFrom(e.ClrType))
            .ToList();

        textItems.Should().HaveCountGreaterThan(5);

        foreach (var entity in textItems)
        {
            entity.GetForeignKeys().Should()
                .NotBeEmpty($"{entity.ClrType.Name} must belong to a parent by foreign key");
        }
    }

    [Theory]
    // One profile per slug, or URLs stop being stable identities.
    [InlineData(typeof(ClassProfile), nameof(ClassProfile.Slug))]
    // Re-ingesting a JD must update it, never duplicate it.
    [InlineData(typeof(JobDescription), nameof(JobDescription.SourceFile))]
    // Standards dedup on the STRICT key; the loose key silently discards ~25 real standards.
    [InlineData(typeof(JobStandard), nameof(JobStandard.TitleCodeKey))]
    // A retired code has exactly one successor, or the remap is not a function.
    [InlineData(typeof(Supersession), nameof(Supersession.FromCode))]
    // The claim the sign-in pipeline looks up on every request.
    [InlineData(typeof(AppUser), nameof(AppUser.NameIdentifier))]
    public void Identity_columns_are_unique(Type clrType, string property)
    {
        var entity = Model().FindEntityType(clrType)!;

        entity.GetIndexes()
            .Where(i => i.IsUnique)
            .SelectMany(i => i.Properties)
            .Select(p => p.Name)
            .Should().Contain(property);
    }

    [Fact]
    public void TitleCode_code_is_indexed_but_not_unique()
    {
        // The reference deliberately lists some titles twice — once as a title UCD could use and
        // once as one observed on payroll — so a unique index here would reject real data.
        var entity = Model().FindEntityType(typeof(TitleCode))!;
        var onCode = entity.GetIndexes()
            .Where(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(TitleCode.Code))
            .ToList();

        onCode.Should().ContainSingle();
        onCode[0].IsUnique.Should().BeFalse();
    }

    [Fact]
    public void Both_title_keys_are_indexed_and_distinct_columns()
    {
        // Merging the loose and strict keys once collapsed 221 reference entries and produced
        // confidently WRONG job codes. Keeping them as two indexed columns is what prevents a
        // future "simplification" from reintroducing that.
        var entity = Model().FindEntityType(typeof(TitleCode))!;
        var indexed = entity.GetIndexes().SelectMany(i => i.Properties).Select(p => p.Name).ToList();

        indexed.Should().Contain(nameof(TitleCode.TitleKey));
        indexed.Should().Contain(nameof(TitleCode.TitleCodeKey));
    }

    [Fact]
    public void Responsibility_percentages_are_nullable_and_unconstrained()
    {
        // Six real JDs in the corpus do not sum to 100 (range 0-200). A NOT NULL or a check
        // constraint here would reject genuine data at load time. Validate and report instead.
        var prop = Model().FindEntityType(typeof(JdResponsibility))!
            .FindProperty(nameof(JdResponsibility.Pct))!;

        prop.IsNullable.Should().BeTrue();
    }

    [Fact]
    public void Distribution_values_allow_null_as_a_real_observation()
    {
        // "The export did not say" is an observation about 1,027 of 1,367 JDs for `supervises`,
        // and is distinct from an explicit no. Dropping it would silently skew every consensus.
        var prop = Model().FindEntityType(typeof(ProfileDistributionValue))!
            .FindProperty(nameof(ProfileDistributionValue.Value))!;

        prop.IsNullable.Should().BeTrue();
    }

    [Fact]
    public void An_authored_jd_pins_its_class_profile()
    {
        // A finished JD is a record of what was published. Cascading a profile delete into it
        // would erase evidence; restrict forces the deletion to be dealt with deliberately.
        var fk = Model().FindEntityType(typeof(AuthoredJd))!
            .GetForeignKeys()
            .Single(f => f.PrincipalEntityType.ClrType == typeof(ClassProfile));

        fk.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
    }

    [Fact]
    public void Profile_children_are_removed_with_their_profile()
    {
        // The converse: everything derived from a profile is worthless without it, so it should
        // not be left orphaned.
        var profile = Model().FindEntityType(typeof(ClassProfile))!;
        var children = Model().GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .Where(f => f.PrincipalEntityType == profile && f.DeclaringEntityType.ClrType != typeof(AuthoredJd))
            .ToList();

        children.Should().NotBeEmpty();
        children.Should().OnlyContain(f => f.DeleteBehavior == DeleteBehavior.Cascade);
    }

    [Fact]
    public void Indexed_string_columns_stay_within_the_SQL_Server_key_limit()
    {
        // SQL Server caps an index key at 900 bytes, and nvarchar(max) cannot be indexed at all.
        // Both failures surface only when the migration is applied to real SQL Server, which is
        // exactly the point at which they are most annoying to discover.
        var offenders = new List<string>();

        foreach (var entity in Model().GetEntityTypes())
        {
            foreach (var index in entity.GetIndexes())
            {
                var bytes = 0;
                foreach (var prop in index.Properties)
                {
                    if (prop.ClrType != typeof(string))
                    {
                        bytes += 8;
                        continue;
                    }

                    var max = prop.GetMaxLength();
                    if (max is null)
                    {
                        offenders.Add($"{entity.ClrType.Name}.{prop.Name} (unbounded)");
                        continue;
                    }

                    bytes += max.Value * 2;
                }

                if (bytes > 900)
                {
                    offenders.Add($"{entity.ClrType.Name}[{string.Join(",", index.Properties.Select(p => p.Name))}] = {bytes} bytes");
                }
            }
        }

        offenders.Should().BeEmpty();
    }
}
