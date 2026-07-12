using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase11DeveloperTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxCronJobs",
                table: "Packages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CronJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Command = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Schedule = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRunning = table.Column<bool>(type: "INTEGER", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EmailOnSuccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmailOnFailure = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CronJobs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DebugMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorReportingEmail = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperSettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhpConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    MemoryLimit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MaxExecutionTime = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadMaxFilesize = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    PostMaxSize = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MaxInputVars = table.Column<int>(type: "INTEGER", nullable: false),
                    Timezone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    DisplayErrors = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorReporting = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhpConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhpConfigs_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SshAccesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowPasswordAuth = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SshAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SshAccesses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SshAccessLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    KeyFingerprint = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SshAccessLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SshKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    KeyType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SshKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SshKeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StagingSites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    StagingDomain = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    StagingPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TablePrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    PasswordProtected = table.Column<bool>(type: "INTEGER", nullable: false),
                    AuthUser = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    AuthPasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncDirection = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagingSites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagingSites_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TerminalSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    TokenId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    TerminationRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    CommandCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TerminalSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WebhookEndpointId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseCode = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponseBody = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_WebhookEndpoints_WebhookEndpointId",
                        column: x => x.WebhookEndpointId,
                        principalTable: "WebhookEndpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CronJobLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CronJobId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Manual = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronJobLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CronJobLogs_CronJobs_CronJobId",
                        column: x => x.CronJobId,
                        principalTable: "CronJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GitRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DomainId = table.Column<int>(type: "INTEGER", nullable: false),
                    RepoUrl = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DeployPath = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    SshKeyId = table.Column<int>(type: "INTEGER", nullable: true),
                    WebhookSecret = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    AutoDeploy = table.Column<bool>(type: "INTEGER", nullable: false),
                    PostDeployCommands = table.Column<string>(type: "TEXT", nullable: true),
                    LastDeployAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCommitHash = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    LastCommitMessage = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitRepositories_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GitRepositories_Domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "Domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GitRepositories_SshKeys_SshKeyId",
                        column: x => x.SshKeyId,
                        principalTable: "SshKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GitDeployments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepositoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    TriggerType = table.Column<int>(type: "INTEGER", nullable: false),
                    CommitHash = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    CommitMessage = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitDeployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GitDeployments_GitRepositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "GitRepositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CronJobLogs_CronJobId_StartedAt",
                table: "CronJobLogs",
                columns: new[] { "CronJobId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CronJobs_NextRunAt",
                table: "CronJobs",
                column: "NextRunAt");

            migrationBuilder.CreateIndex(
                name: "IX_CronJobs_UserId_IsActive",
                table: "CronJobs",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperSettings_UserId",
                table: "DeveloperSettings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GitDeployments_RepositoryId_StartedAt",
                table: "GitDeployments",
                columns: new[] { "RepositoryId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GitRepositories_DomainId",
                table: "GitRepositories",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_GitRepositories_SshKeyId",
                table: "GitRepositories",
                column: "SshKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_GitRepositories_UserId",
                table: "GitRepositories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PhpConfigs_DomainId",
                table: "PhpConfigs",
                column: "DomainId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SshAccesses_UserId",
                table: "SshAccesses",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SshAccessLogs_UserId_ConnectedAt",
                table: "SshAccessLogs",
                columns: new[] { "UserId", "ConnectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SshKeys_UserId_Fingerprint",
                table: "SshKeys",
                columns: new[] { "UserId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StagingSites_DomainId",
                table: "StagingSites",
                column: "DomainId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StagingSites_ExpiresAt",
                table: "StagingSites",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalSessions_UserId_IsActive",
                table: "TerminalSessions",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookEndpointId_CreatedAt",
                table: "WebhookDeliveries",
                columns: new[] { "WebhookEndpointId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CronJobLogs");

            migrationBuilder.DropTable(
                name: "DeveloperSettings");

            migrationBuilder.DropTable(
                name: "GitDeployments");

            migrationBuilder.DropTable(
                name: "PhpConfigs");

            migrationBuilder.DropTable(
                name: "SshAccesses");

            migrationBuilder.DropTable(
                name: "SshAccessLogs");

            migrationBuilder.DropTable(
                name: "StagingSites");

            migrationBuilder.DropTable(
                name: "TerminalSessions");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "CronJobs");

            migrationBuilder.DropTable(
                name: "GitRepositories");

            migrationBuilder.DropTable(
                name: "SshKeys");

            migrationBuilder.DropColumn(
                name: "MaxCronJobs",
                table: "Packages");
        }
    }
}
