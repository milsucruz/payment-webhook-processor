using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using Payments.Application.Processing;

namespace Payments.Infrastructure.Persistence;

// Details: docs/code/SqlTransientErrorDetector.md
public sealed class SqlTransientErrorDetector : ITransientErrorDetector
{
    private static readonly HashSet<int> transientErrorNumbers =
    [
        -2, 20, 64, 233, 258, 1205, 4060, 4221, 10053, 10054, 10060, 10928, 10929,
        40143, 40197, 40501, 40540, 40613, 42108, 42109, 49918, 49919, 49920,
    ];

    public bool IsTransient(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                return sqlException.InnerException is SocketException
                    || sqlException.Errors.Cast<SqlError>().Any(error => IsTransientErrorNumber(error.Number));
            }

            if (current is TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsTransientErrorNumber(int number)
    {
        return transientErrorNumbers.Contains(number);
    }
}
