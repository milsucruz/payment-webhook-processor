using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Payments.Api.Tests.Webhooks;

public class ClientIpStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Client-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                string? clientIp = context.Request.Headers[HeaderName];
                if (clientIp is not null)
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(clientIp);
                }

                return nextMiddleware(context);
            });
            next(app);
        };
}
