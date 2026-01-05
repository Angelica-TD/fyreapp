using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddMoreFieldsToClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Clients",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingAttentionTo",
                table: "Clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCcEmail",
                table: "Clients",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingEmail",
                table: "Clients",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingName",
                table: "Clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

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

            migrationBuilder.AddColumn<DateTime>(
                name: "Created",
                table: "Clients",
                type: "timestamptz",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactCcEmail",
                table: "Clients",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactEmail",
                table: "Clients",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactMobile",
                table: "Clients",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryContactName",
                table: "Clients",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

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

            migrationBuilder.AddColumn<DateTime>(
                name: "Updated",
                table: "Clients",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Active",
                table: "Clients",
                column: "Active");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_BillingEmail",
                table: "Clients",
                column: "BillingEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_PrimaryContactEmail",
                table: "Clients",
                column: "PrimaryContactEmail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clients_Active",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Clients_BillingEmail",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Clients_PrimaryContactEmail",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "BillingAttentionTo",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "BillingCcEmail",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "BillingEmail",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "BillingName",
                table: "Clients");

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

            migrationBuilder.DropColumn(
                name: "Created",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PrimaryContactCcEmail",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PrimaryContactEmail",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PrimaryContactMobile",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PrimaryContactName",
                table: "Clients");

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

            migrationBuilder.DropColumn(
                name: "Updated",
                table: "Clients");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Clients",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
