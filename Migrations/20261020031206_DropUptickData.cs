using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class DropUptickData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PerformedDate",
                table: "ClientTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledDate",
                table: "ClientTasks",
                type: "timestamp with time zone",
                nullable: true);

            // What only the export rows hold, carried over before they go: a task's scheduled and performed dates, a
            // remark's type, an asset type's applicable routines and excluded levels
            migrationBuilder.Sql(@"
UPDATE ""ClientTasks"" SET
    ""ScheduledDate"" = CASE WHEN ""UptickData""->>'Scheduled Date' ~ '^\d{4}-\d{2}-\d{2}'
        THEN left(""UptickData""->>'Scheduled Date', 10)::timestamp AT TIME ZONE 'UTC' END,
    ""PerformedDate"" = CASE WHEN ""UptickData""->>'Performed Date' ~ '^\d{4}-\d{2}-\d{2}'
        THEN left(""UptickData""->>'Performed Date', 10)::timestamp AT TIME ZONE 'UTC' END
WHERE ""UptickData"" IS NOT NULL;");
            migrationBuilder.Sql(@"
UPDATE ""Remarks"" r SET ""RemarkTypeId"" = t.""Id""
FROM ""RemarkTypes"" t
WHERE r.""RemarkTypeId"" IS NULL AND r.""UptickData"" IS NOT NULL
  AND t.""ExternalId"" = trim(r.""UptickData""->>'Remark Type ID');");
            migrationBuilder.Sql(@"
INSERT INTO ""AssetTypeApplicableRoutines"" (""ApplicableRoutineTypesId"", ""AssetTypeId"")
SELECT DISTINCT r.""Id"", a.""Id""
FROM ""AssetTypes"" a
CROSS JOIN LATERAL regexp_split_to_table(a.""UptickData""->>'Applicable Routines', '\r?\n') AS line
JOIN ""RoutineServiceTypes"" r ON lower(r.""Name"") = lower(trim(line))
WHERE a.""UptickData"" IS NOT NULL
ON CONFLICT DO NOTHING;");
            migrationBuilder.Sql(@"
INSERT INTO ""AssetTypeExcludedRoutineLevels"" (""AssetTypeId"", ""ExcludedRoutineLevelsId"")
SELECT DISTINCT a.""Id"", l.""Id""
FROM ""AssetTypes"" a
CROSS JOIN LATERAL regexp_split_to_table(a.""UptickData""->>'Excluded Routine Levels', '\r?\n') AS line
JOIN ""RoutineServiceLevels"" l ON TRUE
JOIN ""RoutineServiceTypes"" r ON r.""Id"" = l.""RoutineServiceTypeId""
WHERE a.""UptickData"" IS NOT NULL AND lower(r.""Name"" || ': ' || l.""Name"") = lower(trim(line))
ON CONFLICT DO NOTHING;");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "UptickUsers");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Stocks");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "ServiceQuotes");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "RoutineServiceTypes");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "RoutineServiceLevels");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Routines");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "RemarkTypes");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Remarks");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "PropertySchedules");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "PropertyContacts");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "PromptQuestions");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "ExtraFields");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "DynamicFormResponses");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "DefectQuotes");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "ClientContacts");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "BillingContracts");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "BillingContractLineItems");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "BillingCards");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "AssetTypeVariants");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "AssetTypes");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "UptickData",
                table: "Accreditations");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PerformedDate",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "ScheduledDate",
                table: "ClientTasks");

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Warehouses",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "UptickUsers",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Suppliers",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Stocks",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "ServiceQuotes",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "RoutineServiceTypes",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "RoutineServiceLevels",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Routines",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Reports",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "RemarkTypes",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Remarks",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "PropertySchedules",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "PropertyContacts",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Properties",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "PromptQuestions",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Products",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Invoices",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "ExtraFields",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "DynamicFormResponses",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "DefectQuotes",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "CreditNotes",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Contractors",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "ClientTasks",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Clients",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "ClientContacts",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "BillingContracts",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "BillingContractLineItems",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "BillingCards",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "AssetTypeVariants",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "AssetTypes",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Assets",
                type: "json",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UptickData",
                table: "Accreditations",
                type: "json",
                nullable: true);
        }
    }
}
