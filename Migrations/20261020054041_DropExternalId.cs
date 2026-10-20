using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class DropExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RaisedOnTaskId",
                table: "Remarks",
                type: "integer",
                nullable: true);

            // Before the columns go: an Uptick id or property ref only ExternalId holds moves to UptickId / Ref, and
            // links the exports made by Uptick id or task ref become foreign keys
            foreach (var table in UptickIdTables)
                migrationBuilder.Sql($@"
UPDATE ""{table}"" r SET ""UptickId"" = r.""ExternalId""::bigint
WHERE r.""UptickId"" IS NULL AND r.""ExternalId"" ~ '^[0-9]{{1,18}}$'
  AND NOT EXISTS (SELECT 1 FROM ""{table}"" x WHERE x.""UptickId""::text = r.""ExternalId"");");
            migrationBuilder.Sql(@"
UPDATE ""Properties"" SET ""Ref"" = ""ExternalId"" WHERE ""Ref"" IS NULL AND ""ExternalId"" IS NOT NULL;");

            migrationBuilder.Sql(@"
UPDATE ""Remarks"" r SET ""RaisedOnTaskId"" = t.""Id""
FROM (SELECT DISTINCT ON (""PropertyId"", ""Ref"") ""Id"", ""PropertyId"", ""Ref"" FROM ""ClientTasks""
      WHERE ""Ref"" IS NOT NULL ORDER BY ""PropertyId"", ""Ref"", ""Id"") t
WHERE r.""RaisedOnTaskRef"" = t.""Ref"" AND r.""PropertyId"" = t.""PropertyId"";");
            foreach (var (table, column, fk, target) in Links)
                migrationBuilder.Sql($@"
UPDATE ""{table}"" r SET ""{fk}"" = t.""Id""
FROM ""{target}"" t
WHERE r.""{fk}"" IS NULL AND t.""UptickId""::text = r.""{column}"";");

            // A record gets a FyreApp ref when it has no Uptick id (was: no ExternalId)
            migrationBuilder.Sql(AssignRef("UptickId"));

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_ExternalId",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_UptickUsers_ExternalId",
                table: "UptickUsers");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_ExternalId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_ServiceQuotes_ExternalId",
                table: "ServiceQuotes");

            migrationBuilder.DropIndex(
                name: "IX_SecurityGroups_ExternalId",
                table: "SecurityGroups");

            migrationBuilder.DropIndex(
                name: "IX_RoutineServiceTypes_ExternalId",
                table: "RoutineServiceTypes");

            migrationBuilder.DropIndex(
                name: "IX_RoutineServiceLevels_ExternalId",
                table: "RoutineServiceLevels");

            migrationBuilder.DropIndex(
                name: "IX_Routines_ExternalId",
                table: "Routines");

            migrationBuilder.DropIndex(
                name: "IX_Reports_ExternalId",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Reports_TaskExternalId",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_RemarkTypes_ExternalId",
                table: "RemarkTypes");

            migrationBuilder.DropIndex(
                name: "IX_Remarks_ExternalId",
                table: "Remarks");

            migrationBuilder.DropIndex(
                name: "IX_Remarks_RepairTaskExternalId",
                table: "Remarks");

            migrationBuilder.DropIndex(
                name: "IX_PropertyContacts_ExternalId",
                table: "PropertyContacts");

            migrationBuilder.DropIndex(
                name: "IX_Properties_ExternalId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_PromptQuestions_ExternalId",
                table: "PromptQuestions");

            migrationBuilder.DropIndex(
                name: "IX_Products_ExternalId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ExternalId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_TaskExternalId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_ExtraFields_ExternalId",
                table: "ExtraFields");

            migrationBuilder.DropIndex(
                name: "IX_DynamicFormResponses_ExternalId",
                table: "DynamicFormResponses");

            migrationBuilder.DropIndex(
                name: "IX_DefectQuotes_ExternalId",
                table: "DefectQuotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_ExternalId",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_InvoiceExternalId",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_Contractors_ExternalId",
                table: "Contractors");

            migrationBuilder.DropIndex(
                name: "IX_ClientTasks_ExternalId",
                table: "ClientTasks");

            migrationBuilder.DropIndex(
                name: "IX_Clients_ExternalId",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_ClientContacts_ExternalId",
                table: "ClientContacts");

            migrationBuilder.DropIndex(
                name: "IX_BillingContracts_ExternalId",
                table: "BillingContracts");

            migrationBuilder.DropIndex(
                name: "IX_BillingContractLineItems_ContractExternalId",
                table: "BillingContractLineItems");

            migrationBuilder.DropIndex(
                name: "IX_BillingContractLineItems_ExternalId",
                table: "BillingContractLineItems");

            migrationBuilder.DropIndex(
                name: "IX_BillingCards_ExternalId",
                table: "BillingCards");

            migrationBuilder.DropIndex(
                name: "IX_AssetTypeVariants_ExternalId",
                table: "AssetTypeVariants");

            migrationBuilder.DropIndex(
                name: "IX_AssetTypes_ExternalId",
                table: "AssetTypes");

            migrationBuilder.DropIndex(
                name: "IX_Assets_ExternalId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Accreditations_ExternalId",
                table: "Accreditations");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "UptickUsers");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "ServiceQuotes");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "SecurityGroups");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "RoutineServiceTypes");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "RoutineServiceLevels");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Routines");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "TaskExternalId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "RemarkTypes");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Remarks");

            migrationBuilder.DropColumn(
                name: "RaisedOnTaskRef",
                table: "Remarks");

            migrationBuilder.DropColumn(
                name: "RepairTaskExternalId",
                table: "Remarks");

            migrationBuilder.DropColumn(
                name: "PropertyExternalId",
                table: "PropertySchedules");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "PropertyContacts");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "PromptQuestions");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TaskExternalId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "ExtraFields");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "DynamicFormResponses");

            migrationBuilder.DropColumn(
                name: "SubmitterExternalId",
                table: "DynamicFormResponses");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "DefectQuotes");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "InvoiceExternalId",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Contractors");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "ClientContacts");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "BillingContracts");

            migrationBuilder.DropColumn(
                name: "ContractExternalId",
                table: "BillingContractLineItems");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "BillingContractLineItems");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "BillingCards");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "AssetTypeVariants");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "AssetTypes");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Accreditations");

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_RaisedOnTaskId",
                table: "Remarks",
                column: "RaisedOnTaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_Remarks_ClientTasks_RaisedOnTaskId",
                table: "Remarks",
                column: "RaisedOnTaskId",
                principalTable: "ClientTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        private static readonly string[] UptickIdTables =
        {
            "Accreditations", "AssetTypeVariants", "AssetTypes", "Assets", "BillingCards", "BillingContractLineItems",
            "BillingContracts", "ClientContacts", "ClientTasks", "Clients", "Contractors", "CreditNotes", "DefectQuotes",
            "DynamicFormResponses", "ExtraFields", "Invoices", "Products", "PromptQuestions", "PropertyContacts",
            "RemarkTypes", "Remarks", "Reports", "RoutineServiceLevels", "RoutineServiceTypes", "Routines",
            "SecurityGroups", "ServiceQuotes", "Suppliers", "UptickUsers", "Warehouses",
        };

        // (table, Uptick id column, foreign key, table it points at)
        private static readonly (string, string, string, string)[] Links =
        {
            ("Remarks", "RepairTaskExternalId", "RepairTaskId", "ClientTasks"),
            ("Reports", "TaskExternalId", "ClientTaskId", "ClientTasks"),
            ("Invoices", "TaskExternalId", "ClientTaskId", "ClientTasks"),
            ("BillingContractLineItems", "ContractExternalId", "BillingContractId", "BillingContracts"),
            ("CreditNotes", "InvoiceExternalId", "InvoiceId", "Invoices"),
            ("PropertySchedules", "PropertyExternalId", "PropertyId", "Properties"),
            ("DynamicFormResponses", "SubmitterExternalId", "UptickUserId", "UptickUsers"),
        };

        // Assigns "<prefix>-<next number>" on insert when the row has no Uptick id. The column is read via jsonb so
        // tables without it (maintenance schedules) work too.
        private static string AssignRef(string column) => $@"
