using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <summary>
    /// Private bookings: <c>Booking.IsPrivate</c>, and a fourth identity part on the two persisted
    /// arrival-group tables. No data moves on the way up — every existing booking and record is shared.
    ///
    /// <para>
    /// 🔴 Both unique indexes are deliberately <b>unfiltered</b> (<c>HasFilter(null)</c> in the configs).
    /// EF's SQL Server provider otherwise adds <c>WHERE [PrivateBookingId] IS NOT NULL</c> to a unique
    /// index over a nullable column, which would stop constraining the shared rows at all. Unfiltered,
    /// SQL Server compares NULLs as equal: one shared row per group, plus one per private booking.
    /// </para>
    /// </summary>
    public partial class AddPrivateBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BlockServiceStatuses_Destination_CheckIn_Category",
                table: "BlockServiceStatuses");

            migrationBuilder.DropIndex(
                name: "IX_BlockExpenseGroups_Expense_Destination_CheckIn_Category",
                table: "BlockExpenseGroups");

            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PrivateBookingId",
                table: "BlockServiceStatuses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrivateBookingId",
                table: "BlockExpenseGroups",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockServiceStatuses_Destination_CheckIn_Category_Private",
                table: "BlockServiceStatuses",
                columns: new[] { "DestinationId", "CheckInDate", "Category", "PrivateBookingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenseGroups_Expense_Destination_CheckIn_Category_Private",
                table: "BlockExpenseGroups",
                columns: new[] { "ExpenseId", "DestinationId", "CheckInDate", "Category", "PrivateBookingId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BlockServiceStatuses_Destination_CheckIn_Category_Private",
                table: "BlockServiceStatuses");

            migrationBuilder.DropIndex(
                name: "IX_BlockExpenseGroups_Expense_Destination_CheckIn_Category_Private",
                table: "BlockExpenseGroups");

            // Without the column, private files can't exist. Their status rows (records and document
            // rows cascade) are dropped — the booking rejoins its shared block, which is what un-marking
            // it private does anyway. Document files stay on disk; nothing deletes files in a migration.
            migrationBuilder.Sql("DELETE FROM [BlockServiceStatuses] WHERE [PrivateBookingId] IS NOT NULL;");

            // Expense money must not be lost: a private group with no shared sibling simply becomes the
            // shared group; one with a sibling has its lines folded into it and is then removed.
            migrationBuilder.Sql(@"
                UPDATE g SET [PrivateBookingId] = NULL
                FROM [BlockExpenseGroups] g
                WHERE g.[PrivateBookingId] IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM [BlockExpenseGroups] s
                                  WHERE s.[ExpenseId] = g.[ExpenseId] AND s.[DestinationId] = g.[DestinationId]
                                    AND s.[CheckInDate] = g.[CheckInDate] AND s.[Category] = g.[Category]
                                    AND s.[Id] <> g.[Id]
                                    AND (s.[PrivateBookingId] IS NULL OR s.[PrivateBookingId] < g.[PrivateBookingId]));");

            migrationBuilder.Sql(@"
                WITH target AS (
                    SELECT g.[Id] AS FromId,
                           (SELECT TOP 1 s.[Id] FROM [BlockExpenseGroups] s
                            WHERE s.[ExpenseId] = g.[ExpenseId] AND s.[DestinationId] = g.[DestinationId]
                              AND s.[CheckInDate] = g.[CheckInDate] AND s.[Category] = g.[Category]
                              AND s.[PrivateBookingId] IS NULL) AS ToId
                    FROM [BlockExpenseGroups] g
                    WHERE g.[PrivateBookingId] IS NOT NULL)
                UPDATE t SET
                    [Amount] = t.[Amount] + f.[Amount], [BookingCount] = t.[BookingCount] + f.[BookingCount],
                    [Adults] = t.[Adults] + f.[Adults], [Children] = t.[Children] + f.[Children],
                    [Infants] = t.[Infants] + f.[Infants], [EligiblePax] = t.[EligiblePax] + f.[EligiblePax],
                    [AppliedFingerprint] = CONCAT(t.[AppliedFingerprint], N';', f.[AppliedFingerprint])
                FROM [BlockExpenseGroups] t
                JOIN target x ON x.ToId = t.[Id]
                JOIN [BlockExpenseGroups] f ON f.[Id] = x.FromId;

                UPDATE l SET [ExpenseGroupId] = (
                    SELECT TOP 1 s.[Id] FROM [BlockExpenseGroups] s
                    WHERE s.[ExpenseId] = g.[ExpenseId] AND s.[DestinationId] = g.[DestinationId]
                      AND s.[CheckInDate] = g.[CheckInDate] AND s.[Category] = g.[Category]
                      AND s.[PrivateBookingId] IS NULL)
                FROM [BlockExpenseBookings] l
                JOIN [BlockExpenseGroups] g ON g.[Id] = l.[ExpenseGroupId]
                WHERE g.[PrivateBookingId] IS NOT NULL;

                DELETE FROM [BlockExpenseGroups] WHERE [PrivateBookingId] IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "IsPrivate",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PrivateBookingId",
                table: "BlockServiceStatuses");

            migrationBuilder.DropColumn(
                name: "PrivateBookingId",
                table: "BlockExpenseGroups");

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
    }
}
