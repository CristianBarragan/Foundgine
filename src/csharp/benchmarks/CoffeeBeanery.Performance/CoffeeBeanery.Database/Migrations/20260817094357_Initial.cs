using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CoffeeBeanery.Database.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Accounting");

            migrationBuilder.EnsureSchema(
                name: "Banking");

            migrationBuilder.EnsureSchema(
                name: "Lending");

            migrationBuilder.CreateTable(
                name: "Account",
                schema: "Accounting",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AccountKey = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountNumber = table.Column<string>(type: "text", nullable: true),
                    AccountName = table.Column<string>(type: "text", nullable: true),
                    ProcessedDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Account", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customer",
                schema: "Banking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerKey = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    FullName = table.Column<string>(type: "text", nullable: true),
                    CustomerType = table.Column<int>(type: "integer", nullable: true),
                    ProcessedDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContactPoint",
                schema: "Banking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContactPointKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactPointType = table.Column<int>(type: "integer", nullable: true),
                    ContactPointValue = table.Column<string>(type: "text", nullable: true),
                    CustomerKey = table.Column<int>(type: "integer", nullable: true),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    ProcessedDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactPoint", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContactPoint_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "Banking",
                        principalTable: "Customer",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CustomerBankingRelationship",
                schema: "Banking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerBankingRelationshipKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: true),
                    CustomerKey = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessedDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBankingRelationship", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerBankingRelationship_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "Banking",
                        principalTable: "Customer",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CustomerCustomerRelationship",
                schema: "Banking",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerCustomerRelationshipKey = table.Column<Guid>(type: "uuid", nullable: true),
                    OuterCustomerId = table.Column<int>(type: "integer", nullable: true),
                    InnerCustomerId = table.Column<int>(type: "integer", nullable: true),
                    CustomerCustomerRelationshipType = table.Column<int>(type: "integer", nullable: true),
                    ProcessedDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCustomerRelationship", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCustomerRelationship_Customer_InnerCustomerId",
                        column: x => x.InnerCustomerId,
                        principalSchema: "Banking",
                        principalTable: "Customer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CustomerCustomerRelationship_Customer_OuterCustomerId",
                        column: x => x.OuterCustomerId,
                        principalSchema: "Banking",
                        principalTable: "Customer",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Contract",
                schema: "Lending",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContractKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractType = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    AccountId = table.Column<int>(type: "integer", nullable: true),
                    CustomerBankingRelationshipId = table.Column<int>(type: "integer", nullable: true),
                    ProcessedDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contract", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contract_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "Accounting",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Contract_CustomerBankingRelationship_CustomerBankingRelatio~",
                        column: x => x.CustomerBankingRelationshipId,
                        principalSchema: "Banking",
                        principalTable: "CustomerBankingRelationship",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Transaction",
                schema: "Lending",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TransactionKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    Balance = table.Column<decimal>(type: "numeric", nullable: true),
                    ContractId = table.Column<int>(type: "integer", nullable: true),
                    ContractKey = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountId = table.Column<int>(type: "integer", nullable: true),
                    AccountKey = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessedDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transaction_Account_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "Accounting",
                        principalTable: "Account",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transaction_Contract_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "Lending",
                        principalTable: "Contract",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Account_AccountKey",
                schema: "Accounting",
                table: "Account",
                column: "AccountKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContactPoint_ContactPointKey",
                schema: "Banking",
                table: "ContactPoint",
                column: "ContactPointKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContactPoint_CustomerId",
                schema: "Banking",
                table: "ContactPoint",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Contract_AccountId",
                schema: "Lending",
                table: "Contract",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contract_ContractKey",
                schema: "Lending",
                table: "Contract",
                column: "ContractKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contract_CustomerBankingRelationshipId_Id",
                schema: "Lending",
                table: "Contract",
                columns: new[] { "CustomerBankingRelationshipId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_CustomerKey",
                schema: "Banking",
                table: "Customer",
                column: "CustomerKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBankingRelationship_CustomerBankingRelationshipKey",
                schema: "Banking",
                table: "CustomerBankingRelationship",
                column: "CustomerBankingRelationshipKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBankingRelationship_CustomerId_Id",
                schema: "Banking",
                table: "CustomerBankingRelationship",
                columns: new[] { "CustomerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCustomerRelationship_CustomerCustomerRelationshipKey",
                schema: "Banking",
                table: "CustomerCustomerRelationship",
                column: "CustomerCustomerRelationshipKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCustomerRelationship_InnerCustomerId",
                schema: "Banking",
                table: "CustomerCustomerRelationship",
                column: "InnerCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCustomerRelationship_OuterCustomerId_InnerCustomerId",
                schema: "Banking",
                table: "CustomerCustomerRelationship",
                columns: new[] { "OuterCustomerId", "InnerCustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_AccountId",
                schema: "Lending",
                table: "Transaction",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_ContractId_Id",
                schema: "Lending",
                table: "Transaction",
                columns: new[] { "ContractId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_TransactionKey",
                schema: "Lending",
                table: "Transaction",
                column: "TransactionKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContactPoint",
                schema: "Banking");

            migrationBuilder.DropTable(
                name: "CustomerCustomerRelationship",
                schema: "Banking");

            migrationBuilder.DropTable(
                name: "Transaction",
                schema: "Lending");

            migrationBuilder.DropTable(
                name: "Contract",
                schema: "Lending");

            migrationBuilder.DropTable(
                name: "Account",
                schema: "Accounting");

            migrationBuilder.DropTable(
                name: "CustomerBankingRelationship",
                schema: "Banking");

            migrationBuilder.DropTable(
                name: "Customer",
                schema: "Banking");
        }
    }
}
