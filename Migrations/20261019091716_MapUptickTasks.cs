using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class MapUptickTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceTasks_ClientTaskId_AssetId_Routine",
                table: "ServiceTasks");

            migrationBuilder.AlterColumn<int>(
                name: "AssetId",
                table: "ServiceTasks",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ServiceTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PerformedUtc",
                table: "ServiceTasks",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Result",
                table: "ServiceTasks",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoutineServiceLevelId",
                table: "ServiceTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TechnicianId",
                table: "ServiceTasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "ServiceTasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickProductId",
                table: "ServiceTasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickRemarkId",
                table: "ServiceTasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickRoutineId",
                table: "ServiceTasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeOfWorks",
                table: "ClientTasks",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UptickId",
                table: "ClientTasks",
                type: "bigint",
                nullable: true);

            // Tasks the file import made carry Uptick's id in ExternalId (so the mapper updates them) and their scope of
            // works in their export row
            migrationBuilder.Sql(@"
UPDATE ""ClientTasks"" SET ""UptickId"" = ""ExternalId""::bigint WHERE ""ExternalId"" ~ '^[0-9]{1,18}$';
UPDATE ""ClientTasks"" SET ""ScopeOfWorks"" = left(nullif(trim(""UptickData""->>'Scope of works'), ''), 4000)
WHERE ""UptickData"" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTasks_ClientTaskId_AssetId_Routine",
                table: "ServiceTasks",
                columns: new[] { "ClientTaskId", "AssetId", "Routine" },
                unique: true,
                filter: "\"UptickId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTasks_RoutineServiceLevelId",
                table: "ServiceTasks",
                column: "RoutineServiceLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTasks_TechnicianId",
                table: "ServiceTasks",
                column: "TechnicianId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTasks_UptickId",
                table: "ServiceTasks",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientTasks_UptickId",
                table: "ClientTasks",
                column: "UptickId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceTasks_RoutineServiceLevels_RoutineServiceLevelId",
                table: "ServiceTasks",
                column: "RoutineServiceLevelId",
                principalTable: "RoutineServiceLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceTasks_UptickUsers_TechnicianId",
                table: "ServiceTasks",
                column: "TechnicianId",
                principalTable: "UptickUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Uptick's service tasks can have no asset or repeat one, which the old column and index don't allow;
            // they come back from the raw copy with the next mapping
            migrationBuilder.Sql(@"DELETE FROM ""ServiceTasks"" WHERE ""UptickId"" IS NOT NULL OR ""AssetId"" IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceTasks_RoutineServiceLevels_RoutineServiceLevelId",
                table: "ServiceTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceTasks_UptickUsers_TechnicianId",
                table: "ServiceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ServiceTasks_ClientTaskId_AssetId_Routine",
                table: "ServiceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ServiceTasks_RoutineServiceLevelId",
                table: "ServiceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ServiceTasks_TechnicianId",
                table: "ServiceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ServiceTasks_UptickId",
                table: "ServiceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ClientTasks_UptickId",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ServiceTasks");

            migrationBuilder.DropColumn(
                name: "PerformedUtc",
                table: "ServiceTasks");

            migrationBuilder.DropColumn(
                name: "Result",
                table: "ServiceTasks");

            migrationBuilder.DropColumn(
                name: "RoutineServiceLevelId",
                table: "ServiceTasks");

            migrationBuilder.DropColumn(
                name: "TechnicianId",
                table: "ServiceTasks");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "ServiceTasks");

            migrationBuilder.DropColumn(
                name: "UptickProductId",
                table: "ServiceTasks");

            migrationBuilder.DropColumn(
                name: "UptickRemarkId",
                table: "ServiceTasks");

            migrationBuilder.DropColumn(
                name: "UptickRoutineId",
                table: "ServiceTasks");

            migrationBuilder.DropColumn(
                name: "ScopeOfWorks",
                table: "ClientTasks");

            migrationBuilder.DropColumn(
                name: "UptickId",
                table: "ClientTasks");

            migrationBuilder.AlterColumn<int>(
                name: "AssetId",
                table: "ServiceTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTasks_ClientTaskId_AssetId_Routine",
                table: "ServiceTasks",
                columns: new[] { "ClientTaskId", "AssetId", "Routine" },
                unique: true);
        }
    }
}
