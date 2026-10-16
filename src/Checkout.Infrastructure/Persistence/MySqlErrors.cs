using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Checkout.Infrastructure.Persistence;

internal static class MySqlErrors
{
    // Error 1062 alone means "some unique index"; the index name says which one.
    // MySQL 8 message: "Duplicate entry '…' for key 'table.index_name'".
    public static bool IsDuplicateKey(DbUpdateException ex, string indexName) =>
        ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry } mysql
        && mysql.Message.Contains(indexName, StringComparison.Ordinal);
}
