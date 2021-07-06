using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Baseline.Validate
{
    /// <summary>
    /// Middleware that handles any validation errors thrown and serializes and returns them as a JSON response.
    /// </summary>
    public class JsonValidationFailureMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<JsonValidationFailureMiddleware> _logger;

        /// <summary>
        /// Initialises a new instance of the <see cref="JsonValidationFailureMiddleware" /> class.
        /// </summary>
        /// <param name="next">The next action in the pipeline to execute.</param>
        /// <param name="logger">A logger.</param>
        public JsonValidationFailureMiddleware(
            RequestDelegate next,
            ILogger<JsonValidationFailureMiddleware> logger
        )
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>
        /// Handles the invocation of the middleware, calling the next action in the pipeline and handling validation
        /// errors if they are thrown.
        /// </summary>
        /// <param name="httpContext">The context of the request and response.</param>
        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                await _next.Invoke(httpContext);
            }
            catch (ValidationFailedException e)
            {
                // If the caller does not accept JSON responses then we shouldn't return one!
                if (!httpContext.Request.Headers?["Accept"].Any(x => x.Contains("application/json")) ?? true)
                {
                    _logger.LogTrace("Skipping JsonValidationFailureMiddleware as requestee cannot accept JSON.");
                    throw;
                }
                
                _logger.LogInformation(
                    e,
                    $"Validation failed for object {e.ValidationResult.ValidationTarget}."
                );

                if (httpContext.Response.HasStarted)
                {
                    return;
                }

                httpContext.Response.Clear();
                httpContext.Response.OnStarting(ClearCacheHeaders, httpContext);
                httpContext.Response.StatusCode = 422;
                httpContext.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(
                    httpContext.Response.Body,
                    new
                    {
                        reason = "Validation failure.",
                        validationFailures = e.ValidationResult
                            .Failures
                            .Select(vrf => new { property = vrf.Key, message = vrf.Value })
                    }
                );
            }
        }
        
        /// <summary>
        /// Prevents any cache headers being returned from this endpoint.
        /// </summary>
        /// <param name="state">The current response state.</param>
        private static Task ClearCacheHeaders(object state)
        {
            var headers = ((HttpContext)state).Response.Headers;
            
            headers["Cache-Control"] = "no-cache,no-store";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "-1";
            headers["ETag"] = default;

            return Task.CompletedTask;
        }
    }
}