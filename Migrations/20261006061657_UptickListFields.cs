using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UptickListFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Created",
                table: "Sites",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "ClientTasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ClientTasks",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientTasks_IsActive",
                table: "ClientTasks",
                column: "IsActive");

            // Records already imported: fill the new fields from their stored Uptick row
            migrationBuilder.Sql(@"
UPDATE ""ClientTasks""
SET ""Category"" = ""UptickData""::jsonb ->> 'Category',
    ""IsActive"" = coalesce((""UptickData""::jsonb ->> 'Is Active') = 'True', ""Status"" NOT IN (4, 5))
WHERE ""UptickData"" IS NOT NULL;");
            migrationBuilder.Sql(@"UPDATE ""ClientTasks"" SET ""IsActive"" = ""Status"" NOT IN (4, 5) WHERE ""UptickData"" IS NULL;");
            // Uptick times are Queensland local time
            migrationBuilder.Sql(@"
UPDATE ""Sites""
SET ""Created"" = (""UptickData""::jsonb ->> 'Created')::timestamp AT TIME ZONE 'Australia/Brisbane'
WHERE ""UptickData"" IS NOT NULL AND (""UptickData""::jsonb ->> 'Created') ~ '^\d{4}-\d{2}-\d{2}';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientTasks_IsActive",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "Created",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ClientTasks");
        }
    }
}
