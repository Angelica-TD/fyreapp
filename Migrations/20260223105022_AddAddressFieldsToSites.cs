using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddAddressFieldsToSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressDisplay",
                table: "Sites",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "Sites",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "Sites",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GooglePlaceId",
                table: "Sites",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Postcode",
                table: "Sites",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Sites",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Suburb",
                table: "Sites",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressDisplay",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "GooglePlaceId",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Postcode",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Suburb",
                table: "Sites");
        }
    }
}
