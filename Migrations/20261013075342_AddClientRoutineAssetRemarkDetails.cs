using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddClientRoutineAssetRemarkDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultTechnician",
                table: "RoutineOccurrences",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedMinutes",
                table: "RoutineOccurrences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subcontractor",
                table: "RoutineOccurrences",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InactiveUtc",
                table: "Defects",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuotedDate",
                table: "Defects",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingRequirements",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactAfterHours",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactPhone",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimarySector",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportAddress",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportAttentionTo",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportCcEmail",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportEmail",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportOrganisation",
                table: "Clients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InactiveDate",
                table: "Assets",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InspectionOrder",
                table: "Assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalNote",
                table: "Assets",
                type: "text",
                nullable: true);

            // Fill the new columns from the Uptick rows already stored on imported records. Re-imports skip
            // rows that haven't changed in Uptick, so they wouldn't fill them in. Uptick times are Brisbane time.
            migrationBuilder.Sql(@"
UPDATE ""Clients"" SET
    ""PrimaryContactPhone"" = ""UptickData""->>'Primary Contact BH',
    ""PrimaryContactAfterHours"" = ""UptickData""->>'Primary Contact AH',
    ""Reference"" = ""UptickData""->>'Reference',
    ""PrimarySector"" = ""UptickData""->>'Primary Sector',
    ""Notes"" = ""UptickData""->>'Notes',
    ""BillingRequirements"" = coalesce(""UptickData""->>'Billing Requirements', ""UptickData""->>'Billing Contact: Requirements'),
    ""ReportOrganisation"" = ""UptickData""->>'Report Contact: Organisation',
    ""ReportAttentionTo"" = ""UptickData""->>'Report Contact: Attention To',
    ""ReportAddress"" = ""UptickData""->>'Report Contact: Address',
    ""ReportEmail"" = ""UptickData""->>'Report Contact: Email To',
    ""ReportCcEmail"" = ""UptickData""->>'Report Contact: Email CC'
WHERE ""UptickData"" IS NOT NULL;");

            migrationBuilder.Sql(@"
UPDATE ""RoutineOccurrences"" SET
    ""DefaultTechnician"" = left(""UptickData""->>'Default Technician', 200),
    ""Subcontractor"" = left(""UptickData""->>'Subcontractor', 200),
    ""EstimatedMinutes"" = CASE WHEN ""UptickData""->>'Estimated Duration' ~ '^\d+:\d{2}:\d{2}$'
                               THEN nullif(round(extract(epoch from (""UptickData""->>'Estimated Duration')::interval) / 60)::int, 0) END
WHERE ""UptickData"" IS NOT NULL;");

            migrationBuilder.Sql(@"
UPDATE ""Assets"" SET
    ""InternalNote"" = ""UptickData""->>'Internal Note',
    ""InspectionOrder"" = CASE WHEN ""UptickData""->>'Inspection Order' ~ '^-?\d+$' THEN (""UptickData""->>'Inspection Order')::int END,
    ""InactiveDate"" = CASE WHEN ""UptickData""->>'Inactive Date' ~ '^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$'
                           THEN (""UptickData""->>'Inactive Date')::timestamp AT TIME ZONE 'Australia/Brisbane' END
WHERE ""UptickData"" IS NOT NULL;");

            migrationBuilder.Sql(@"
UPDATE ""Defects"" SET
    ""InactiveUtc"" = CASE WHEN ""UptickData""->>'Inactive Date' ~ '^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$'
                          THEN (""UptickData""->>'Inactive Date')::timestamp AT TIME ZONE 'Australia/Brisbane' END,
    ""QuotedDate"" = CASE WHEN ""UptickData""->>'Quoted Date' ~ '^\d{4}-\d{2}-\d{2}$'
                         THEN (""UptickData""->>'Quoted Date')::date::timestamp AT TIME ZONE 'UTC' END
WHERE ""UptickData"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultTechnician",
                table: "RoutineOccurrences");

            migrationBuilder.DropColumn(
                name: "EstimatedMinutes",
                table: "RoutineOccurrences");

            migrationBuilder.DropColumn(
                name: "Subcontractor",
                table: "RoutineOccurrences");

            migrationBuilder.DropColumn(
                name: "InactiveUtc",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "QuotedDate",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "BillingRequirements",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PrimaryContactAfterHours",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PrimaryContactPhone",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PrimarySector",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ReportAddress",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ReportAttentionTo",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ReportCcEmail",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ReportEmail",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ReportOrganisation",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "InactiveDate",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "InspectionOrder",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "InternalNote",
                table: "Assets");
        }
    }
}
