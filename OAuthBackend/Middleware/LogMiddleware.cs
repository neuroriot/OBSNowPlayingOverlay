using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using NLog;
using OAuthBackend.Services;
using System;
using System.Text;
using System.Threading.Tasks;

namespace OAuthBackend.Middleware
{
    public class LogMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly RedisService _redisService;
        private readonly Logger _logger = LogManager.GetLogger("ACCE");

        public LogMiddleware(RequestDelegate next, RedisService redisService)
        {
            _next = next;
            _redisService = redisService;
        }

        public async Task Invoke(HttpContext context)
        {
            var originalResponseBodyStream = context.Response.Body;

            try
            {
                var remoteIpAddress = context.GetRemoteIPAddress();
                var requestUrl = context.Request.GetDisplayUrl();

                await _next(context);

                // Generate from ChatGPT
                var route = context.GetRouteValue("action")?.ToString()?.ToLower();
                if (route != null && route == "statuscheck")
                    return;

                _logger.Info($"{remoteIpAddress} | {context.Request.Method} | {context.Response.StatusCode} | {requestUrl}");
            }
            catch (Exception e)
            {
                _logger.Error(e);

                var errorMessage = JsonConvert.SerializeObject(new
                {
                    ErrorMessage = e.Message
                });
                var bytes = Encoding.UTF8.GetBytes(errorMessage);

                await originalResponseBodyStream.WriteAsync(
                    bytes, 0, bytes.Length);
            }
        }
    }
}