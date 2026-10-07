using FluentAssertions;
using Server.Helpers;

namespace Server.Tests.Helpers;

/// <summary>
/// Mounting the app under a path (people.caes.ucdavis.edu/jdwriter). The client finds its router
/// base path and API prefix in the base element alone, so a missing or wrong one strands it at the
/// host's root, where the portal lives.
/// </summary>
public class SpaIndexTests
{
    [Theory]
    [InlineData("/jdwriter", "/jdwriter")]
    [InlineData("/jdwriter/", "/jdwriter")]
    [InlineData("jdwriter", "/jdwriter")]
    [InlineData(" /hr/jdwriter/ ", "/hr/jdwriter")]
    [InlineData("/", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_path_base_is_normalized_to_a_leading_slash_and_no_trailing_one(string? configured, string expected) =>
        SpaIndex.NormalizePathBase(configured).Should().Be(expected);

    [Fact]
    public void The_base_is_the_first_thing_in_head_so_every_relative_url_resolves_against_it()
    {
        var html = "<!doctype html><html><head><link rel=\"icon\" href=\"./thumbnail.svg\" /></head></html>";

        SpaIndex.InjectBase(html, "/jdwriter").Should().Be(
            "<!doctype html><html><head><base href=\"/jdwriter/\" /><link rel=\"icon\" href=\"./thumbnail.svg\" /></head></html>");
    }

    [Fact]
    public void At_the_root_the_base_is_a_single_slash()
    {
        SpaIndex.InjectBase("<head></head>", "").Should().Be("<head><base href=\"/\" /></head>");
    }

    [Fact]
    public void A_path_base_is_encoded_into_the_attribute()
    {
        SpaIndex.InjectBase("<head></head>", "/a\"b").Should().NotContain("/a\"b").And.Contain("&quot;");
    }
}
