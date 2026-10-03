using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <summary>
    /// Makes the booking's category (4★ / 5★) part of an arrival group's identity, and splits every
    /// existing mixed group's persisted records in two.
    ///
    /// <para>
    /// 🔴 <b>Hand-edited.</b> The scaffolder emitted <c>defaultValue: 0</c> for both columns, and 0 is
    /// not a <c>PackageCategory</c> member (4 / 5) — every existing row would have keyed to a group
    /// that can never exist. The default is 4, and the data SQL below then moves what belongs to 5★.
    /// </para>
    ///
    /// <para>
    /// <b>Status rows</b> — a row whose live bookings are all 5★ flips to 5. A mixed one keeps 4 and
    /// gains a 5★ clone carrying the same status, email links and documents (the document rows share
    /// one file; <c>BlockServiceController.DeleteDocument</c> only removes a file nothing else points
    /// at). The supplier record moves to the half its booking belongs to, so neither half re-flags.
    /// </para>
    ///
    /// <para>
    /// <b>Expense groups</b> — split by the category of each allocation line's booking; a line whose
    /// booking was deleted (<c>BookingId</c> NULL) can't be placed and stays on the 4★ half. Money only
    /// moves between the two halves: every expense total, safe entry and line amount is unchanged. Each
    /// half's counts, room tally and applied fingerprint are rebuilt from its own lines, the
    /// fingerprint ordered with a BIN2 collation because <c>ExpenseFingerprint</c> sorts its segments
    /// with <c>StringComparer.Ordinal</c> — any other collation would produce a string that never
    /// matches, and every split group would read "changed" forever.
    /// </para>
    /// </summary>
    public partial class SplitBlocksByCategory : Migration
    {
        // Sibling = the other half of the same expense's same (destination, check-in).
        private const string HasSibling = @"EXISTS (SELECT 1 FROM [BlockExpenseGroups] s
                     WHERE s.[ExpenseId] = g.[ExpenseId] AND s.[DestinationId] = g.[DestinationId]
                       AND s.[CheckInDate] = g.[CheckInDate] AND s.[Id] <> g.[Id])";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BlockServiceStatuses_Destination_CheckIn",
                table: "BlockServiceStatuses");

            migrationBuilder.DropIndex(
                name: "IX_BlockExpenseGroups_Expense_Destination_CheckIn",
                table: "BlockExpenseGroups");

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "BlockServiceStatuses",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "BlockExpenseGroups",
                type: "int",
                nullable: false,
                defaultValue: 4);

            // ── Status rows ─────────────────────────────────────────────────

            // Which grades are live at each (destination, check-in) right now. Cancelled bookings are
            // out of Block Services entirely (BlockStaysSpec), so they don't count here either.
            const string present = @"WITH present AS (
                SELECT s.[DestinationId], s.[CheckInDate],
                       MAX(CASE WHEN b.[Category] = 4 THEN 1 ELSE 0 END) AS Has4,
                       MAX(CASE WHEN b.[Category] = 5 THEN 1 ELSE 0 END) AS Has5
                FROM [BookingStays] s
                JOIN [Bookings] b ON b.[Id] = s.[BookingId]
                WHERE b.[Status] <> 4
                GROUP BY s.[DestinationId], s.[CheckInDate])";

            migrationBuilder.Sql(present + @"
                UPDATE st SET [Category] = 5
                FROM [BlockServiceStatuses] st
                JOIN present p ON p.[DestinationId] = st.[DestinationId] AND p.[CheckInDate] = st.[CheckInDate]
                WHERE p.Has5 = 1 AND p.Has4 = 0;");

            migrationBuilder.Sql(present + @"
                INSERT INTO [BlockServiceStatuses]
                    ([DestinationId], [CheckInDate], [Category], [Status], [RequestEmailLink],
                     [ConfirmationEmailLink], [UpdatedAt], [UpdatedByUserId], [UpdatedByName])
                SELECT st.[DestinationId], st.[CheckInDate], 5, st.[Status], st.[RequestEmailLink],
                       st.[ConfirmationEmailLink], st.[UpdatedAt], st.[UpdatedByUserId], st.[UpdatedByName]
                FROM [BlockServiceStatuses] st
                JOIN present p ON p.[DestinationId] = st.[DestinationId] AND p.[CheckInDate] = st.[CheckInDate]
                WHERE p.Has4 = 1 AND p.Has5 = 1 AND st.[Category] = 4;");

            // A clone is the only 5★ row with a 4★ sibling, so this copies documents onto clones only.
            migrationBuilder.Sql(@"
                INSERT INTO [BlockAttachments]
                    ([BlockServiceStatusId], [FileUrl], [OriginalFileName], [UploadedAt], [UploadedByName])
                SELECT c.[Id], a.[FileUrl], a.[OriginalFileName], a.[UploadedAt], a.[UploadedByName]
                FROM [BlockServiceStatuses] c
                JOIN [BlockServiceStatuses] o ON o.[DestinationId] = c.[DestinationId]
                                             AND o.[CheckInDate] = c.[CheckInDate] AND o.[Category] = 4
                JOIN [BlockAttachments] a ON a.[BlockServiceStatusId] = o.[Id]
                WHERE c.[Category] = 5;");

            migrationBuilder.Sql(@"
                UPDATE r SET [BlockServiceStatusId] = c.[Id]
                FROM [BlockRecordedBookings] r
                JOIN [BlockServiceStatuses] o ON o.[Id] = r.[BlockServiceStatusId] AND o.[Category] = 4
                JOIN [BlockServiceStatuses] c ON c.[DestinationId] = o.[DestinationId]
                                             AND c.[CheckInDate] = o.[CheckInDate] AND c.[Category] = 5
                JOIN [Bookings] b ON b.[Id] = r.[BookingId]
                WHERE b.[Category] = 5;");

            // ── Expense groups ──────────────────────────────────────────────

            const string cats = @"WITH cats AS (
                SELECT l.[ExpenseGroupId],
                       MAX(CASE WHEN b.[Category] = 4 THEN 1 ELSE 0 END) AS Has4,
                       MAX(CASE WHEN b.[Category] = 5 THEN 1 ELSE 0 END) AS Has5
                FROM [BlockExpenseBookings] l
                JOIN [Bookings] b ON b.[Id] = l.[BookingId]
                GROUP BY l.[ExpenseGroupId])";

            migrationBuilder.Sql(cats + @"
                UPDATE g SET [Category] = 5
                FROM [BlockExpenseGroups] g
                JOIN cats c ON c.[ExpenseGroupId] = g.[Id]
                WHERE c.Has5 = 1 AND c.Has4 = 0;");

            // The clone starts with the full fingerprint; both halves are filtered down below.
            migrationBuilder.Sql(cats + @"
                INSERT INTO [BlockExpenseGroups]
                    ([ExpenseId], [DestinationId], [DestinationName], [CheckInDate], [Category], [Amount],
                     [BookingCount], [Adults], [Children], [Infants], [EligiblePax], [RoomsFingerprint],
                     [AppliedFingerprint])
                SELECT g.[ExpenseId], g.[DestinationId], g.[DestinationName], g.[CheckInDate], 5, 0,
                       0, 0, 0, 0, 0, N'', g.[AppliedFingerprint]
                FROM [BlockExpenseGroups] g
                JOIN cats c ON c.[ExpenseGroupId] = g.[Id]
                WHERE c.Has4 = 1 AND c.Has5 = 1;");

            migrationBuilder.Sql(@"
                UPDATE l SET [ExpenseGroupId] = c.[Id]
                FROM [BlockExpenseBookings] l
                JOIN [BlockExpenseGroups] o ON o.[Id] = l.[ExpenseGroupId] AND o.[Category] = 4
                JOIN [BlockExpenseGroups] c ON c.[ExpenseId] = o.[ExpenseId] AND c.[DestinationId] = o.[DestinationId]
                                           AND c.[CheckInDate] = o.[CheckInDate] AND c.[Category] = 5
                JOIN [Bookings] b ON b.[Id] = l.[BookingId]
                WHERE b.[Category] = 5;");

            // Each half's segments: the 5★ half keeps the bookings now on its lines; the 4★ half keeps
            // everything else, including a deleted booking's segment, which stayed with it.
            migrationBuilder.Sql(@"
                UPDATE g SET [AppliedFingerprint] = ISNULL(f.Fp, N'')
                FROM [BlockExpenseGroups] g
                OUTER APPLY (
                    SELECT STRING_AGG(p.[value], N';') WITHIN GROUP (ORDER BY p.[value] COLLATE Latin1_General_BIN2) AS Fp
                    FROM STRING_SPLIT(g.[AppliedFingerprint], N';') p
                    WHERE p.[value] <> N''
                      AND (CASE WHEN EXISTS (
                               SELECT 1 FROM [BlockExpenseBookings] l
                               JOIN [BlockExpenseGroups] h ON h.[Id] = l.[ExpenseGroupId]
                               WHERE h.[ExpenseId] = g.[ExpenseId] AND h.[DestinationId] = g.[DestinationId]
                                 AND h.[CheckInDate] = g.[CheckInDate] AND h.[Category] = 5
                                 AND l.[BookingId] = TRY_CAST(LEFT(p.[value], CHARINDEX(N':', p.[value]) - 1) AS int))
                           THEN 5 ELSE 4 END) = g.[Category]) f
                WHERE " + HasSibling + ";");

            migrationBuilder.Sql(RecomputeGroupsSql);

            migrationBuilder.CreateIndex(
                name: "IX_BlockServiceStatuses_Destination_CheckIn_Category",
                table: "BlockServiceStatuses",
                columns: new[] { "DestinationId", "CheckInDate", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenseGroups_Expense_Destination_CheckIn_Category",
                table: "BlockExpenseGroups",
                columns: new[] { "ExpenseId", "DestinationId", "CheckInDate", "Category" },
                unique: true);
        }

        /// <summary>
        /// Rebuilds a split group's snapshot columns from its own lines — the same sums
        /// <c>ExpenseCalculator.GroupAllocation</c> takes, and the room tally in
        /// <c>BlockSnapshot.RoomsFingerprint</c>'s "type x quantity" spelling, ordered by type.
        /// </summary>
        private const string RecomputeGroupsSql = @"
            UPDATE g SET
                [Amount] = x.Amount, [BookingCount] = x.Cnt, [Adults] = x.Adults, [Children] = x.Children,
                [Infants] = x.Infants, [EligiblePax] = x.EligiblePax, [RoomsFingerprint] = ISNULL(r.Fp, N'')
            FROM [BlockExpenseGroups] g
            CROSS APPLY (
                SELECT ISNULL(SUM(l.[Amount]), 0) AS Amount, COUNT(*) AS Cnt,
                       ISNULL(SUM(l.[Adults]), 0) AS Adults, ISNULL(SUM(l.[Children]), 0) AS Children,
                       ISNULL(SUM(l.[Infants]), 0) AS Infants, ISNULL(SUM(l.[EligiblePax]), 0) AS EligiblePax
                FROM [BlockExpenseBookings] l WHERE l.[ExpenseGroupId] = g.[Id]) x
            OUTER APPLY (
                SELECT STRING_AGG(CONCAT(t.RoomType, N'x', t.Qty), N'|') WITHIN GROUP (ORDER BY t.RoomType) AS Fp
                FROM (
                    SELECT CAST(LEFT(p.[value], CHARINDEX(N'x', p.[value]) - 1) AS int) AS RoomType,
                           SUM(CAST(SUBSTRING(p.[value], CHARINDEX(N'x', p.[value]) + 1, 10) AS int)) AS Qty
                    FROM [BlockExpenseBookings] l
                    CROSS APPLY STRING_SPLIT(l.[RoomsFingerprint], N'|') p
                    WHERE l.[ExpenseGroupId] = g.[Id] AND p.[value] <> N''
                    GROUP BY CAST(LEFT(p.[value], CHARINDEX(N'x', p.[value]) - 1) AS int)) t
                WHERE t.Qty > 0) r
            WHERE " + HasSibling + ";";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BlockServiceStatuses_Destination_CheckIn_Category",
                table: "BlockServiceStatuses");

            migrationBuilder.DropIndex(
                name: "IX_BlockExpenseGroups_Expense_Destination_CheckIn_Category",
                table: "BlockExpenseGroups");

            // Merge each 5★ half back into its 4★ sibling. A 5★ row with no sibling simply loses its
            // column below — it becomes the one row for that (destination, check-in) again.

            migrationBuilder.Sql(@"
                UPDATE r SET [BlockServiceStatusId] = o.[Id]
                FROM [BlockRecordedBookings] r
                JOIN [BlockServiceStatuses] c ON c.[Id] = r.[BlockServiceStatusId] AND c.[Category] = 5
                JOIN [BlockServiceStatuses] o ON o.[DestinationId] = c.[DestinationId]
                                             AND o.[CheckInDate] = c.[CheckInDate] AND o.[Category] = 4;");

            // Documents uploaded to one half only move across; copies the Up made are dropped.
            migrationBuilder.Sql(@"
                UPDATE a SET [BlockServiceStatusId] = o.[Id]
                FROM [BlockAttachments] a
                JOIN [BlockServiceStatuses] c ON c.[Id] = a.[BlockServiceStatusId] AND c.[Category] = 5
                JOIN [BlockServiceStatuses] o ON o.[DestinationId] = c.[DestinationId]
                                             AND o.[CheckInDate] = c.[CheckInDate] AND o.[Category] = 4
                WHERE NOT EXISTS (SELECT 1 FROM [BlockAttachments] x
                                  WHERE x.[BlockServiceStatusId] = o.[Id] AND x.[FileUrl] = a.[FileUrl]);");

            migrationBuilder.Sql(@"
                DELETE c FROM [BlockServiceStatuses] c
                WHERE c.[Category] = 5 AND EXISTS (
                    SELECT 1 FROM [BlockServiceStatuses] o
                    WHERE o.[DestinationId] = c.[DestinationId] AND o.[CheckInDate] = c.[CheckInDate]
                      AND o.[Category] = 4);");

            // Expense halves: union the fingerprints onto the 4★ row, move the lines, drop the clone,
            // then re-sum. Rows left without a sibling are skipped by the recompute's sibling guard,
            // so it runs while both halves still exist.
            migrationBuilder.Sql(@"
                UPDATE o SET [AppliedFingerprint] = ISNULL(f.Fp, N'')
                FROM [BlockExpenseGroups] o
                JOIN [BlockExpenseGroups] c ON c.[ExpenseId] = o.[ExpenseId] AND c.[DestinationId] = o.[DestinationId]
                                           AND c.[CheckInDate] = o.[CheckInDate] AND c.[Category] = 5
                OUTER APPLY (
                    SELECT STRING_AGG(p.[value], N';') WITHIN GROUP (ORDER BY p.[value] COLLATE Latin1_General_BIN2) AS Fp
                    FROM STRING_SPLIT(CONCAT(o.[AppliedFingerprint], N';', c.[AppliedFingerprint]), N';') p
                    WHERE p.[value] <> N'') f
                WHERE o.[Category] = 4;");

            migrationBuilder.Sql(@"
                UPDATE l SET [ExpenseGroupId] = o.[Id]
                FROM [BlockExpenseBookings] l
                JOIN [BlockExpenseGroups] c ON c.[Id] = l.[ExpenseGroupId] AND c.[Category] = 5
                JOIN [BlockExpenseGroups] o ON o.[ExpenseId] = c.[ExpenseId] AND o.[DestinationId] = c.[DestinationId]
                                           AND o.[CheckInDate] = c.[CheckInDate] AND o.[Category] = 4;");

            migrationBuilder.Sql(RecomputeGroupsSql);

            migrationBuilder.Sql(@"
                DELETE c FROM [BlockExpenseGroups] c
                WHERE c.[Category] = 5 AND EXISTS (
                    SELECT 1 FROM [BlockExpenseGroups] o
                    WHERE o.[ExpenseId] = c.[ExpenseId] AND o.[DestinationId] = c.[DestinationId]
                      AND o.[CheckInDate] = c.[CheckInDate] AND o.[Category] = 4);");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "BlockServiceStatuses");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "BlockExpenseGroups");

            migrationBuilder.CreateIndex(
                name: "IX_BlockServiceStatuses_Destination_CheckIn",
                table: "BlockServiceStatuses",
                columns: new[] { "DestinationId", "CheckInDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenseGroups_Expense_Destination_CheckIn",
                table: "BlockExpenseGroups",
                columns: new[] { "ExpenseId", "DestinationId", "CheckInDate" },
                unique: true);
        }
    }
}
