using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Travel.Presentation.Logging
{
    /// <summary>
    /// Logs a form POST that was rejected and re-rendered. Without it, a validation rule whose message
    /// has no rendering surface (see the 2026-08-02 gotchas) leaves no trace anywhere: the POST returns
    /// 200 with the same form. Registered globally, so a form added later is covered with no code.
    /// </summary>
    public class ModelStateLoggingFilter : IActionFilter
    {
        private readonly ILogger<ModelStateLoggingFilter> _logger;

        public ModelStateLoggingFilter(ILogger<ModelStateLoggingFilter> logger) => _logger = logger;

        public void OnActionExecuting(ActionExecutingContext context) { }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (!HttpMethods.IsPost(context.HttpContext.Request.Method)
                || context.Result is not ViewResult
                || context.ModelState.IsValid
                || context.ActionDescriptor.EndpointMetadata.OfType<SkipModelStateLogAttribute>().Any())
                return;

            // Keys + messages only. The messages are the app's own; the binder's "The value 'x' is
            // invalid" only echoes numbers and dates, since a string never fails to bind.
            var errors = context.ModelState
                .Where(kv => kv.Value is { Errors.Count: > 0 })
                .Select(kv => $"{(kv.Key.Length == 0 ? "(form)" : kv.Key)}: " +
                              string.Join(" / ", kv.Value!.Errors.Select(e => e.ErrorMessage)));

            _logger.LogWarning("{Controller}.{Action} rejected: {Errors}",
                context.RouteData.Values["controller"], context.RouteData.Values["action"],
                string.Join("; ", errors));
        }
    }
}
