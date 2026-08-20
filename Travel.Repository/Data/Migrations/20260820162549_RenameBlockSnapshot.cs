using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <summary>
    /// Renames the block snapshot to the business fact it now records: not only what the supplier
    /// <i>confirmed</i>, but what the supplier was <i>told</i> — a request while the block is In
    /// progress, a confirmation while it is Confirmed. Pure rename, no schema or data change.
    ///
    /// <para>
    /// 🔴 HAND-EDITED. The scaffolder emitted DropTable + CreateTable, which would have deleted every
    /// existing confirmation snapshot — and since a Confirmed block with no snapshot resolves to
    /// "Needs amendment", every confirmed block in the system would have flagged the moment this was
    /// applied, with nothing anywhere to say why. Renames only, in place.
    /// </para>
    /// </summary>
    public partial class RenameBlockSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "BlockConfirmedBookings",
                newName: "BlockRecordedBookings");

            migrationBuilder.RenameColumn(
                name: "ConfirmedAt",
                table: "BlockRecordedBookings",
                newName: "RecordedAt");

            migrationBuilder.RenameIndex(
                name: "IX_BlockConfirmedBookings_Block_Booking",
                table: "BlockRecordedBookings",
                newName: "IX_BlockRecordedBookings_Block_Booking");

            migrationBuilder.RenameIndex(
                name: "IX_BlockConfirmedBookings_BookingId",
                table: "BlockRecordedBookings",
                newName: "IX_BlockRecordedBookings_BookingId");

            // RenameTable does not touch constraint names, so the key and FK names would stay behind
            // and no longer match what EF generates from the model — a future DropForeignKey would
            // then target a name the database doesn't have.
            RenameConstraints(migrationBuilder, from: "BlockConfirmedBookings", to: "BlockRecordedBookings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RenameConstraints(migrationBuilder, from: "BlockRecordedBookings", to: "BlockConfirmedBookings");

            migrationBuilder.RenameIndex(
                name: "IX_BlockRecordedBookings_BookingId",
                table: "BlockRecordedBookings",
                newName: "IX_BlockConfirmedBookings_BookingId");

            migrationBuilder.RenameIndex(
                name: "IX_BlockRecordedBookings_Block_Booking",
                table: "BlockRecordedBookings",
                newName: "IX_BlockConfirmedBookings_Block_Booking");

            migrationBuilder.RenameColumn(
                name: "RecordedAt",
                table: "BlockRecordedBookings",
                newName: "ConfirmedAt");

            migrationBuilder.RenameTable(
                name: "BlockRecordedBookings",
                newName: "BlockConfirmedBookings");
        }

        private static void RenameConstraints(MigrationBuilder migrationBuilder, string from, string to)
        {
            foreach (var name in new[]
            {
                "PK_{0}",
                "FK_{0}_BlockServiceStatuses_BlockServiceStatusId",
                "FK_{0}_Bookings_BookingId",
            })
            {
                migrationBuilder.Sql(
                    $"EXEC sp_rename N'{string.Format(name, from)}', N'{string.Format(name, to)}', N'OBJECT';");
            }
        }
    }
}
