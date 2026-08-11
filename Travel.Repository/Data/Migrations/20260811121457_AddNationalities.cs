using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNationalities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "Bookings");

            migrationBuilder.AddColumn<int>(
                name: "NationalityId",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Nationalities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nationalities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_NationalityId",
                table: "Bookings",
                column: "NationalityId");

            migrationBuilder.CreateIndex(
                name: "IX_Nationalities_Name",
                table: "Nationalities",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Nationalities_NationalityId",
                table: "Bookings",
                column: "NationalityId",
                principalTable: "Nationalities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Nationalities_NationalityId",
                table: "Bookings");

            migrationBuilder.DropTable(
                name: "Nationalities");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_NationalityId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NationalityId",
                table: "Bookings");

            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "Bookings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
