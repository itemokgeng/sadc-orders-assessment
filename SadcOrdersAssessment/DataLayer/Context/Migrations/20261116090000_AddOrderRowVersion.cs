using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Context.Migrations
{
    /// <summary>
    /// Evolution 1, additive only, so code from before this migration keeps working:
    /// - RowVersion (rowversion) for optimistic concurrency. SQL Server fills it in for existing
    ///   rows, so NOT NULL is safe without a backfill.
    /// - UpdatedAt, an optional column. Old code never writes it, which is fine.
    /// </summary>
    [DbContext(typeof(OrdersDbContext))]
    [Migration("20261116090000_AddOrderRowVersion")]
    public partial class AddOrderRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Orders",
                type: "rowversion",
                rowVersion: true,
                nullable: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Orders");
        }
    }
}
