using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceQuoteTypeAndToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientToken",
                table: "ServiceQuotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuoteType",
                table: "ServiceQuotes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentUtc",
                table: "ServiceQuotes",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceQuotes_ClientToken",
                table: "ServiceQuotes",
                column: "ClientToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceQuotes_ClientToken",
                table: "ServiceQuotes");

            migrationBuilder.DropColumn(
                name: "ClientToken",
                table: "ServiceQuotes");

            migrationBuilder.DropColumn(
                name: "QuoteType",
                table: "ServiceQuotes");

            migrationBuilder.DropColumn(
                name: "SentUtc",
                table: "ServiceQuotes");
        }
    }
}
