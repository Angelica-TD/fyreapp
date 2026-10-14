using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddContractorsSuppliersUptickUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contractors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PrimaryContact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EmailCc = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Phone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Mobile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Tags = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Skills = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ServiceAreas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Starred = table.Column<bool>(type: "boolean", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contractors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BusinessNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PaymentTerms = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentDay = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AccountsContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AccountsEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PurchasingContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PurchasingEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AccountingOrganisation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UptickUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LicenseType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SecurityGroups = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ServiceGroup = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HomeBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AllowedBranches = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CostRate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    SellRate = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Licence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    JoinedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    LastLoginUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UptickUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contractors_ExternalId",
                table: "Contractors",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_ExternalId",
                table: "Suppliers",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UptickUsers_ExternalId",
                table: "UptickUsers",
                column: "ExternalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Contractors");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "UptickUsers");
        }
    }
}
