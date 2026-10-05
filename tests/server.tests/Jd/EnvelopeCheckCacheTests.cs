using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Server.Core.Jd;

namespace Server.Tests.Jd;

public class EnvelopeCheckCacheTests
{
    private static BuildInputs With(params string[] added) => new() { AddedItems = [.. added] };

    [Fact]
    public void A_verdict_is_recalled_only_for_the_same_class_and_the_same_additions()
    {
        var cache = new EnvelopeCheckCache(new MemoryCache(new MemoryCacheOptions()));
        cache.Remember("009605-lab-ast-1", With("Runs the plant sale"), EnvelopeVerdict.OutOfEnvelope);

        cache.Recall("009605-lab-ast-1", With("Runs the plant sale")).Should().Be(EnvelopeVerdict.OutOfEnvelope);
        cache.Recall("009605-lab-ast-1", With("Something else")).Should().BeNull("different additions need their own check");
        cache.Recall("004724-farm-laborer", With("Runs the plant sale")).Should().BeNull("a different class needs its own check");
    }

    [Fact]
    public void Notes_are_part_of_what_was_judged()
    {
        var cache = new EnvelopeCheckCache(new MemoryCache(new MemoryCacheOptions()));
        var judged = With("Runs the plant sale");
        cache.Remember("x", judged, EnvelopeVerdict.InEnvelope);

        var withNotes = With("Runs the plant sale");
        withNotes.Notes = "Also supervises two students.";

        cache.Recall("x", withNotes).Should().BeNull();
    }
}
