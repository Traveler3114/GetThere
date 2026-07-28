using GetThereAPI.Exceptions;
using GetThereAPI.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GetThere.Tests.ImportedTickets;

/// <summary>
/// Storage is a filesystem boundary, so the interesting assertions are about what it refuses. Blob
/// keys are server-minted today, but the containment check exists because a previous defect in the
/// feed importer came from trusting an identifier that "could not" contain path separators.
/// </summary>
public class TicketFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "getthere-store-tests", Guid.NewGuid().ToString("N"));

    private LocalTicketFileStore CreateStore()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TicketFiles:RootPath"] = _root })
            .Build();

        return new LocalTicketFileStore(new StubEnvironment(), configuration, NullLogger<LocalTicketFileStore>.Instance);
    }

    [Fact]
    public async Task Round_trips_a_file()
    {
        var store = CreateStore();
        byte[] content = [1, 2, 3, 4, 5];

        var key = await store.SaveAsync("user-1", content, ".pdf");
        var read = await store.ReadAsync("user-1", key);

        Assert.Equal(content, read);
        Assert.EndsWith(".pdf", key, StringComparison.Ordinal);
    }

    /// <summary>The uploaded filename never reaches disk — the key is minted, not echoed.</summary>
    [Fact]
    public async Task Mints_a_distinct_key_per_save()
    {
        var store = CreateStore();

        var first = await store.SaveAsync("user-1", [1], ".png");
        var second = await store.SaveAsync("user-1", [1], ".png");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task One_users_key_does_not_resolve_under_another_user()
    {
        var store = CreateStore();
        var key = await store.SaveAsync("user-1", [9, 9, 9], ".png");

        Assert.Null(await store.ReadAsync("user-2", key));
    }

    [Fact]
    public async Task Reading_an_unknown_key_returns_null()
    {
        var store = CreateStore();
        Assert.Null(await store.ReadAsync("user-1", "does-not-exist.png"));
    }

    /// <summary>
    /// Rejection is by string shape, not by resolved path, because what counts as a separator is
    /// platform-dependent — <c>..\..\etc\passwd</c> is an inert filename on Linux but escapes on
    /// Windows. Asserting on the shape check keeps this meaningful on whichever host runs it.
    /// </summary>
    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config\\sam")]
    [InlineData("subdir/../../escape.png")]
    [InlineData("nested/file.png")]
    [InlineData("..")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_keys_that_are_not_plain_file_names(string blobKey)
    {
        var store = CreateStore();

        Assert.Throws<AppException>(() => store.ResolvePath("user-1", blobKey));
    }

    /// <summary>A user id reaches the path too, so it gets the same treatment.</summary>
    [Theory]
    [InlineData("../other-user")]
    [InlineData("a/b")]
    public void Rejects_user_ids_that_are_not_plain_file_names(string userId)
    {
        var store = CreateStore();

        Assert.Throws<AppException>(() => store.ResolvePath(userId, "file.png"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_user(string userId)
    {
        var store = CreateStore();

        Assert.Throws<AppException>(() => store.ResolvePath(userId, "file.png"));
    }

    [Fact]
    public async Task Delete_removes_the_file_and_tolerates_a_second_call()
    {
        var store = CreateStore();
        var key = await store.SaveAsync("user-1", [1, 2], ".ics");

        await store.DeleteAsync("user-1", key);
        Assert.Null(await store.ReadAsync("user-1", key));

        // The purge sweep can race with itself; a missing file is not an error.
        await store.DeleteAsync("user-1", key);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "GetThere.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }
}
