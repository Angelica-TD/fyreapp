using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UptickCorrespondence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Correspondence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    Subject = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    From = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FromName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    To = table.Column<string>(type: "text", nullable: true),
                    Cc = table.Column<string>(type: "text", nullable: true),
                    Bcc = table.Column<string>(type: "text", nullable: true),
                    ReplyTo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SentUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    SentById = table.Column<int>(type: "integer", nullable: true),
                    IsStarred = table.Column<bool>(type: "boolean", nullable: false),
                    BodyHtml = table.Column<string>(type: "text", nullable: true),
                    Attachments = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Correspondence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Correspondence_UptickUsers_SentById",
                        column: x => x.SentById,
                        principalTable: "UptickUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Dispatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    Ref = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ClientTaskId = table.Column<int>(type: "integer", nullable: true),
                    PropertyId = table.Column<int>(type: "integer", nullable: true),
                    InvoiceId = table.Column<int>(type: "integer", nullable: true),
                    SenderId = table.Column<int>(type: "integer", nullable: true),
                    IssueInvoice = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    Emails = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dispatches_ClientTasks_ClientTaskId",
                        column: x => x.ClientTaskId,
                        principalTable: "ClientTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Dispatches_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Dispatches_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Dispatches_UptickUsers_SenderId",
                        column: x => x.SenderId,
                        principalTable: "UptickUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CorrespondenceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    CorrespondenceId = table.Column<int>(type: "integer", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecordUptickId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondenceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CorrespondenceLinks_Correspondence_CorrespondenceId",
                        column: x => x.CorrespondenceId,
                        principalTable: "Correspondence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Correspondence_SentById",
                table: "Correspondence",
                column: "SentById");

            migrationBuilder.CreateIndex(
                name: "IX_Correspondence_SentUtc",
                table: "Correspondence",
                column: "SentUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Correspondence_UptickId",
                table: "Correspondence",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondenceLinks_CorrespondenceId",
                table: "CorrespondenceLinks",
                column: "CorrespondenceId");

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondenceLinks_RecordType_RecordUptickId",
                table: "CorrespondenceLinks",
                columns: new[] { "RecordType", "RecordUptickId" });

            migrationBuilder.CreateIndex(
                name: "IX_CorrespondenceLinks_UptickId",
                table: "CorrespondenceLinks",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_ClientTaskId",
                table: "Dispatches",
                column: "ClientTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_InvoiceId",
                table: "Dispatches",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_PropertyId",
                table: "Dispatches",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_SenderId",
                table: "Dispatches",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_UptickId",
                table: "Dispatches",
                column: "UptickId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrespondenceLinks");

            migrationBuilder.DropTable(
                name: "Dispatches");

            migrationBuilder.DropTable(
                name: "Correspondence");
        }
    }
}
