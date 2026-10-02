using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStayRoomRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HAND-EDITED. The scaffolder put the DropColumn first, which destroys real money figures
            // with nothing left to recover them from. Extra nights are now priced per room type, so
            // the old flat rate cannot be converted automatically — a flat 120 means something
            // different depending on how many rooms stayed on, and guessing would restate settled
            // bookings. Those rows become unpriced (deliberate, and the reason Booking.IsPriced now
            // covers extra nights: they read "—" rather than a wrong number), but the figures are
            // kept here so the operator re-pricing them has the old number to work from.
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[BookingStayLegacyRates]', N'U') IS NULL
                    SELECT [Id] AS [StayId], [ExtraNightPrice]
                    INTO [BookingStayLegacyRates]
                    FROM [BookingStays]
                    WHERE [ExtraNightPrice] IS NOT NULL;");

            // defaultValue: false is correct, and was checked rather than assumed — every extra night
            // taken before this feature existed WAS booked through us.
            migrationBuilder.AddColumn<bool>(
                name: "ExtraNightsArrangedByGuest",
                table: "BookingStays",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BookingStayRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StayId = table.Column<int>(type: "int", nullable: false),
                    RoomType = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingStayRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingStayRates_BookingStays_StayId",
                        column: x => x.StayId,
                        principalTable: "BookingStays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingStayRates_Stay_RoomType",
                table: "BookingStayRates",
                columns: new[] { "StayId", "RoomType" },
                unique: true);

            // Last, once the figures above are safely copied.
            migrationBuilder.DropColumn(
                name: "ExtraNightPrice",
                table: "BookingStays");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingStayRates");

            migrationBuilder.DropColumn(
                name: "ExtraNightsArrangedByGuest",
                table: "BookingStays");

            migrationBuilder.AddColumn<decimal>(
                name: "ExtraNightPrice",
                table: "BookingStays",
                type: "decimal(18,2)",
                nullable: true);

            // Restore from the backup Up() took, so this migration is genuinely reversible rather
            // than reversible-shaped. The table is left in place: dropping it would make a second
            // Up() silently find nothing to preserve.
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'[BookingStayLegacyRates]', N'U') IS NOT NULL
                    UPDATE s SET s.[ExtraNightPrice] = l.[ExtraNightPrice]
                    FROM [BookingStays] s
                    INNER JOIN [BookingStayLegacyRates] l ON l.[StayId] = s.[Id];");
        }
    }
}
