using Backend.Common;
using Backend.Common.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests;

/// <summary>An upload request is capped as a whole, not only part by part.</summary>
public class UploadSizeLimitTests
{
    [Fact]
    public void It_caps_the_whole_body_at_the_upload_budget()
    {
        var options = new MediaOptions { MaxImageBytes = 10, MaxVideoBytes = 100 };
        var feature = new BodySizeFeature();

        new UploadSizeLimitAttribute().OnResourceExecuting(Context(options, feature));

        Assert.Equal(options.MaxUploadRequestBytes, feature.MaxRequestBodySize);
        Assert.Equal(100 + 1024 * 1024, feature.MaxRequestBodySize);
    }

    [Fact]
    public void It_leaves_a_body_that_is_already_being_read_alone()
    {
        var feature = new BodySizeFeature { IsReadOnly = true, MaxRequestBodySize = 42 };

        new UploadSizeLimitAttribute().OnResourceExecuting(Context(new MediaOptions(), feature));

        Assert.Equal(42, feature.MaxRequestBodySize);
    }

    private static ResourceExecutingContext Context(MediaOptions options, IHttpMaxRequestBodySizeFeature feature)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(Microsoft.Extensions.Options.Options.Create(options))
                .BuildServiceProvider()
        };
        http.Features.Set(feature);

        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ResourceExecutingContext(action, [], []);
    }

    private sealed class BodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; init; }

        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }
}
