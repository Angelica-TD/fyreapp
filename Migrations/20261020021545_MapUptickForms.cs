using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FormResponse -> DynamicFormResponse (Uptick's name for it): the imported rows are kept
            migrationBuilder.RenameTable(name: "FormResponses", newName: "DynamicFormResponses");
            migrationBuilder.Sql(@"ALTER TABLE ""DynamicFormResponses"" RENAME CONSTRAINT ""PK_FormResponses"" TO ""PK_DynamicFormResponses"";");
            migrationBuilder.RenameIndex(name: "IX_FormResponses_ExternalId", table: "DynamicFormResponses", newName: "IX_DynamicFormResponses_ExternalId");
            migrationBuilder.RenameIndex(name: "IX_FormResponses_TargetContentType_TargetObjectId", table: "DynamicFormResponses",
                newName: "IX_DynamicFormResponses_TargetContentType_TargetObjectId");

            migrationBuilder.AddColumn<long>(name: "UptickId", table: "DynamicFormResponses", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<int>(name: "ClientTaskId", table: "DynamicFormResponses", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "UptickUserId", table: "DynamicFormResponses", type: "integer", nullable: true);
            migrationBuilder.AddColumn<string>(name: "TemplateName", table: "DynamicFormResponses", type: "character varying(200)", maxLength: 200, nullable: true);

            migrationBuilder.AddColumn<string>(name: "Location", table: "PropertySchedules", type: "character varying(300)", maxLength: 300, nullable: true);
            migrationBuilder.AddColumn<int>(name: "PropertyId", table: "PropertySchedules", type: "integer", nullable: true);
            migrationBuilder.AddColumn<long>(name: "UptickId", table: "PropertySchedules", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<long>(name: "UptickId", table: "PromptQuestions", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<long>(name: "UptickId", table: "ExtraFields", type: "bigint", nullable: true);

            // Rows the exports placed (ExternalId = Uptick's id) are the API's records
            foreach (var table in new[] { "ExtraFields", "PromptQuestions", "DynamicFormResponses" })
                migrationBuilder.Sql($@"
UPDATE ""{table}"" SET ""UptickId"" = ""ExternalId""::bigint
WHERE ""ExternalId"" ~ '^[0-9]{{1,18}}$';");

            // Link what the exports named by Uptick id or ref
            migrationBuilder.Sql(@"
UPDATE ""PropertySchedules"" s SET ""PropertyId"" = p.""Id""
FROM ""Properties"" p
WHERE p.""UptickId""::text = s.""PropertyExternalId"" OR p.""ExternalId"" = s.""PropertyRef"";");
            migrationBuilder.Sql(@"
UPDATE ""DynamicFormResponses"" f SET ""ClientTaskId"" = t.""Id""
FROM ""ClientTasks"" t
WHERE f.""TargetContentType"" = '28' AND (t.""UptickId""::text = f.""TargetObjectId"" OR t.""ExternalId"" = f.""TargetObjectId"");");
            migrationBuilder.Sql(@"
UPDATE ""DynamicFormResponses"" f SET ""UptickUserId"" = u.""Id""
FROM ""UptickUsers"" u
WHERE u.""UptickId""::text = f.""SubmitterExternalId"" OR u.""ExternalId"" = f.""SubmitterExternalId"";");

            migrationBuilder.CreateIndex(name: "IX_PropertySchedules_PropertyId", table: "PropertySchedules", column: "PropertyId");
            migrationBuilder.CreateIndex(name: "IX_PropertySchedules_UptickId", table: "PropertySchedules", column: "UptickId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_PromptQuestions_UptickId", table: "PromptQuestions", column: "UptickId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_ExtraFields_UptickId", table: "ExtraFields", column: "UptickId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_DynamicFormResponses_ClientTaskId", table: "DynamicFormResponses", column: "ClientTaskId");
            migrationBuilder.CreateIndex(name: "IX_DynamicFormResponses_UptickId", table: "DynamicFormResponses", column: "UptickId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_DynamicFormResponses_UptickUserId", table: "DynamicFormResponses", column: "UptickUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_DynamicFormResponses_ClientTasks_ClientTaskId",
                table: "DynamicFormResponses",
                column: "ClientTaskId",
                principalTable: "ClientTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(
                name: "FK_DynamicFormResponses_UptickUsers_UptickUserId",
                table: "DynamicFormResponses",
                column: "UptickUserId",
                principalTable: "UptickUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(
                name: "FK_PropertySchedules_Properties_PropertyId",
                table: "PropertySchedules",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_PropertySchedules_Properties_PropertyId", table: "PropertySchedules");
            migrationBuilder.DropForeignKey(name: "FK_DynamicFormResponses_UptickUsers_UptickUserId", table: "DynamicFormResponses");
            migrationBuilder.DropForeignKey(name: "FK_DynamicFormResponses_ClientTasks_ClientTaskId", table: "DynamicFormResponses");

            migrationBuilder.DropIndex(name: "IX_DynamicFormResponses_UptickUserId", table: "DynamicFormResponses");
            migrationBuilder.DropIndex(name: "IX_DynamicFormResponses_UptickId", table: "DynamicFormResponses");
            migrationBuilder.DropIndex(name: "IX_DynamicFormResponses_ClientTaskId", table: "DynamicFormResponses");
            migrationBuilder.DropIndex(name: "IX_ExtraFields_UptickId", table: "ExtraFields");
            migrationBuilder.DropIndex(name: "IX_PromptQuestions_UptickId", table: "PromptQuestions");
            migrationBuilder.DropIndex(name: "IX_PropertySchedules_UptickId", table: "PropertySchedules");
            migrationBuilder.DropIndex(name: "IX_PropertySchedules_PropertyId", table: "PropertySchedules");

            migrationBuilder.DropColumn(name: "UptickId", table: "ExtraFields");
            migrationBuilder.DropColumn(name: "UptickId", table: "PromptQuestions");
            migrationBuilder.DropColumn(name: "UptickId", table: "PropertySchedules");
            migrationBuilder.DropColumn(name: "PropertyId", table: "PropertySchedules");
            migrationBuilder.DropColumn(name: "Location", table: "PropertySchedules");
            migrationBuilder.DropColumn(name: "TemplateName", table: "DynamicFormResponses");
            migrationBuilder.DropColumn(name: "UptickUserId", table: "DynamicFormResponses");
            migrationBuilder.DropColumn(name: "ClientTaskId", table: "DynamicFormResponses");
            migrationBuilder.DropColumn(name: "UptickId", table: "DynamicFormResponses");

            migrationBuilder.RenameIndex(name: "IX_DynamicFormResponses_TargetContentType_TargetObjectId", table: "DynamicFormResponses",
                newName: "IX_FormResponses_TargetContentType_TargetObjectId");
            migrationBuilder.RenameIndex(name: "IX_DynamicFormResponses_ExternalId", table: "DynamicFormResponses", newName: "IX_FormResponses_ExternalId");
            migrationBuilder.Sql(@"ALTER TABLE ""DynamicFormResponses"" RENAME CONSTRAINT ""PK_DynamicFormResponses"" TO ""PK_FormResponses"";");
            migrationBuilder.RenameTable(name: "DynamicFormResponses", newName: "FormResponses");
        }
    }
}
