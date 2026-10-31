using Serilog.Core;
using Serilog.Events;

namespace Travel.Presentation.Logging
{
    /// <summary>
    /// Stamps every log line with who made the request and which request it was. Reads the HTTP
    /// context at enrich time rather than pushing a LogContext scope from a middleware: the exception
    /// handler runs before UseAuthentication, so a pushed scope would already be gone by the time it
    /// logs an unhandled error — exactly the line that most needs the user.
    /// </summary>
    public class HttpContextEnricher : ILogEventEnricher
    {
        private const string None = "-";
        private readonly IHttpContextAccessor _accessor;

        public HttpContextEnricher(IHttpContextAccessor accessor) => _accessor = accessor;

        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
        {
            var context = _accessor.HttpContext;
            var user = context?.User?.Identity is { IsAuthenticated: true } identity ? identity.Name : null;

            logEvent.AddPropertyIfAbsent(factory.CreateProperty("UserName", user ?? None));
            // TraceIdentifier is also what /Home/Error shows, so the id on the error page is greppable.
            logEvent.AddPropertyIfAbsent(factory.CreateProperty("RequestId", context?.TraceIdentifier ?? None));
            logEvent.AddPropertyIfAbsent(factory.CreateProperty("Source", ShortSource(logEvent)));
        }

        // "Travel.Presentation.Controllers.BookingController" → "BookingController".
        private static string ShortSource(LogEvent logEvent)
        {
            if (!logEvent.Properties.TryGetValue("SourceContext", out var value)
                || value is not ScalarValue { Value: string source })
                return "App";

            var dot = source.LastIndexOf('.');
            return dot < 0 ? source : source[(dot + 1)..];
        }
    }
}
