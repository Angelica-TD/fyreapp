using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePrimaryStreetAddressToPrimaryContactAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrimaryPostcode",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PrimaryState",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PrimaryStreetAddress",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PrimarySuburb",
                table: "Clients");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactAddress",
                table: "Clients",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrimaryContactAddress",
                table: "Clients");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryPostcode",
                table: "Clients",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryState",
                table: "Clients",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryStreetAddress",
                table: "Clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimarySuburb",
                table: "Clients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
