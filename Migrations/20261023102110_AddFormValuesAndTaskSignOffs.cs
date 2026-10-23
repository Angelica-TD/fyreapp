using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class AddFormValuesAndTaskSignOffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Values",
                table: "DynamicFormResponses",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskSignOffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientTaskId = table.Column<int>(type: "integer", nullable: false),
                    SigneeName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    NoSignatureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SignatureFileId = table.Column<int>(type: "integer", nullable: true),
                    SignedUtc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UptickUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSignOffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskSignOffs_ClientTasks_ClientTaskId",
                        column: x => x.ClientTaskId,
                        principalTable: "ClientTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskSignOffs_UptickFiles_SignatureFileId",
                        column: x => x.SignatureFileId,
                        principalTable: "UptickFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TaskSignOffs_UptickUsers_UptickUserId",
                        column: x => x.UptickUserId,
                        principalTable: "UptickUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskSignOffs_ClientTaskId",
                table: "TaskSignOffs",
                column: "ClientTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSignOffs_SignatureFileId",
                table: "TaskSignOffs",
                column: "SignatureFileId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSignOffs_UptickUserId",
                table: "TaskSignOffs",
                column: "UptickUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskSignOffs");

            migrationBuilder.DropColumn(
                name: "Values",
                table: "DynamicFormResponses");
        }
    }
}
