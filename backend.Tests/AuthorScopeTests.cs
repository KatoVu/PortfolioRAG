using Backend.Common.Exceptions;
using Backend.Common.Security;
using Backend.Models.DTOs;
using Backend.Models.DTOs.OAuth;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests;

/// <summary>
/// What an AI assistant's token may do: write drafts, and nothing that destroys anything.
/// </summary>
public class AuthorScopeTests
{
    [Fact]
    public async Task It_can_create_and_edit_a_draft()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        harness.SignInWithToken(author, ApiTokenScope.Author);

        var post = await harness.PostService.CreateAsync(new CreatePostDto { Title = "Written by a bot", Body = "draft" });
        var edited = await harness.PostService.UpdateAsync(post.Id, new UpdatePostDto { Title = "Written by a bot", Body = "better" });

        Assert.Equal("better", edited.Body);
    }

    [Fact]
    public async Task It_cannot_edit_a_published_post()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: false);
        harness.SignInWithToken(author, ApiTokenScope.Author);

        await Assert.ThrowsAsync<ForbiddenException>(() => harness.PostService.UpdateAsync(
            post.Id, new UpdatePostDto { Title = "Rewritten", Body = "rewritten" }));
    }

    [Fact]
    public async Task Its_edit_leaves_the_featured_flag_alone()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: true, isFeatured: true);
        harness.SignInWithToken(author, ApiTokenScope.Author);

        var edited = await harness.PostService.UpdateAsync(
            post.Id, new UpdatePostDto { Title = "Still featured", Body = "body", IsFeatured = false });

        Assert.True(edited.IsFeatured);
    }

    [Fact]
    public async Task It_can_give_a_draft_its_artwork_and_publish_it()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: true);
        harness.SignInWithToken(author, ApiTokenScope.Author);

        await harness.MediaService.AddToPostAsync(post.Id, TestFiles.FormFile(TestFiles.Png(), "thumb.png"), MediaRole.Thumbnail);
        await harness.MediaService.AddToPostAsync(post.Id, TestFiles.FormFile(TestFiles.Mp4(), "trailer.mp4"), MediaRole.Trailer);
        var published = await harness.PostService.PublishAsync(post.Id);

        Assert.False(published.IsDraft);
    }

    [Fact]
    public async Task It_cannot_replace_artwork_because_replacing_deletes()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: true);

        harness.SignIn(author);
        var mine = await harness.MediaService.AddToPostAsync(post.Id, TestFiles.FormFile(TestFiles.Png(), "mine.png"), MediaRole.Thumbnail);
        var objectsBefore = harness.Storage.Objects.Count;

        harness.SignInWithToken(author, ApiTokenScope.Author);
        await Assert.ThrowsAsync<ConflictException>(() => harness.MediaService.AddToPostAsync(
            post.Id, TestFiles.FormFile(TestFiles.Png(), "bot.png"), MediaRole.Thumbnail));

        // Nothing was uploaded, and the author's own thumbnail is still there.
        Assert.Equal(objectsBefore, harness.Storage.Objects.Count);
        var media = Assert.Single(await harness.Context.Medias.Where(m => m.PostId == post.Id).ToListAsync());
        Assert.Equal(mine.Id, media.Id);
    }

    [Fact]
    public async Task A_session_can_still_replace_artwork()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: true);
        harness.SignIn(author);

        await harness.MediaService.AddToPostAsync(post.Id, TestFiles.FormFile(TestFiles.Png(), "one.png"), MediaRole.Thumbnail);
        var second = await harness.MediaService.AddToPostAsync(post.Id, TestFiles.FormFile(TestFiles.Png(), "two.png"), MediaRole.Thumbnail);

        var media = Assert.Single(await harness.Context.Medias.Where(m => m.PostId == post.Id).ToListAsync());
        Assert.Equal(second.Id, media.Id);
    }

    [Fact]
    public async Task It_cannot_add_to_or_recaption_a_published_post()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: false, withArtwork: true);
        var thumbnail = await harness.Context.Medias.FirstAsync(m => m.PostId == post.Id);
        harness.SignInWithToken(author, ApiTokenScope.Author);

        await Assert.ThrowsAsync<ForbiddenException>(() => harness.MediaService.AddToPostAsync(
            post.Id, TestFiles.FormFile(TestFiles.Png(), "extra.png")));
        await Assert.ThrowsAsync<ForbiddenException>(() => harness.MediaService.UpdateAsync(
            post.Id, thumbnail.Id, new UpdateMediaDto { Caption = "changed" }));
    }

    // ------------------------------------------------------------------
    // The route-level rules, enforced before any service runs
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("DELETE", true)]
    [InlineData("DELETE", false)]
    public async Task It_can_never_delete(string method, bool endpointAllowsAuthorScope)
    {
        var context = Request(method, endpointAllowsAuthorScope);

        await Assert.ThrowsAsync<ForbiddenException>(() => Run(context, ApiTokenScope.Author));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task It_can_only_write_where_an_endpoint_says_so(string method)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(Request(method, allowsAuthorScope: false), ApiTokenScope.Author));

        var reached = await Run(Request(method, allowsAuthorScope: true), ApiTokenScope.Author);
        Assert.True(reached);
    }

    [Fact]
    public async Task It_can_read_anything_a_read_token_can()
    {
        Assert.True(await Run(Request("GET", allowsAuthorScope: false), ApiTokenScope.Author));
    }

    [Fact]
    public async Task A_write_token_is_not_held_to_the_author_rules()
    {
        Assert.True(await Run(Request("DELETE", allowsAuthorScope: false), ApiTokenScope.Write));
    }

    private static DefaultHttpContext Request(string method, bool allowsAuthorScope)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;

        var metadata = allowsAuthorScope
            ? new EndpointMetadataCollection(new AllowAuthorScopeAttribute())
            : new EndpointMetadataCollection();

        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));
        return context;
    }

    /// <summary>Runs the middleware; true if the request got through to the endpoint.</summary>
    private static async Task<bool> Run(HttpContext context, ApiTokenScope scope)
    {
        var reached = false;
        var middleware = new ApiTokenRestrictionMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, new StubCurrentUser { AuthorId = 1, IsApiToken = true, ApiTokenScope = scope });
        return reached;
    }
}

