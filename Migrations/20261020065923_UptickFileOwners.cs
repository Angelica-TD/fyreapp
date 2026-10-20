using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UptickFileOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Caption",
                table: "UptickFiles",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedUtc",
                table: "UptickFiles",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "UptickFiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotoType",
                table: "UptickFiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordType",
                table: "UptickFiles",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RecordUptickId",
                table: "UptickFiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UptickFiles_RecordType_RecordUptickId",
                table: "UptickFiles",
                columns: new[] { "RecordType", "RecordUptickId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UptickFiles_RecordType_RecordUptickId",
                table: "UptickFiles");

            migrationBuilder.DropColumn(
                name: "Caption",
                table: "UptickFiles");

            migrationBuilder.DropColumn(
                name: "CreatedUtc",
                table: "UptickFiles");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "UptickFiles");

            migrationBuilder.DropColumn(
                name: "PhotoType",
                table: "UptickFiles");

            migrationBuilder.DropColumn(
                name: "RecordType",
                table: "UptickFiles");

            migrationBuilder.DropColumn(
                name: "RecordUptickId",
                table: "UptickFiles");
        }
    }
}
