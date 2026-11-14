using Backend.Common.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Backend.Common;

/// <summary>
/// Caps the whole request body of an upload at <see cref="MediaOptions.MaxUploadRequestBytes"/>.
/// [RequestSizeLimit] wants a constant and the budget is configuration, so this sets the same
/// Kestrel feature at runtime. A resource filter runs before model binding, so an oversized
/// body is refused before any part of the form is buffered. FormOptions.MultipartBodyLengthLimit
/// alone bounds each part, not their sum.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UploadSizeLimitAttribute : Attribute, IResourceFilter, IOrderedFilter
{
    // NOTE: same slot as the built-in RequestSizeLimitAttribute, ahead of everything that reads the body.
    public int Order => 900;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();

        if (feature is { IsReadOnly: false })
        {
            var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<MediaOptions>>().Value;
            feature.MaxRequestBodySize = options.MaxUploadRequestBytes;
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
