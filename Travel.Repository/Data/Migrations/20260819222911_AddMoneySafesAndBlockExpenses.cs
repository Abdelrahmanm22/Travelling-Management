using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMoneySafesAndBlockExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpenseTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    IncludeChildren = table.Column<bool>(type: "bit", nullable: false),
                    IncludeInfants = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MoneySafes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoneySafes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlockExpenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpenseTypeId = table.Column<int>(type: "int", nullable: false),
                    ExpenseTypeName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    IncludeChildren = table.Column<bool>(type: "bit", nullable: false),
                    IncludeInfants = table.Column<bool>(type: "bit", nullable: false),
                    SpentOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecalculatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockExpenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockExpenses_ExpenseTypes_ExpenseTypeId",
                        column: x => x.ExpenseTypeId,
                        principalTable: "ExpenseTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BlockExpenseGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpenseId = table.Column<int>(type: "int", nullable: false),
                    DestinationId = table.Column<int>(type: "int", nullable: false),
                    DestinationName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CheckInDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BookingCount = table.Column<int>(type: "int", nullable: false),
                    Adults = table.Column<int>(type: "int", nullable: false),
                    Children = table.Column<int>(type: "int", nullable: false),
                    Infants = table.Column<int>(type: "int", nullable: false),
                    EligiblePax = table.Column<int>(type: "int", nullable: false),
                    RoomsFingerprint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AppliedFingerprint = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockExpenseGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockExpenseGroups_BlockExpenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "BlockExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BlockExpenseGroups_Destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "Destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BlockExpenseRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpenseId = table.Column<int>(type: "int", nullable: false),
                    RoomType = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockExpenseRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockExpenseRates_BlockExpenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "BlockExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SafeEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SafeId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpenseId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafeEntries_BlockExpenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "BlockExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SafeEntries_MoneySafes_SafeId",
                        column: x => x.SafeId,
                        principalTable: "MoneySafes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockExpenseBookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpenseGroupId = table.Column<int>(type: "int", nullable: false),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    BookingCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LeadGuestName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Nights = table.Column<int>(type: "int", nullable: false),
                    EligiblePax = table.Column<int>(type: "int", nullable: false),
                    Adults = table.Column<int>(type: "int", nullable: false),
                    Children = table.Column<int>(type: "int", nullable: false),
                    Infants = table.Column<int>(type: "int", nullable: false),
                    RoomsFingerprint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockExpenseBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockExpenseBookings_BlockExpenseGroups_ExpenseGroupId",
                        column: x => x.ExpenseGroupId,
                        principalTable: "BlockExpenseGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BlockExpenseBookings_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SafeEntryAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntryId = table.Column<int>(type: "int", nullable: false),
                    FileUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafeEntryAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafeEntryAttachments_SafeEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "SafeEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenseBookings_BookingId",
                table: "BlockExpenseBookings",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenseBookings_Group_Booking",
                table: "BlockExpenseBookings",
                columns: new[] { "ExpenseGroupId", "BookingId" },
                unique: true,
                filter: "[BookingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenseGroups_DestinationId",
                table: "BlockExpenseGroups",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenseGroups_Expense_Destination_CheckIn",
                table: "BlockExpenseGroups",
                columns: new[] { "ExpenseId", "DestinationId", "CheckInDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenseRates_Expense_RoomType",
                table: "BlockExpenseRates",
                columns: new[] { "ExpenseId", "RoomType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenses_ExpenseTypeId",
                table: "BlockExpenses",
                column: "ExpenseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockExpenses_SpentOn",
                table: "BlockExpenses",
                column: "SpentOn");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseTypes_Name",
                table: "ExpenseTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MoneySafes_Name",
                table: "MoneySafes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SafeEntries_Expense",
                table: "SafeEntries",
                column: "ExpenseId",
                unique: true,
                filter: "[ExpenseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SafeEntries_Safe_OccurredOn",
                table: "SafeEntries",
                columns: new[] { "SafeId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_SafeEntryAttachments_EntryId",
                table: "SafeEntryAttachments",
                column: "EntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockExpenseBookings");

            migrationBuilder.DropTable(
                name: "BlockExpenseRates");

            migrationBuilder.DropTable(
                name: "SafeEntryAttachments");

            migrationBuilder.DropTable(
                name: "BlockExpenseGroups");

            migrationBuilder.DropTable(
                name: "SafeEntries");

            migrationBuilder.DropTable(
                name: "BlockExpenses");

            migrationBuilder.DropTable(
                name: "MoneySafes");

            migrationBuilder.DropTable(
                name: "ExpenseTypes");
        }
    }
}
