using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KipuPOS.Api.Migrations
{
    /// <inheritdoc />
    public partial class MesasYPagoMixto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TableName",
                table: "Sales",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TableOrderId",
                table: "Sales",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiningTables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiningTables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiningTables_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiningTables_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalePayments_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalePayments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TableOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TableOrders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableOrders_DiningTables_TableId",
                        column: x => x.TableId,
                        principalTable: "DiningTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableOrders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableOrders_Users_OpenedById",
                        column: x => x.OpenedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TableOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TableOrderLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableOrderLines_TableOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "TableOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TableOrderLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_TableOrderId",
                table: "Sales",
                column: "TableOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DiningTables_BranchId_Name",
                table: "DiningTables",
                columns: new[] { "BranchId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiningTables_TenantId",
                table: "DiningTables",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalePayments_SaleId_Method",
                table: "SalePayments",
                columns: new[] { "SaleId", "Method" });

            migrationBuilder.CreateIndex(
                name: "IX_SalePayments_TenantId",
                table: "SalePayments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TableOrderLines_OrderId",
                table: "TableOrderLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TableOrderLines_ProductId",
                table: "TableOrderLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TableOrderLines_TenantId",
                table: "TableOrderLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TableOrders_BranchId_Status",
                table: "TableOrders",
                columns: new[] { "BranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TableOrders_OpenedById",
                table: "TableOrders",
                column: "OpenedById");

            migrationBuilder.CreateIndex(
                name: "IX_TableOrders_TableId",
                table: "TableOrders",
                column: "TableId",
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_TableOrders_TenantId",
                table: "TableOrders",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_TableOrders_TableOrderId",
                table: "Sales",
                column: "TableOrderId",
                principalTable: "TableOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Ventas anteriores al pago mixto: un solo pago por el total con su forma de pago
            migrationBuilder.Sql("""
                INSERT INTO SalePayments (Id, TenantId, SaleId, Method, Amount)
                SELECT NEWID(), s.TenantId, s.Id, s.PaymentMethod, s.Total
                FROM Sales s
                WHERE NOT EXISTS (SELECT 1 FROM SalePayments p WHERE p.SaleId = s.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_TableOrders_TableOrderId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "SalePayments");

            migrationBuilder.DropTable(
                name: "TableOrderLines");

            migrationBuilder.DropTable(
                name: "TableOrders");

            migrationBuilder.DropTable(
                name: "DiningTables");

            migrationBuilder.DropIndex(
                name: "IX_Sales_TableOrderId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "TableName",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "TableOrderId",
                table: "Sales");
        }
    }
}
