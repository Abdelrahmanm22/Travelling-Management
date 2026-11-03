using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <summary>
    /// Domestic flight documents stop being bare files on an arrival group and become
    /// <c>BlockFlight</c> entries (number, date, time, files, the bookings on it); the "needs a
    /// domestic flight" rule moves from the booking to a stored per-stay switch.
    ///
    /// <para>
    /// Hand-edited in three places:
    /// <list type="bullet">
    /// <item>Every existing <c>BlockAttachments</c> row is DELETED first — the user's call: old files
    /// carry no flight facts and no bookings. That is also what lets the scaffolded column rename
    /// (BlockServiceStatusId → BlockFlightId) run with no row pointing at the wrong table. The files on
    /// disk sit in <c>wwwroot/Files/BlockAttachments/</c>, which nothing references afterwards (new
    /// uploads go to <c>BlockFlights/</c>), so that folder is deleted by hand on each server.</item>
    /// <item><c>BookingStays.RequiresDomesticFlight</c> is backfilled from the rule it replaces
    /// (<c>Booking.RequiresDomesticFlight</c>: a cruise stay or 2+ destinations), applied per stay and
    /// never to a booking's first stay — so every booking flagged before deploy is still flagged.</item>
    /// <item><c>Down</c> deletes the flight files' rows too before restoring the old FK. The files
    /// themselves cannot come back either way.</item>
    /// </list>
    /// </para>
    /// </summary>
    public partial class DomesticFlightsPerBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [BlockAttachments];");

            migrationBuilder.DropForeignKey(
                name: "FK_BlockAttachments_BlockServiceStatuses_BlockServiceStatusId",
                table: "BlockAttachments");

            migrationBuilder.RenameColumn(
                name: "BlockServiceStatusId",
                table: "BlockAttachments",
                newName: "BlockFlightId");

            migrationBuilder.RenameIndex(
                name: "IX_BlockAttachments_BlockServiceStatusId",
                table: "BlockAttachments",
                newName: "IX_BlockAttachments_BlockFlightId");

            migrationBuilder.AddColumn<bool>(
                name: "UsuallyReachedByDomesticFlight",
                table: "Destinations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresDomesticFlight",
                table: "BookingStays",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"
UPDATE s SET s.[RequiresDomesticFlight] = 1
FROM [BookingStays] s
WHERE s.[SortOrder] > (SELECT MIN(x.[SortOrder]) FROM [BookingStays] x WHERE x.[BookingId] = s.[BookingId])
  AND (EXISTS (SELECT 1 FROM [BookingStays] c WHERE c.[BookingId] = s.[BookingId] AND c.[IsCruise] = 1)
       OR (SELECT COUNT(DISTINCT d.[DestinationId]) FROM [BookingStays] d WHERE d.[BookingId] = s.[BookingId]) > 1);");

            migrationBuilder.CreateTable(
                name: "BlockFlights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlockServiceStatusId = table.Column<int>(type: "int", nullable: false),
                    FlightNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FlightDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FlightTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockFlights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockFlights_BlockServiceStatuses_BlockServiceStatusId",
                        column: x => x.BlockServiceStatusId,
                        principalTable: "BlockServiceStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockFlightBookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlockFlightId = table.Column<int>(type: "int", nullable: false),
                    BookingId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockFlightBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockFlightBookings_BlockFlights_BlockFlightId",
                        column: x => x.BlockFlightId,
                        principalTable: "BlockFlights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BlockFlightBookings_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockFlightBookings_BookingId",
                table: "BlockFlightBookings",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockFlightBookings_Flight_Booking",
                table: "BlockFlightBookings",
                columns: new[] { "BlockFlightId", "BookingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockFlights_BlockServiceStatusId",
                table: "BlockFlights",
                column: "BlockServiceStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_BlockAttachments_BlockFlights_BlockFlightId",
                table: "BlockAttachments",
                column: "BlockFlightId",
                principalTable: "BlockFlights",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BlockAttachments_BlockFlights_BlockFlightId",
                table: "BlockAttachments");

            // Their BlockFlightId values would otherwise land in BlockServiceStatusId and fail the FK.
            migrationBuilder.Sql("DELETE FROM [BlockAttachments];");

            migrationBuilder.DropTable(
                name: "BlockFlightBookings");

            migrationBuilder.DropTable(
                name: "BlockFlights");

            migrationBuilder.DropColumn(
                name: "UsuallyReachedByDomesticFlight",
                table: "Destinations");

            migrationBuilder.DropColumn(
                name: "RequiresDomesticFlight",
                table: "BookingStays");

            migrationBuilder.RenameColumn(
                name: "BlockFlightId",
                table: "BlockAttachments",
                newName: "BlockServiceStatusId");

            migrationBuilder.RenameIndex(
                name: "IX_BlockAttachments_BlockFlightId",
                table: "BlockAttachments",
                newName: "IX_BlockAttachments_BlockServiceStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_BlockAttachments_BlockServiceStatuses_BlockServiceStatusId",
                table: "BlockAttachments",
                column: "BlockServiceStatusId",
                principalTable: "BlockServiceStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
