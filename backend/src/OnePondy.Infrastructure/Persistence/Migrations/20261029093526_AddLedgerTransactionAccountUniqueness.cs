using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerTransactionAccountUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_transaction",
                table: "ledger_entries");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_entries_transaction_account",
                table: "ledger_entries",
                columns: new[] { "transaction_id", "account" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_ledger_entries_transaction_account",
                table: "ledger_entries");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transaction",
                table: "ledger_entries",
                column: "transaction_id");
        }
    }
}
