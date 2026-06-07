using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionDataAnalysisPlatform.Migrations
{
    /// <inheritdoc />
    public partial class AddStoresAndExternalIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_ProductCode",
                table: "Products");

            migrationBuilder.AddColumn<int>(
                name: "ExternalOrderItemId",
                table: "Sales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "Sales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExternalProductId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreConnectionId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExternalInventoryId",
                table: "Inventories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreConnectionId",
                table: "Inventories",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StoreId",
                table: "Inventories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    StoreId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreConnectionId = table.Column<int>(type: "int", nullable: false),
                    ExternalStoreId = table.Column<int>(type: "int", nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.StoreId);
                    table.ForeignKey(
                        name: "FK_Stores_StoreConnections_StoreConnectionId",
                        column: x => x.StoreConnectionId,
                        principalTable: "StoreConnections",
                        principalColumn: "StoreConnectionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_StoreId",
                table: "Sales",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreConnectionId_StoreId_ProductCode",
                table: "Products",
                columns: new[] { "StoreConnectionId", "StoreId", "ProductCode" },
                unique: true,
                filter: "[StoreConnectionId] IS NOT NULL AND [StoreId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_StoreId",
                table: "Products",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_StoreConnectionId",
                table: "Inventories",
                column: "StoreConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Inventories_StoreId",
                table: "Inventories",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_StoreConnectionId",
                table: "Stores",
                column: "StoreConnectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Inventories_StoreConnections_StoreConnectionId",
                table: "Inventories",
                column: "StoreConnectionId",
                principalTable: "StoreConnections",
                principalColumn: "StoreConnectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Inventories_Stores_StoreId",
                table: "Inventories",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_StoreConnections_StoreConnectionId",
                table: "Products",
                column: "StoreConnectionId",
                principalTable: "StoreConnections",
                principalColumn: "StoreConnectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Stores_StoreId",
                table: "Products",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Stores_StoreId",
                table: "Sales",
                column: "StoreId",
                principalTable: "Stores",
                principalColumn: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Inventories_StoreConnections_StoreConnectionId",
                table: "Inventories");

            migrationBuilder.DropForeignKey(
                name: "FK_Inventories_Stores_StoreId",
                table: "Inventories");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_StoreConnections_StoreConnectionId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Stores_StoreId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Stores_StoreId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropIndex(
                name: "IX_Sales_StoreId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Products_StoreConnectionId_StoreId_ProductCode",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_StoreId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Inventories_StoreConnectionId",
                table: "Inventories");

            migrationBuilder.DropIndex(
                name: "IX_Inventories_StoreId",
                table: "Inventories");

            migrationBuilder.DropColumn(
                name: "ExternalOrderItemId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ExternalProductId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StoreConnectionId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ExternalInventoryId",
                table: "Inventories");

            migrationBuilder.DropColumn(
                name: "StoreConnectionId",
                table: "Inventories");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "Inventories");

            migrationBuilder.CreateIndex(
                name: "IX_Products_ProductCode",
                table: "Products",
                column: "ProductCode",
                unique: true);
        }
    }
}
