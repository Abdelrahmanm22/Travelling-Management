using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTourGuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingTourGuests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingTourId = table.Column<int>(type: "int", nullable: false),
                    GuestId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingTourGuests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingTourGuests_BookingGuests_GuestId",
                        column: x => x.GuestId,
                        principalTable: "BookingGuests",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BookingTourGuests_BookingTours_BookingTourId",
                        column: x => x.BookingTourId,
                        principalTable: "BookingTours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingTourGuests_GuestId",
                table: "BookingTourGuests",
                column: "GuestId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingTourGuests_Tour_Guest",
                table: "BookingTourGuests",
                columns: new[] { "BookingTourId", "GuestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingTourGuests");
        }
    }
}