/// <summary>One-time upload links, which is how an assistant on another machine sends a file.</summary>
public class UploadTicketTests
{
    [Fact]
    public async Task A_link_uploads_one_file_with_its_role_and_caption()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: true);
        harness.SignInWithToken(author, ApiTokenScope.Author);

        var ticket = await harness.UploadTickets.CreateAsync(
            post.Id, new CreateUploadTicketDto { Role = MediaRole.Thumbnail, Caption = "The dashboard" });

        Assert.StartsWith("https://api.example.com/api/uploads/pfu_", ticket.UploadUrl);

        // The upload itself arrives with no credential at all.
        harness.SignOut();
        var media = await harness.UploadTickets.UploadAsync(RawTicket(ticket), TestFiles.FormFile(TestFiles.Png(), "thumb.png"));

        Assert.Equal(MediaRole.Thumbnail, media.Role);
        Assert.Equal("The dashboard", media.Caption);
    }

    [Fact]
    public async Task A_link_works_once()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: true);
        harness.SignIn(author);

        var ticket = await harness.UploadTickets.CreateAsync(post.Id, new CreateUploadTicketDto());
        harness.SignOut();

        await harness.UploadTickets.UploadAsync(RawTicket(ticket), TestFiles.FormFile(TestFiles.Png(), "a.png"));
        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.UploadTickets.UploadAsync(RawTicket(ticket), TestFiles.FormFile(TestFiles.Png(), "b.png")));
    }

    [Fact]
    public async Task A_link_expires()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: true);
        harness.SignIn(author);

        var ticket = await harness.UploadTickets.CreateAsync(post.Id, new CreateUploadTicketDto());
        harness.TimeProvider.Advance(TimeSpan.FromMinutes(16));

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.UploadTickets.UploadAsync(RawTicket(ticket), TestFiles.FormFile(TestFiles.Png(), "a.png")));
    }

    [Fact]
    public async Task An_assistants_link_is_held_to_its_rules_when_spent()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: true);

        harness.SignInWithToken(author, ApiTokenScope.Author);
        var ticket = await harness.UploadTickets.CreateAsync(post.Id, new CreateUploadTicketDto());

        // The author publishes in the meantime.
        var tracked = await harness.Context.Posts.FirstAsync(p => p.Id == post.Id);
        tracked.IsDraft = false;
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        harness.SignOut();
        await Assert.ThrowsAsync<ForbiddenException>(
            () => harness.UploadTickets.UploadAsync(RawTicket(ticket), TestFiles.FormFile(TestFiles.Png(), "a.png")));
    }

    [Fact]
    public async Task No_link_is_issued_for_someone_elses_post()
    {
        await using var harness = await TestHarness.CreateAsync();
        var owner = await harness.AddAuthorAsync("owner@example.com", "a long enough password");
        var other = await harness.AddAuthorAsync("other@example.com", "a long enough password");
        var post = await harness.AddPostAsync(owner, isDraft: true);
        harness.SignIn(other);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => harness.UploadTickets.CreateAsync(post.Id, new CreateUploadTicketDto()));
    }

    [Fact]
    public async Task A_made_up_link_is_unknown()
    {
        await using var harness = await TestHarness.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.UploadTickets.UploadAsync("pfu_made-up", TestFiles.FormFile(TestFiles.Png(), "a.png")));
        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.UploadTickets.UploadAsync("pfl_wrong-kind", TestFiles.FormFile(TestFiles.Png(), "a.png")));
    }

    private static string RawTicket(UploadTicketDto ticket) => ticket.UploadUrl[(ticket.UploadUrl.LastIndexOf('/') + 1)..];
}
