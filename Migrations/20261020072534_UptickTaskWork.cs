using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FyreApp.Migrations
{
    /// <inheritdoc />
    public partial class UptickTaskWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    ClientTaskId = table.Column<int>(type: "integer", nullable: true),
                    StartUtc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appointments_ClientTasks_ClientTaskId",
                        column: x => x.ClientTaskId,
                        principalTable: "ClientTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subtasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    ClientTaskId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    RoutineId = table.Column<int>(type: "integer", nullable: true),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    IsPerformed = table.Column<bool>(type: "boolean", nullable: false),
                    PerformedDate = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    IsServiceTask = table.Column<bool>(type: "boolean", nullable: false),
                    IsReportTask = table.Column<bool>(type: "boolean", nullable: false),
                    IsBillingTask = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subtasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subtasks_ClientTasks_ClientTaskId",
                        column: x => x.ClientTaskId,
                        principalTable: "ClientTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Subtasks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Subtasks_Routines_RoutineId",
                        column: x => x.RoutineId,
                        principalTable: "Routines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TaskSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    ClientTaskId = table.Column<int>(type: "integer", nullable: false),
                    UptickUserId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsOnsite = table.Column<bool>(type: "boolean", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    FinishedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    Hours = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    Cost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Rate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsSubmitted = table.Column<bool>(type: "boolean", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    StartedLatitude = table.Column<double>(type: "double precision", nullable: true),
                    StartedLongitude = table.Column<double>(type: "double precision", nullable: true),
                    FinishedLatitude = table.Column<double>(type: "double precision", nullable: true),
                    FinishedLongitude = table.Column<double>(type: "double precision", nullable: true),
                    TrashedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskSessions_ClientTasks_ClientTaskId",
                        column: x => x.ClientTaskId,
                        principalTable: "ClientTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskSessions_UptickUsers_UptickUserId",
                        column: x => x.UptickUserId,
                        principalTable: "UptickUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TaskStockTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UptickId = table.Column<long>(type: "bigint", nullable: true),
                    ClientTaskId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    OtherType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OtherUptickId = table.Column<long>(type: "bigint", nullable: true),
                    CostPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    UptickUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskStockTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskStockTransactions_ClientTasks_ClientTaskId",
                        column: x => x.ClientTaskId,
                        principalTable: "ClientTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskStockTransactions_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TaskStockTransactions_UptickUsers_UptickUserId",
                        column: x => x.UptickUserId,
                        principalTable: "UptickUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentTechnicians",
                columns: table => new
                {
                    AppointmentId = table.Column<int>(type: "integer", nullable: false),
                    TechniciansId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentTechnicians", x => new { x.AppointmentId, x.TechniciansId });
                    table.ForeignKey(
                        name: "FK_AppointmentTechnicians_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppointmentTechnicians_UptickUsers_TechniciansId",
                        column: x => x.TechniciansId,
                        principalTable: "UptickUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClientTaskId",
                table: "Appointments",
                column: "ClientTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_UptickId",
                table: "Appointments",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTechnicians_TechniciansId",
                table: "AppointmentTechnicians",
                column: "TechniciansId");

            migrationBuilder.CreateIndex(
                name: "IX_Subtasks_ClientTaskId",
                table: "Subtasks",
                column: "ClientTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Subtasks_ProductId",
                table: "Subtasks",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Subtasks_RoutineId",
                table: "Subtasks",
                column: "RoutineId");

            migrationBuilder.CreateIndex(
                name: "IX_Subtasks_UptickId",
                table: "Subtasks",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSessions_ClientTaskId",
                table: "TaskSessions",
                column: "ClientTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSessions_UptickId",
                table: "TaskSessions",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSessions_UptickUserId",
                table: "TaskSessions",
                column: "UptickUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskStockTransactions_ClientTaskId",
                table: "TaskStockTransactions",
                column: "ClientTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskStockTransactions_ProductId",
                table: "TaskStockTransactions",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskStockTransactions_UptickId",
                table: "TaskStockTransactions",
                column: "UptickId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskStockTransactions_UptickUserId",
                table: "TaskStockTransactions",
                column: "UptickUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentTechnicians");

            migrationBuilder.DropTable(
                name: "Subtasks");

            migrationBuilder.DropTable(
                name: "TaskSessions");

            migrationBuilder.DropTable(
                name: "TaskStockTransactions");

            migrationBuilder.DropTable(
                name: "Appointments");
        }
    }
}
