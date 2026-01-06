using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBillingAddressToOneField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingPostcode",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "BillingState",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "BillingStreetAddress",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "BillingSuburb",
                table: "Clients");

            migrationBuilder.AddColumn<string>(
                name: "BillingAddress",
                table: "Clients",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingAddress",
                table: "Clients");

            migrationBuilder.AddColumn<string>(
                name: "BillingPostcode",
                table: "Clients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingState",
                table: "Clients",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingStreetAddress",
                table: "Clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingSuburb",
                table: "Clients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
