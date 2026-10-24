using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Server.Controllers;
using Server.Core.Ai;

namespace Server.Tests.Controllers;

public class AiServiceFailedTests
{
    private static ExceptionContext Context(Exception ex) =>
        new(new ActionContext(
            new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() },
            new RouteData(),
            new ActionDescriptor()), [])
        {
            Exception = ex,
        };

    [Fact]
    public void A_failed_AI_call_is_a_502_that_says_so_not_a_bare_500()
    {
        var context = Context(new StructuredLlmException("classify: the AI provider rejected the credentials."));

        new AiServiceFailedAttribute().OnException(context);

        context.ExceptionHandled.Should().BeTrue();
        var result = context.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(502);
        result.Value.Should().BeEquivalentTo(new
        {
            message = "The AI service didn't return a usable answer, so this couldn't be completed. Try again in a moment; if it keeps happening, ask a JDWriter administrator to check the AI provider in Settings.",
        });
    }

    [Fact]
    public void Other_failures_are_left_alone()
    {
        var context = Context(new InvalidOperationException("something else"));

        new AiServiceFailedAttribute().OnException(context);

        context.ExceptionHandled.Should().BeFalse();
        context.Result.Should().BeNull();
    }
}
