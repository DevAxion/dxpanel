using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRXPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase8Frontend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlogCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlogTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContactMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FrontendSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SiteName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Tagline = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HeroHeadline = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HeroSubheadline = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    HeroCtaPrimaryText = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    HeroCtaSecondaryText = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ContactEmail = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ContactPhone = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ContactAddress = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    GoogleMapsEmbed = table.Column<string>(type: "TEXT", nullable: true),
                    AboutContent = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    MissionStatement = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SocialFacebook = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SocialTwitter = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SocialInstagram = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SocialLinkedin = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SocialGithub = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PrimaryColor = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    SecondaryColor = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    CustomCss = table.Column<string>(type: "TEXT", nullable: true),
                    CustomJs = table.Column<string>(type: "TEXT", nullable: true),
                    GoogleAnalyticsId = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    FacebookPixelId = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    LiveChatCode = table.Column<string>(type: "TEXT", nullable: true),
                    CookieConsentText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MetaDescription = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RobotsTxt = table.Column<string>(type: "TEXT", nullable: false),
                    MoneyBackDays = table.Column<int>(type: "INTEGER", nullable: false),
                    MaintenanceMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaintenanceMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrontendSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KbCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KbCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StatCounters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    Suffix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatCounters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Testimonials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Company = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    PhotoUrl = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Content = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Testimonials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VpsPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CpuCores = table.Column<int>(type: "INTEGER", nullable: false),
                    RamMB = table.Column<int>(type: "INTEGER", nullable: false),
                    DiskGB = table.Column<int>(type: "INTEGER", nullable: false),
                    BandwidthGB = table.Column<int>(type: "INTEGER", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BillingCycle = table.Column<int>(type: "INTEGER", nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    OsOptions = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsPopular = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpsPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlogPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Excerpt = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AuthorId = table.Column<string>(type: "TEXT", nullable: true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FeaturedImage = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    MetaTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    MetaDescription = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Views = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlogPosts_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BlogPosts_BlogCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "BlogCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "KbArticles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    Views = table.Column<int>(type: "INTEGER", nullable: false),
                    HelpfulYes = table.Column<int>(type: "INTEGER", nullable: false),
                    HelpfulNo = table.Column<int>(type: "INTEGER", nullable: false),
                    MetaDescription = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KbArticles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KbArticles_KbCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "KbCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlogPostBlogTag",
                columns: table => new
                {
                    PostsId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogPostBlogTag", x => new { x.PostsId, x.TagsId });
                    table.ForeignKey(
                        name: "FK_BlogPostBlogTag_BlogPosts_PostsId",
                        column: x => x.PostsId,
                        principalTable: "BlogPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BlogPostBlogTag_BlogTags_TagsId",
                        column: x => x.TagsId,
                        principalTable: "BlogTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlogCategories_Slug",
                table: "BlogCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlogPostBlogTag_TagsId",
                table: "BlogPostBlogTag",
                column: "TagsId");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_AuthorId",
                table: "BlogPosts",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_CategoryId",
                table: "BlogPosts",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_Slug",
                table: "BlogPosts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_Status_PublishedAt",
                table: "BlogPosts",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BlogTags_Slug",
                table: "BlogTags",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContactMessages_CreatedAt",
                table: "ContactMessages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureItems_SortOrder",
                table: "FeatureItems",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_KbArticles_CategoryId_Slug",
                table: "KbArticles",
                columns: new[] { "CategoryId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KbCategories_Slug",
                table: "KbCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KbCategories_SortOrder",
                table: "KbCategories",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_StatCounters_SortOrder",
                table: "StatCounters",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_Testimonials_SortOrder",
                table: "Testimonials",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_VpsPlans_SortOrder",
                table: "VpsPlans",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlogPostBlogTag");

            migrationBuilder.DropTable(
                name: "ContactMessages");

            migrationBuilder.DropTable(
                name: "FeatureItems");

            migrationBuilder.DropTable(
                name: "FrontendSettings");

            migrationBuilder.DropTable(
                name: "KbArticles");

            migrationBuilder.DropTable(
                name: "StatCounters");

            migrationBuilder.DropTable(
                name: "Testimonials");

            migrationBuilder.DropTable(
                name: "VpsPlans");

            migrationBuilder.DropTable(
                name: "BlogPosts");

            migrationBuilder.DropTable(
                name: "BlogTags");

            migrationBuilder.DropTable(
                name: "KbCategories");

            migrationBuilder.DropTable(
                name: "BlogCategories");
        }
    }
}
