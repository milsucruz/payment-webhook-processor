using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Payments.Api.Authorization;

// Details: docs/code/RequireAdminKeyAttribute.md
public sealed class RequireAdminKeyAttribute : TypeFilterAttribute
{
    public const string HeaderName = "X-Admin-Key";

    public RequireAdminKeyAttribute()
        : base(typeof(AdminKeyFilter))
    {
    }

    private sealed class AdminKeyFilter : IAuthorizationFilter
    {
        private readonly byte[] expectedKeyHash;

        public AdminKeyFilter(IOptions<AdminOptions> options)
        {
            this.expectedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.ApiKey));
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            byte[] providedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(context.HttpContext.Request.Headers[HeaderName].ToString()));
            if (!CryptographicOperations.FixedTimeEquals(providedKeyHash, this.expectedKeyHash))
            {
                context.Result = new UnauthorizedResult();
            }
        }
    }
}
