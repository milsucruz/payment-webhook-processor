using Microsoft.Extensions.Configuration;

namespace Simulator;

public static class Settings
{
    public static string Require(IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not set. Use dotnet user-secrets in tools/Simulator or the environment variable {key.Replace(":", "__")}.");
        }

        return value;
    }
}
