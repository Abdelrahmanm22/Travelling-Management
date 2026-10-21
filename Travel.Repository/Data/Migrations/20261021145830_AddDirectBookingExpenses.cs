using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectBookingExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Target",
                table: "BlockExpenses",
                type: "int",
                nullable: false,
                // Hand-edited from the scaffolded 0, which is not an ExpenseTarget member (1/2). Every
                // expense recorded before this column existed was applied to arrival groups.
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "BlockExpenseDirectBookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpenseId = table.Column<int>(type: "int", nullable: false),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    BookingCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LeadGuestName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Nights = table.Column<int>(type: "int", nullable: false),
                    EligiblePax = table.Column<int>(type: "int", nullable: false),
                    Adults = table.Column<int>(type: "int", nullable: false),
                    Children = table.Column<int>(type: "int", nullable: false),
                    Infants = table.Column<int>(type: "int", nullable: false),
                    RoomsFingerprint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AppliedFingerprint = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockExpenseDirectBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockExpenseDirectBookings_BlockExpenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "BlockExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BlockExpenseDirectBookings_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenseDirectBookings_BookingId",
                table: "BlockExpenseDirectBookings",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenseDirectBookings_Expense_Booking",
                table: "BlockExpenseDirectBookings",
                columns: new[] { "ExpenseId", "BookingId" },
                unique: true,
                filter: "[BookingId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockExpenseDirectBookings");

            migrationBuilder.DropColumn(
                name: "Target",
                table: "BlockExpenses");
        }
    }
}
