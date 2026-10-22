using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Server.Controllers;
using Server.Core.Uploads;
using Server.Helpers;

namespace Server.Tests.Uploads;

public class UploadStagingTests
{
    /// <summary>Staging in memory: what the browser would have put in blob storage.</summary>
    private sealed class FakeStaging : IUploadStaging
    {
        public Dictionary<string, byte[]> Blobs { get; } = [];
        public bool Enabled => true;

        public Task<StagedSlot> StageAsync(string owner, CancellationToken ct = default)
        {
            var name = $"{owner}/{Guid.NewGuid():N}";
            return Task.FromResult(new StagedSlot(name, new Uri($"https://st.example/uploads/{name}?sig=x"), DateTimeOffset.UtcNow.AddMinutes(10)));
        }

        public Task<byte[]> TakeAsync(string owner, string blobName, long maxBytes, CancellationToken ct = default)
        {
            if (!blobName.StartsWith($"{owner}/", StringComparison.Ordinal) || !Blobs.Remove(blobName, out var bytes))
            {
                throw new StagedUploadException("That upload isn't one of yours. Choose the file again.");
            }

            return Task.FromResult(bytes);
        }
    }

    private static ClaimsPrincipal Person(string id = "person-1") =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));

    private static HttpRequest Form(ClaimsPrincipal user, IFormFileCollection? files = null, string? staged = null)
    {
        var context = new DefaultHttpContext { User = user };
        var fields = new Dictionary<string, StringValues>();
        if (staged is not null)
        {
            fields[UploadReader.StagedField] = staged;
        }

        context.Request.ContentType = "multipart/form-data; boundary=x";
        context.Request.Form = new FormCollection(fields, files ?? new FormFileCollection());
        return context.Request;
    }

    [Fact]
    public async Task A_file_sent_in_the_request_is_read_as_before()
    {
        var bytes = Encoding.UTF8.GetBytes("a document");
        var files = new FormFileCollection { new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "jd.docx") { Headers = new HeaderDictionary(), ContentType = "application/x-test" } };

        var read = await new UploadReader(new DirectUploads()).ReadAsync(Form(Person(), files), 1_000, default);

        read.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Name = "jd.docx", ContentType = "application/x-test", Bytes = bytes });
    }

    [Fact]
    public async Task A_staged_file_is_taken_from_storage_by_its_owner_and_is_gone_after()
    {
        var staging = new FakeStaging();
        var owner = UploadReader.Owner(Person())!;
        var slot = await staging.StageAsync(owner);
        staging.Blobs[slot.BlobName] = Encoding.UTF8.GetBytes("staged bytes");
        var staged = JsonSerializer.Serialize(new[] { new { blob = slot.BlobName, name = "Lab Ast 1.docx", type = "application/x-test" } });

        var read = await new UploadReader(staging).ReadAsync(Form(Person(), staged: staged), 1_000, default);

        read.Should().ContainSingle().Which.Name.Should().Be("Lab Ast 1.docx");
        Encoding.UTF8.GetString(read[0].Bytes).Should().Be("staged bytes");
        staging.Blobs.Should().BeEmpty("a staged file is read once and deleted");
    }

    [Fact]
    public async Task Someone_else_s_staged_file_is_refused()
    {
        var staging = new FakeStaging();
        var theirs = await staging.StageAsync(UploadReader.Owner(Person("someone-else"))!);
        staging.Blobs[theirs.BlobName] = [1, 2, 3];
        var staged = JsonSerializer.Serialize(new[] { new { blob = theirs.BlobName, name = "x.docx" } });

        var act = () => new UploadReader(staging).ReadAsync(Form(Person(), staged: staged), 1_000, default);

        await act.Should().ThrowAsync<StagedUploadException>();
        staging.Blobs.Should().ContainKey(theirs.BlobName);
    }

    [Fact]
    public async Task A_staged_file_on_a_server_without_staging_is_refused_with_a_reason()
    {
        var act = () => new UploadReader(new DirectUploads()).ReadAsync(Form(Person(), staged: "[]"), 1_000, default);

        (await act.Should().ThrowAsync<StagedUploadException>()).Which.Message.Should().Contain("send the file itself");
    }

    [Fact]
    public void The_owner_in_a_blob_name_is_a_hash_that_says_nothing_about_the_person()
    {
        var owner = UploadReader.Owner(Person("jdoe@ucdavis.edu"))!;

        owner.Should().MatchRegex("^[0-9a-f]{16}$").And.NotContain("jdoe");
        UploadReader.Owner(Person("jdoe@ucdavis.edu")).Should().Be(owner, "the same person, the same owner");
        UploadReader.Owner(new ClaimsPrincipal(new ClaimsIdentity())).Should().BeNull();
    }

    [Fact]
    public async Task Blob_staging_refuses_a_name_that_isnt_the_callers_before_touching_storage()
    {
        // The connection string is never dialled: the name is checked first.
        var staging = new BlobUploadStaging(new UploadOptions { ConnectionString = "UseDevelopmentStorage=true" });
        var owner = UploadReader.Owner(Person())!;

        foreach (var name in new[] { "../secrets", $"{owner}/not-a-guid", $"{new string('0', 16)}/{Guid.NewGuid():N}", $"{owner}/{Guid.NewGuid():N}/x" })
        {
            var act = () => staging.TakeAsync(owner, name, 1_000);
            await act.Should().ThrowAsync<StagedUploadException>(name);
        }
    }

    [Fact]
    public async Task Without_staging_the_client_is_told_to_send_files_directly()
    {
        var controller = new UploadsController(new DirectUploads(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadsController>.Instance) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Person() } } };

        var ok = (await controller.Stage(new UploadsController.StageRequest { Count = 2 }, default)).Should().BeOfType<OkObjectResult>().Subject;

        JsonSerializer.Serialize(ok.Value).Should().Be("""{"direct":true}""");
    }

    [Fact]
    public async Task With_staging_each_file_gets_its_own_link_under_the_callers_name()
    {
        var controller = new UploadsController(new FakeStaging(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadsController>.Instance) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Person() } } };

        var ok = (await controller.Stage(new UploadsController.StageRequest { Count = 2 }, default)).Should().BeOfType<OkObjectResult>().Subject;

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var slots = json.RootElement.GetProperty("slots");
        slots.GetArrayLength().Should().Be(2);
        slots[0].GetProperty("blob").GetString().Should().StartWith(UploadReader.Owner(Person()) + "/");
        slots[0].GetProperty("blob").GetString().Should().NotBe(slots[1].GetProperty("blob").GetString());
        (await controller.Stage(new UploadsController.StageRequest { Count = 0 }, default)).Should().BeOfType<BadRequestObjectResult>();
    }

    private sealed class RefusingStaging : IUploadStaging
    {
        public bool Enabled => true;

        public Task<StagedSlot> StageAsync(string owner, CancellationToken ct = default) =>
            throw new Azure.RequestFailedException(403, "This request is not authorized to perform this operation.");

        public Task<byte[]> TakeAsync(string owner, string blobName, long maxBytes, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Storage_that_refuses_to_sign_links_is_a_503_that_says_so()
    {
        var controller = new UploadsController(new RefusingStaging(), Microsoft.Extensions.Logging.Abstractions.NullLogger<UploadsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Person() } },
        };

        var result = (await controller.Stage(new UploadsController.StageRequest(), default)).Should().BeOfType<ObjectResult>().Subject;

        result.StatusCode.Should().Be(503);
        result.Value.Should().BeEquivalentTo(new { message = "Uploads aren't available right now: JDWriter can't reach its upload storage. Ask a JDWriter administrator to check the upload storage setup." });
    }

    [Fact]
    public void An_unusable_staged_file_is_a_400_with_its_reason()
    {
        var context = new ExceptionContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = new StagedUploadException("The upload wasn't found."),
        };

        new StagedUploadRefusedAttribute().OnException(context);

        context.ExceptionHandled.Should().BeTrue();
        context.Result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeEquivalentTo(new { message = "The upload wasn't found." });
    }
}
