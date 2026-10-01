using System.Text;

namespace Jdw.Cli.Migration;

/// <summary>
/// What a migration run found and wrote.
///
/// Reported rather than asserted. A load that silently drops rows is the failure mode worth
/// guarding against here, so every stage records what it saw and the caller compares that against
/// the expected shape of the corpus.
/// </summary>
public sealed class MigrationReport
{
    public bool DryRun { get; set; }

    public int TitleCodes { get; set; }
    public int Supersessions { get; set; }
    public int AmbiguousSupersessions { get; set; }

    public int Standards { get; set; }
    public int StandardsWithCode { get; set; }
    public int StandardItems { get; set; }

    public int JobDescriptions { get; set; }
    public int JdResponsibilities { get; set; }
    public int JdDuties { get; set; }
    public int JdQualificationItems { get; set; }
    public int JdPemEntries { get; set; }
    public int DistinctJobCodes { get; set; }
    public int RemappedJobCodes { get; set; }

    public int Profiles { get; set; }
    public int ProfilesWithEnvelope { get; set; }
    public int ProfilesWithCoverage { get; set; }
    public int ProfilesWithConsolidated { get; set; }

    public int ComplianceRules { get; set; }

    /// <summary>
    /// JDs whose percentages do not sum to 100. Reported, never rejected: six real exports range
    /// from 0 to 200, and refusing them would mean refusing genuine data.
    /// </summary>
    public List<string> PercentAnomalies { get; set; } = [];

    /// <summary>Anything that could not be loaded, with enough detail to chase it.</summary>
    public List<string> Problems { get; set; } = [];

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine(DryRun
            ? "=== DRY RUN — nothing written. Re-run with --write to commit. ==="
            : "=== WRITE — committed to the database. ===");
        sb.AppendLine();
        sb.AppendLine($"  title codes           {TitleCodes,7}");
        sb.AppendLine($"  supersessions         {Supersessions,7}   (ambiguous: {AmbiguousSupersessions})");
        sb.AppendLine($"  job standards         {Standards,7}   (with code: {StandardsWithCode})");
        sb.AppendLine($"  standard items        {StandardItems,7}");
        sb.AppendLine($"  job descriptions      {JobDescriptions,7}   ({DistinctJobCodes} distinct codes, {RemappedJobCodes} remapped)");
        sb.AppendLine($"    responsibilities    {JdResponsibilities,7}");
        sb.AppendLine($"    duties              {JdDuties,7}");
        sb.AppendLine($"    qualification items {JdQualificationItems,7}");
        sb.AppendLine($"    PEM entries         {JdPemEntries,7}");
        sb.AppendLine($"  class profiles        {Profiles,7}   (envelope: {ProfilesWithEnvelope}, coverage: {ProfilesWithCoverage}, consolidated: {ProfilesWithConsolidated})");
        sb.AppendLine($"  compliance rules      {ComplianceRules,7}");

        if (PercentAnomalies.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  {PercentAnomalies.Count} JD(s) do not sum to 100% time (loaded anyway, by design):");
            foreach (var a in PercentAnomalies.Take(10))
            {
                sb.AppendLine($"    {a}");
            }
        }

        if (Problems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  {Problems.Count} PROBLEM(S):");
            foreach (var p in Problems.Take(20))
            {
                sb.AppendLine($"    {p}");
            }
        }

        return sb.ToString();
    }
}
