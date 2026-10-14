using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddUptickBillingQuotesStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accreditations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Number = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Expiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Technician = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PropertyRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accreditations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BusinessNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PostalAttention = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostalEmail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PostalMobile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PostalBusinessHours = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PostalAddress = table.Column<string>(type: "text", nullable: true),
                    SiteAddress = table.Column<string>(type: "text", nullable: true),
                    NumberOfInvoices = table.Column<int>(type: "integer", nullable: true),
                    AccountingOrganisation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DefaultTaxCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreditLimit = table.Column<decimal>(type: "numeric", nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingContractLineItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ContractExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Product = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RoutineType = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LevelType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Visible = table.Column<bool>(type: "boolean", nullable: false),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingContractLineItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingContracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Frequency = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InvoiceMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PropertyRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PropertyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ClientName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BillingCardName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextDue = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeriodBegin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AnnualValue = table.Column<decimal>(type: "numeric", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingContracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    InvoiceExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InvoiceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PropertyRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DefectQuotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Reference = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PropertyRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PropertyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ClientName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AuthorisedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ScopeOfWorks = table.Column<string>(type: "text", nullable: true),
                    Severity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ServiceGroup = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric", nullable: true),
                    Tax = table.Column<decimal>(type: "numeric", nullable: true),
                    Total = table.Column<decimal>(type: "numeric", nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectQuotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Ref = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SubmittedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmitterExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TemplateVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TargetObjectId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InvoiceRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    PropertyRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PropertyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ClientName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BillingCardName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TaskExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TaskRef = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric", nullable: true),
                    Tax = table.Column<decimal>(type: "numeric", nullable: true),
                    Total = table.Column<decimal>(type: "numeric", nullable: true),
                    CreditTotal = table.Column<decimal>(type: "numeric", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    UnitDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Supplier = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Subcategory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CostPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    DefaultPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Stocked = table.Column<bool>(type: "boolean", nullable: false),
                    Starred = table.Column<bool>(type: "boolean", nullable: false),
                    LastUsedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PropertySchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PropertyExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PropertyRef = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PropertyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Issuer = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertySchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesQuotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Ref = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PropertyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    ClientName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ScopeOfWorksHtml = table.Column<string>(type: "text", nullable: true),
                    Template = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Subtotal = table.Column<decimal>(type: "numeric", nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesQuotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Warehouse = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Product = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SupplierRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InternalRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Supplier = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    OnHand = table.Column<decimal>(type: "numeric", nullable: true),
                    Available = table.Column<decimal>(type: "numeric", nullable: true),
                    Reserved = table.Column<decimal>(type: "numeric", nullable: true),
                    OnOrder = table.Column<decimal>(type: "numeric", nullable: true),
                    RestockLevel = table.Column<decimal>(type: "numeric", nullable: true),
                    Required = table.Column<decimal>(type: "numeric", nullable: true),
                    BinLocation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StockValue = table.Column<decimal>(type: "numeric", nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLevels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Manager = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StockValue = table.Column<decimal>(type: "numeric", nullable: true),
                    UptickData = table.Column<string>(type: "json", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accreditations_ExternalId",
                table: "Accreditations",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCards_ExternalId",
                table: "BillingCards",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingContractLineItems_ContractExternalId",
                table: "BillingContractLineItems",
                column: "ContractExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingContractLineItems_ExternalId",
                table: "BillingContractLineItems",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingContracts_ExternalId",
                table: "BillingContracts",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingContracts_PropertyRef",
                table: "BillingContracts",
                column: "PropertyRef");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_ExternalId",
                table: "CreditNotes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceExternalId",
                table: "CreditNotes",
                column: "InvoiceExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuotes_ExternalId",
                table: "DefectQuotes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuotes_PropertyRef",
                table: "DefectQuotes",
                column: "PropertyRef");

            migrationBuilder.CreateIndex(
                name: "IX_DefectQuotes_Reference",
                table: "DefectQuotes",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_FormResponses_ExternalId",
                table: "FormResponses",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormResponses_TargetContentType_TargetObjectId",
                table: "FormResponses",
                columns: new[] { "TargetContentType", "TargetObjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ExternalId",
                table: "Invoices",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PropertyRef",
                table: "Invoices",
                column: "PropertyRef");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TaskExternalId",
                table: "Invoices",
                column: "TaskExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_ExternalId",
                table: "Products",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertySchedules_PropertyRef",
                table: "PropertySchedules",
                column: "PropertyRef");

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_ExternalId",
                table: "SalesQuotes",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesQuotes_PropertyName",
                table: "SalesQuotes",
                column: "PropertyName");

            migrationBuilder.CreateIndex(
                name: "IX_StockLevels_Product",
                table: "StockLevels",
                column: "Product");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_ExternalId",
                table: "Warehouses",
                column: "ExternalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accreditations");

            migrationBuilder.DropTable(
                name: "BillingCards");

            migrationBuilder.DropTable(
                name: "BillingContractLineItems");

            migrationBuilder.DropTable(
                name: "BillingContracts");

            migrationBuilder.DropTable(
                name: "CreditNotes");

            migrationBuilder.DropTable(
                name: "DefectQuotes");

            migrationBuilder.DropTable(
                name: "FormResponses");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "PropertySchedules");

            migrationBuilder.DropTable(
                name: "SalesQuotes");

            migrationBuilder.DropTable(
                name: "StockLevels");

            migrationBuilder.DropTable(
                name: "Warehouses");
        }
    }
}
