using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataLayer.Context.Migrations
{
    /// <summary>
    /// Database setting, no schema change (so the model snapshot is unchanged). Read Committed
    /// Snapshot Isolation makes readers see the last committed row version instead of taking shared
    /// locks. List queries then never block, or deadlock with, status updates (see ANSWERS.md, Q17).
    /// ALTER DATABASE can't run inside a transaction, hence suppressTransaction. WITH ROLLBACK
    /// IMMEDIATE briefly drops other sessions, so schedule this for a quiet window in production.
    /// Azure SQL Database has RCSI on by default, so there it changes nothing.
    /// </summary>
    [DbContext(typeof(OrdersDbContext))]
    [Migration("20261116110000_EnableReadCommittedSnapshot")]
    public partial class EnableReadCommittedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);
        }
    }
}
