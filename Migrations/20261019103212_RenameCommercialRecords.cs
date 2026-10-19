using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <summary>
    /// Phase 5a (docs/uptick-migration.md §5.6): FyreApp's own proposals (ServiceQuote) become Proposal, so Uptick's
    /// sales quotes (SalesQuote) can take Uptick's name, ServiceQuote; StockLevel becomes Stock. Renamed, not rebuilt.
    /// The proposals go first: the sales quotes take the names they leave.
    /// </summary>
    public partial class RenameCommercialRecords : Migration
    {
        // (old table, new table, constraints and indexes as (old, new))
        private static readonly (string Old, string New, (string Old, string New)[] Constraints, (string Old, string New)[] Indexes)[] Tables =
        {
            ("ServiceQuotes", "Proposals",
                new[]
                {
                    ("PK_ServiceQuotes", "PK_Proposals"),
                    ("FK_ServiceQuotes_Clients_ClientId", "FK_Proposals_Clients_ClientId"),
                    ("FK_ServiceQuotes_MaintenanceIntervals_MaintenanceIntervalId", "FK_Proposals_MaintenanceIntervals_MaintenanceIntervalId"),
                    ("FK_ServiceQuotes_Properties_PropertyId", "FK_Proposals_Properties_PropertyId"),
                    ("FK_ServiceQuotes_ServiceOfferings_ServiceOfferingId", "FK_Proposals_ServiceOfferings_ServiceOfferingId"),
                },
                new[]
                {
                    ("IX_ServiceQuotes_ClientId", "IX_Proposals_ClientId"),
                    ("IX_ServiceQuotes_ClientToken", "IX_Proposals_ClientToken"),
                    ("IX_ServiceQuotes_MaintenanceIntervalId", "IX_Proposals_MaintenanceIntervalId"),
                    ("IX_ServiceQuotes_PropertyId", "IX_Proposals_PropertyId"),
                    ("IX_ServiceQuotes_QuoteNumber", "IX_Proposals_QuoteNumber"),
                    ("IX_ServiceQuotes_ServiceOfferingId", "IX_Proposals_ServiceOfferingId"),
                    ("IX_ServiceQuotes_Status", "IX_Proposals_Status"),
                }),
            ("SalesQuotes", "ServiceQuotes",
                new[] { ("PK_SalesQuotes", "PK_ServiceQuotes") },
                new[]
                {
                    ("IX_SalesQuotes_ExternalId", "IX_ServiceQuotes_ExternalId"),
                    ("IX_SalesQuotes_PropertyName", "IX_ServiceQuotes_PropertyName"),
                }),
            ("StockLevels", "Stocks",
                new[] { ("PK_StockLevels", "PK_Stocks") },
                new[] { ("IX_StockLevels_Product", "IX_Stocks_Product") }),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (from, to, constraints, indexes) in Tables)
                Rename(migrationBuilder, from, to, constraints, indexes);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back in the opposite order: the sales quotes leave the ServiceQuotes names first
            foreach (var (from, to, constraints, indexes) in Tables.Reverse())
                Rename(migrationBuilder, to, from,
                    constraints.Select(c => (c.New, c.Old)).ToArray(), indexes.Select(i => (i.New, i.Old)).ToArray());
        }

        private static void Rename(MigrationBuilder migrationBuilder, string from, string to,
            (string Old, string New)[] constraints, (string Old, string New)[] indexes)
        {
            migrationBuilder.RenameTable(name: from, newName: to);
            foreach (var (oldName, newName) in constraints)
                migrationBuilder.Sql($"""ALTER TABLE "{to}" RENAME CONSTRAINT "{oldName}" TO "{newName}";""");
            foreach (var (oldName, newName) in indexes)
                migrationBuilder.Sql($"""ALTER INDEX "{oldName}" RENAME TO "{newName}";""");
            migrationBuilder.Sql($"""ALTER SEQUENCE "{from}_Id_seq" RENAME TO "{to}_Id_seq";""");
        }
    }
}
