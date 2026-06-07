using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionDataAnalysisPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExternalOrderId",
                table: "Sales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalProductCode",
                table: "Sales",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreConnectionId",
                table: "Sales",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoreConnections",
                columns: table => new
                {
                    StoreConnectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StoreApiUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreConnections", x => x.StoreConnectionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_StoreConnectionId",
                table: "Sales",
                column: "StoreConnectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_StoreConnections_StoreConnectionId",
                table: "Sales",
                column: "StoreConnectionId",
                principalTable: "StoreConnections",
                principalColumn: "StoreConnectionId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_StoreConnections_StoreConnectionId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "StoreConnections");

            migrationBuilder.DropIndex(
                name: "IX_Sales_StoreConnectionId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ExternalOrderId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ExternalProductCode",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "StoreConnectionId",
                table: "Sales");
        }
    }
}
