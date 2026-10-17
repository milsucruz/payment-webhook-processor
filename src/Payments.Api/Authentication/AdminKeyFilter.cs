using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Payments.Api.Authentication;

public sealed class AdminKeyFilter : IAuthorizationFilter
{
    public const string HeaderName = "X-Admin-Key";

    private readonly IOptions<AdminOptions> adminOptions;

    public AdminKeyFilter(IOptions<AdminOptions> adminOptions)
    {
        this.adminOptions = adminOptions;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(this.adminOptions.Value.ApiKey));
        byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(context.HttpContext.Request.Headers[HeaderName].ToString()));
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, providedHash))
        {
            context.Result = new UnauthorizedResult();
        }
    }
}
