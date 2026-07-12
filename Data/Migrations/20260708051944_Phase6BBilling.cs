using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6BBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayCurrency",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ReferredByAffiliateId",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AffiliateClicks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AffiliateId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ip = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Utm = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Converted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateClicks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Affiliates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalEarned = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PendingBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Affiliates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Affiliates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiRequestLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiKeyId = table.Column<int>(type: "INTEGER", nullable: true),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Integration = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Ip = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiRequestLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlatformName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LogoPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    DefaultCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PlatformFeePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TrialPeriodDays = table.Column<int>(type: "INTEGER", nullable: false),
                    MinPayoutAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DefaultAffiliateCommission = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TermsUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    PrivacyUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    MaintenanceMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    Registration = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireEmailVerification = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResellerBillingConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResellerId = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformFeePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinPayoutAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AutoTopUpEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoTopUpThreshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AutoTopUpAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResellerBillingConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResellerBillingConfigs_AspNetUsers_ResellerId",
                        column: x => x.ResellerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResellerInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResellerId = table.Column<string>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResellerInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResellerInvoices_AspNetUsers_ResellerId",
                        column: x => x.ResellerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResellerInvoiceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResellerId = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    CompanyAddress = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    TaxNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    InvoicePrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    NextNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    LogoPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    PaymentTerms = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    FooterNotes = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    BankDetails = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResellerInvoiceSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResellerInvoiceSettings_AspNetUsers_ResellerId",
                        column: x => x.ResellerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResellerPaymentSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResellerId = table.Column<string>(type: "TEXT", nullable: false),
                    StripeConnectAccountId = table.Column<string>(type: "TEXT", nullable: true),
                    ConnectOnboarded = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseOwnKeys = table.Column<bool>(type: "INTEGER", nullable: false),
                    OwnPublishableKey = table.Column<string>(type: "TEXT", nullable: true),
                    OwnSecretKey = table.Column<string>(type: "TEXT", nullable: true),
                    AcceptedMethods = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxLabel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TaxNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ShowTaxNumberOnInvoice = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResellerPaymentSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResellerPaymentSettings_AspNetUsers_ResellerId",
                        column: x => x.ResellerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResellerTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResellerId = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ReferenceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResellerTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResellerTransactions_AspNetUsers_ResellerId",
                        column: x => x.ResellerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookEndpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Secret = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    OnDomainChange = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnEmailChange = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnSslExpiring = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnInvoicePaid = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEndpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookEndpoints_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AffiliatePayoutRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AffiliateId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PaymentDetails = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliatePayoutRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliatePayoutRequests_Affiliates_AffiliateId",
                        column: x => x.AffiliateId,
                        principalTable: "Affiliates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AffiliateReferrals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AffiliateId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferredUserId = table.Column<string>(type: "TEXT", nullable: false),
                    SubscriptionId = table.Column<int>(type: "INTEGER", nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SignupIp = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffiliateReferrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AffiliateReferrals_Affiliates_AffiliateId",
                        column: x => x.AffiliateId,
                        principalTable: "Affiliates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateClicks_AffiliateId_CreatedAt",
                table: "AffiliateClicks",
                columns: new[] { "AffiliateId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AffiliatePayoutRequests_AffiliateId",
                table: "AffiliatePayoutRequests",
                column: "AffiliateId");

            migrationBuilder.CreateIndex(
                name: "IX_AffiliateReferrals_AffiliateId",
                table: "AffiliateReferrals",
                column: "AffiliateId");

            migrationBuilder.CreateIndex(
                name: "IX_Affiliates_Code",
                table: "Affiliates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Affiliates_UserId",
                table: "Affiliates",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiRequestLogs_KeyPrefix_CreatedAt",
                table: "ApiRequestLogs",
                columns: new[] { "KeyPrefix", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Code",
                table: "Currencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_FromCurrency_ToCurrency",
                table: "ExchangeRates",
                columns: new[] { "FromCurrency", "ToCurrency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResellerBillingConfigs_ResellerId",
                table: "ResellerBillingConfigs",
                column: "ResellerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResellerInvoices_ResellerId",
                table: "ResellerInvoices",
                column: "ResellerId");

            migrationBuilder.CreateIndex(
                name: "IX_ResellerInvoiceSettings_ResellerId",
                table: "ResellerInvoiceSettings",
                column: "ResellerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResellerPaymentSettings_ResellerId",
                table: "ResellerPaymentSettings",
                column: "ResellerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResellerTransactions_ResellerId_CreatedAt",
                table: "ResellerTransactions",
                columns: new[] { "ResellerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEndpoints_UserId",
                table: "WebhookEndpoints",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffiliateClicks");

            migrationBuilder.DropTable(
                name: "AffiliatePayoutRequests");

            migrationBuilder.DropTable(
                name: "AffiliateReferrals");

            migrationBuilder.DropTable(
                name: "ApiRequestLogs");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "ResellerBillingConfigs");

            migrationBuilder.DropTable(
                name: "ResellerInvoices");

            migrationBuilder.DropTable(
                name: "ResellerInvoiceSettings");

            migrationBuilder.DropTable(
                name: "ResellerPaymentSettings");

            migrationBuilder.DropTable(
                name: "ResellerTransactions");

            migrationBuilder.DropTable(
                name: "WebhookEndpoints");

            migrationBuilder.DropTable(
                name: "Affiliates");

            migrationBuilder.DropColumn(
                name: "DisplayCurrency",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ReferredByAffiliateId",
                table: "AspNetUsers");
        }
    }
}
