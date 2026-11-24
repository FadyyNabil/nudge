using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Marketplace.Services;

/// <summary>
/// A form post that names an action the page does not have (for example an old or made-up ?handler=...)
/// gets a normal 404 page instead of rendering the page half-empty and failing with a 500.
/// </summary>
public class UnknownHandlerFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var req = context.HttpContext.Request;
        if (context.HandlerMethod is null && !HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method))
        {
            context.Result = new NotFoundResult();
            return Task.CompletedTask;
        }
        return next();
    }
}
