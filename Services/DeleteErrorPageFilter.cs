using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SRXPanel.Services;

/// <summary>
/// Global Razor Pages filter that turns an unhandled exception thrown from a
/// delete/remove handler into a friendly flash message and a redirect back to the
/// same page — instead of the developer exception page (dev) or /Error (prod).
///
/// This covers "wrap all delete actions in try/catch" in one place: any handler
/// whose name contains "Delete" or "Remove" (e.g. OnPostDeleteNodeAsync) is
/// protected. Non-delete handlers are unaffected, and handlers that already set
/// their own TempData error (no exception thrown) are left alone.
/// </summary>
public class DeleteErrorPageFilter : IAsyncPageFilter
{
    private readonly ILogger<DeleteErrorPageFilter> _logger;

    public DeleteErrorPageFilter(ILogger<DeleteErrorPageFilter> logger) => _logger = logger;

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var handlerName = context.HandlerMethod?.MethodInfo.Name ?? string.Empty;
        var isDelete = handlerName.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                       || handlerName.Contains("Remove", StringComparison.OrdinalIgnoreCase);

        var executed = await next();

        if (isDelete
            && executed.Exception != null
            && !executed.ExceptionHandled
            && context.HandlerInstance is PageModel page)
        {
            _logger.LogError(executed.Exception, "Delete handler {Handler} failed", handlerName);

            page.TempData["Error"] = "Could not delete. Please try again.";

            // Redirect back to the current page (PRG) — preserves any route values
            // in the path such as /Admin/Nodes/Detail/5.
            var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
            executed.Result = new LocalRedirectResult(returnUrl);
            executed.ExceptionHandled = true;
        }
    }
}
