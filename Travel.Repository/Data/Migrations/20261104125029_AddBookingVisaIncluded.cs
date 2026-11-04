using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingVisaIncluded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VisaIncluded",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisaIncluded",
                table: "Bookings");
        }
    }
}