CREATE OR REPLACE FUNCTION fyre_assign_ref() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.""FyreRef"" IS NULL AND coalesce(to_jsonb(NEW)->>'{column}', '') = '' THEN
        NEW.""FyreRef"" := TG_ARGV[0] || '-' || nextval(TG_ARGV[1]::regclass);
    END IF;
    RETURN NEW;
END;
$$;";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Remarks_ClientTasks_RaisedOnTaskId",
                table: "Remarks");

            migrationBuilder.DropIndex(
                name: "IX_Remarks_RaisedOnTaskId",
                table: "Remarks");

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Warehouses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "UptickUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Suppliers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "ServiceQuotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "SecurityGroups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "RoutineServiceTypes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "RoutineServiceLevels",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Routines",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Reports",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaskExternalId",
                table: "Reports",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "RemarkTypes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Remarks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RaisedOnTaskRef",
                table: "Remarks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepairTaskExternalId",
                table: "Remarks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PropertyExternalId",
                table: "PropertySchedules",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "PropertyContacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Properties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "PromptQuestions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaskExternalId",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "ExtraFields",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "DynamicFormResponses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmitterExternalId",
                table: "DynamicFormResponses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "DefectQuotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "CreditNotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceExternalId",
                table: "CreditNotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Contractors",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "ClientTasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Clients",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "ClientContacts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "BillingContracts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractExternalId",
                table: "BillingContractLineItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "BillingContractLineItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "BillingCards",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "AssetTypeVariants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "AssetTypes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Assets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Accreditations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(AssignRef("ExternalId"));
            foreach (var table in UptickIdTables)
                migrationBuilder.Sql($@"UPDATE ""{table}"" SET ""ExternalId"" = ""UptickId""::text WHERE ""UptickId"" IS NOT NULL;");
            migrationBuilder.Sql(@"UPDATE ""Properties"" SET ""ExternalId"" = ""Ref"" WHERE ""Ref"" IS NOT NULL;");
            migrationBuilder.Sql(@"
UPDATE ""Remarks"" r SET ""RaisedOnTaskRef"" = t.""Ref"" FROM ""ClientTasks"" t WHERE t.""Id"" = r.""RaisedOnTaskId"";");
            foreach (var (table, column, fk, target) in Links)
                migrationBuilder.Sql($@"
UPDATE ""{table}"" r SET ""{column}"" = t.""UptickId""::text FROM ""{target}"" t WHERE t.""Id"" = r.""{fk}"";");

            migrationBuilder.DropColumn(
                name: "RaisedOnTaskId",
                table: "Remarks");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_ExternalId",
                table: "Warehouses",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UptickUsers_ExternalId",
                table: "UptickUsers",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_ExternalId",
                table: "Suppliers",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuotes_ExternalId",
                table: "ServiceQuotes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityGroups_ExternalId",
                table: "SecurityGroups",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutineServiceTypes_ExternalId",
                table: "RoutineServiceTypes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutineServiceLevels_ExternalId",
                table: "RoutineServiceLevels",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Routines_ExternalId",
                table: "Routines",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ExternalId",
                table: "Reports",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_TaskExternalId",
                table: "Reports",
                column: "TaskExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_RemarkTypes_ExternalId",
                table: "RemarkTypes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_ExternalId",
                table: "Remarks",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Remarks_RepairTaskExternalId",
                table: "Remarks",
                column: "RepairTaskExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyContacts_ExternalId",
                table: "PropertyContacts",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Properties_ExternalId",
                table: "Properties",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptQuestions_ExternalId",
                table: "PromptQuestions",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_ExternalId",
                table: "Products",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ExternalId",
                table: "Invoices",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TaskExternalId",
                table: "Invoices",
                column: "TaskExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtraFields_ExternalId",
                table: "ExtraFields",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DynamicFormResponses_ExternalId",
                table: "DynamicFormResponses",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuotes_ExternalId",
                table: "DefectQuotes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_ExternalId",
                table: "CreditNotes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceExternalId",
                table: "CreditNotes",
                column: "InvoiceExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Contractors_ExternalId",
                table: "Contractors",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientTasks_ExternalId",
                table: "ClientTasks",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_ExternalId",
                table: "Clients",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientContacts_ExternalId",
                table: "ClientContacts",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingContracts_ExternalId",
                table: "BillingContracts",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingContractLineItems_ContractExternalId",
                table: "BillingContractLineItems",
                column: "ContractExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingContractLineItems_ExternalId",
                table: "BillingContractLineItems",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCards_ExternalId",
                table: "BillingCards",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypeVariants_ExternalId",
                table: "AssetTypeVariants",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypes_ExternalId",
                table: "AssetTypes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ExternalId",
                table: "Assets",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accreditations_ExternalId",
                table: "Accreditations",
                column: "ExternalId",
                unique: true);
        }
    }
}
