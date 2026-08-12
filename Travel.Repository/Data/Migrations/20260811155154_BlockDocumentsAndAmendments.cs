using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <inheritdoc />
    public partial class BlockDocumentsAndAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlockServiceStatusId = table.Column<int>(type: "int", nullable: false),
                    FileUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockAttachments_BlockServiceStatuses_BlockServiceStatusId",
                        column: x => x.BlockServiceStatusId,
                        principalTable: "BlockServiceStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockConfirmedBookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlockServiceStatusId = table.Column<int>(type: "int", nullable: false),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    Adults = table.Column<int>(type: "int", nullable: false),
                    Children = table.Column<int>(type: "int", nullable: false),
                    Infants = table.Column<int>(type: "int", nullable: false),
                    Nights = table.Column<int>(type: "int", nullable: false),
                    RoomsFingerprint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockConfirmedBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockConfirmedBookings_BlockServiceStatuses_BlockServiceStatusId",
                        column: x => x.BlockServiceStatusId,
                        principalTable: "BlockServiceStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BlockConfirmedBookings_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockAttachments_BlockServiceStatusId",
                table: "BlockAttachments",
                column: "BlockServiceStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockConfirmedBookings_Block_Booking",
                table: "BlockConfirmedBookings",
                columns: new[] { "BlockServiceStatusId", "BookingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockConfirmedBookings_BookingId",
                table: "BlockConfirmedBookings",
                column: "BookingId");

            // Hand-added. Domestic flight documents moved from the booking to the arrival group, and
            // the old per-booking rows are deleted rather than migrated: guessing which leg of a trip
            // a file belonged to would have fanned a one-booking document out to everyone sharing that
            // arrival. Category 3 was AttachmentCategory.DomesticFlight and is now retired.
            //
            // This removes the rows only — the files under wwwroot/Files/BookingAttachments are left
            // orphaned and must be deleted by hand. Capture "SELECT FileUrl FROM BookingAttachment
            // WHERE Category = 3" BEFORE applying this; afterwards there is no way to tell those files
            // apart from the Flight/Passport ones. (Table is singular: BookingAttachment has no DbSet.)
            migrationBuilder.Sql("DELETE FROM BookingAttachment WHERE Category = 3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockAttachments");

            migrationBuilder.DropTable(
                name: "BlockConfirmedBookings");
        }
    }
}
