using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travel.Repository.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Packages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Bookings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Hand-added: EF's defaultValue "" would leave every existing row sharing one code and the
            // unique indexes below would fail. Row numbers are ordered by Id so the backfilled codes stay
            // monotonic with the IDENTITY PK — which is exactly what the generator relies on when it reads
            // the newest row to find the last issued number. No-op on an empty table.
            migrationBuilder.Sql(@"
                UPDATE p
                SET p.Code = 'MUSP-' + RIGHT('000000' + CAST(x.rn AS varchar(10)), 6)
                FROM Packages p
                JOIN (SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM Packages) x ON x.Id = p.Id;");

            migrationBuilder.Sql(@"
                UPDATE b
                SET b.Code = 'MUSB-' + RIGHT('000000' + CAST(x.rn AS varchar(10)), 6)
                FROM Bookings b
                JOIN (SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) AS rn FROM Bookings) x ON x.Id = b.Id;");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_Code",
                table: "Packages",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Code",
                table: "Bookings",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Packages_Code",
                table: "Packages");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Code",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Bookings");
        }
    }
}
