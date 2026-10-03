using System.Text;
using Microsoft.EntityFrameworkCore.SqlServer.Update.Internal;
using Microsoft.EntityFrameworkCore.Update;

namespace CP6.Core.Persistence;

// This narrow extension follows EF Core SQL Server 8.0.30's existing INSERT+SELECT
// fallback. Keep its native regression when upgrading the provider's internal API.
#pragma warning disable EF1001
public sealed class SqlServerIdentitySnapshotUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies)
    : SqlServerUpdateSqlGenerator(dependencies)
{
    public override ResultSetMapping AppendBulkInsertOperation(
        StringBuilder commandStringBuilder,
        IReadOnlyList<IReadOnlyModificationCommand> modificationCommands,
        int commandPosition,
        out bool requiresTransaction)
    {
        if (modificationCommands.Count >= MergeIntoMinimumThreshold)
        {
            var first = modificationCommands[0];
            if (first.TableName == "Snapshot" && first.Schema == "crm_identity" &&
                first.ColumnModifications.Any(column => column.IsRead) &&
                first.ColumnModifications.All(column => !column.IsKey || !column.IsRead))
            {
                // MERGE OUTPUT INTO declares string keys using the database's default
                // collation. Joining that table variable to the BIN2 aggregate key can
                // fail with SQL468. All keys here are supplied by the application, so
                // read generated values by those parameters after each INSERT/trigger.
                // Keep the whole batch atomic; generation is an opaque change boundary.
                requiresTransaction = true;
                foreach (var command in modificationCommands)
                    AppendInsertAndSelectOperation(commandStringBuilder, command, commandPosition++, out _);
                return ResultSetMapping.LastInResultSet;
            }
        }

        return base.AppendBulkInsertOperation(commandStringBuilder, modificationCommands, commandPosition, out requiresTransaction);
    }
}
#pragma warning restore EF1001
