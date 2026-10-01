using Microsoft.Extensions.Logging;
using Server.Core.Domain;
using Server.Core.Profiles;

namespace Server.Core.Standards;

/// <summary>
/// Builds a bootstrap envelope by asking <see cref="EnvelopeSynthesizer"/>, degrading to its
/// deterministic output when the model cannot be reached.
///
/// A seam between two halves of the port that were written independently and each invented a
/// carrier for the same five fields — <see cref="BootstrapMeta"/> here and
/// <see cref="EnvelopeSynthesizer.StandardMeta"/> there. Rather than change either, this maps one
/// to the other; they are identical in content, and collapsing them would mean editing code on both
/// sides of a boundary that is otherwise clean.
///
/// The degradation is the behaviour that matters. Bootstrapping a class from its official standard
/// is how a class with no ingested JDs becomes usable at all, so a missing API key or a failed call
/// must not fail the bootstrap — it must produce the deterministic envelope instead. The reference
/// does exactly that, and an analyst can tell the difference because the profile records
/// <c>EnvelopeSource</c>.
/// </summary>
public sealed class SynthesizedEnvelopeBuilder : IStandardEnvelopeBuilder
{
    private readonly EnvelopeSynthesizer _synthesizer;
    private readonly ILogger<SynthesizedEnvelopeBuilder>? _logger;

    public SynthesizedEnvelopeBuilder(
        EnvelopeSynthesizer synthesizer,
        ILogger<SynthesizedEnvelopeBuilder>? logger = null)
    {
        _synthesizer = synthesizer;
        _logger = logger;
    }

    public async Task<JobEnvelope> BuildAsync(
        ClassStandardRecord standard, BootstrapMeta meta, CancellationToken ct = default)
    {
        var synthesizerMeta = new EnvelopeSynthesizer.StandardMeta(
            meta.Title, meta.Code, meta.Family, meta.Function, meta.Program);

        try
        {
            return await _synthesizer.SynthesizeFromStandardAsync(standard, synthesizerMeta, ct);
        }
        catch (OperationCanceledException)
        {
            // A cancelled request is the caller giving up, not a synthesis failure. Falling back
            // here would do expensive work nobody is waiting for.
            throw;
        }
        catch (Exception ex)
        {
            // Deliberately broad: no API key, a transport failure, a refusal, a schema mismatch —
            // every one of them should still yield a usable class rather than a failed bootstrap.
            // Logged at warning because the resulting envelope IS worse, and someone should know
            // the class was built without the model's help.
            _logger?.LogWarning(ex,
                "Envelope synthesis failed for {Title}; falling back to the deterministic envelope.",
                meta.Title);

            return EnvelopeSynthesizer.DeterministicFromStandard(standard);
        }
    }
}
