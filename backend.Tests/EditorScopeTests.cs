using Backend.Common.Exceptions;
using Backend.Common.Security;
using Backend.Models.DTOs;
using Backend.Models.DTOs.Auth;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests;

/// <summary>
/// An assistant the author switched to full editing: anything to a post and its media,
/// including deleting, and still nothing else of the account.
/// </summary>
public class EditorScopeTests
{
    [Fact]
    public async Task It_can_edit_a_live_post_and_the_index_follows()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: false, withArtwork: true);
        harness.SignInWithToken(author, ApiTokenScope.Editor);

        var edited = await harness.PostService.UpdateAsync(post.Id, new UpdatePostDto { Title = "Rewritten", Body = "rewritten" });

        Assert.Equal("rewritten", edited.Body);
        Assert.False(edited.IsDraft);
        Assert.NotEmpty(await harness.Context.RagJobs.ToListAsync());
    }

    [Fact]
    public async Task It_keeps_the_showcase_choice_to_the_author()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: true);
        post.IsFeatured = true;
        await harness.Context.SaveChangesAsync();
        harness.SignInWithToken(author, ApiTokenScope.Editor);

        var edited = await harness.PostService.UpdateAsync(post.Id, new UpdatePostDto { Title = "Edited", Body = "edited" });

        Assert.True(edited.IsFeatured);
    }

    [Fact]
    public async Task It_can_replace_artwork()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var post = await harness.AddPostAsync(author, isDraft: false, withArtwork: true);
        var old = await harness.Context.Medias.AsNoTracking().SingleAsync(m => m.PostId == post.Id && m.Role == MediaRole.Thumbnail);
        harness.SignInWithToken(author, ApiTokenScope.Editor);

        var replacement = await harness.MediaService.AddToPostAsync(
            post.Id, TestFiles.FormFile(TestFiles.Png(), "new.png"), MediaRole.Thumbnail);

        harness.Context.ChangeTracker.Clear();
        var thumbnail = await harness.Context.Medias.SingleAsync(m => m.PostId == post.Id && m.Role == MediaRole.Thumbnail);
        Assert.Equal(replacement.Id, thumbnail.Id);
        Assert.DoesNotContain(old.ObjectKey, harness.Storage.Objects.Keys);
    }

    [Fact]
    public async Task It_can_unpublish_and_delete()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var live = await harness.AddPostAsync(author, isDraft: false, withArtwork: true);
        var draft = await harness.AddPostAsync(author, isDraft: true);
        harness.SignInWithToken(author, ApiTokenScope.Editor);
        var attachment = await harness.MediaService.AddToPostAsync(draft.Id, TestFiles.FormFile(TestFiles.Png(), "figure.png"));

        var unpublished = await harness.PostService.UnpublishAsync(live.Id);
        await harness.MediaService.DeleteAsync(draft.Id, attachment.Id);
        await harness.PostService.DeleteAsync(draft.Id);

        Assert.True(unpublished.IsDraft);
        harness.Context.ChangeTracker.Clear();
        Assert.False(await harness.Context.Posts.AnyAsync(p => p.Id == draft.Id));
        Assert.False(await harness.Context.Medias.AnyAsync(m => m.Id == attachment.Id));
    }

    [Fact]
    public async Task It_still_cannot_touch_another_authors_post()
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        var other = await harness.AddAuthorAsync("other@example.com", "a long enough password");
        var theirs = await harness.AddPostAsync(other, isDraft: true);
        harness.SignInWithToken(author, ApiTokenScope.Editor);

        await Assert.ThrowsAsync<ForbiddenException>(() => harness.PostService.DeleteAsync(theirs.Id));
    }

    [Theory]
    [InlineData(ApiTokenScope.Editor)]
    [InlineData((ApiTokenScope)99)]
    public async Task It_cannot_be_issued_by_hand_and_neither_can_an_unknown_scope(ApiTokenScope scope)
    {
        await using var harness = await TestHarness.CreateAsync();
        var author = await harness.AddAuthorAsync("author@example.com", "a long enough password");
        harness.SignIn(author);

        await Assert.ThrowsAsync<ValidationException>(() =>
            harness.Auth.CreateApiTokenAsync(new CreateApiTokenDto { Name = "bot", Scope = scope }));
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task It_can_write_where_a_post_endpoint_says_so(string method)
    {
        Assert.True(await Run(Request(method, new AllowEditorScopeAttribute()), ApiTokenScope.Editor));
        Assert.True(await Run(Request(method, new AllowAuthorScopeAttribute()), ApiTokenScope.Editor));
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    public async Task It_cannot_write_anywhere_else(string method)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(Request(method), ApiTokenScope.Editor));
    }

    [Fact]
    public async Task A_drafts_only_token_is_still_refused_what_full_editing_opens()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Run(Request("DELETE", new AllowEditorScopeAttribute()), ApiTokenScope.Author));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Run(Request("POST", new AllowEditorScopeAttribute()), ApiTokenScope.Author));
    }

    private static DefaultHttpContext Request(string method, params object[] metadata)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
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
