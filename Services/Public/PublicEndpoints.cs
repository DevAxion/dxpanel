using System.Text;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using SRXPanel.Data;
using SRXPanel.Models;

namespace SRXPanel.Services.Public;

public static class PublicEndpoints
{
    private static readonly string[] StaticPaths =
        { "/", "/pricing", "/vps", "/domains", "/features", "/about", "/contact", "/blog", "/kb", "/register", "/docs" };

    public static void MapPublicSite(this WebApplication app)
    {
        // Language selector (footer) — persists a cookie, no server-side culture switch required.
        app.MapPost("/lang", (HttpContext ctx, IFrontendService _) =>
        {
            var lang = ctx.Request.Form["lang"].FirstOrDefault() ?? "en";
            if (lang is not ("en" or "az" or "tr" or "ar")) lang = "en";
            ctx.Response.Cookies.Append("srx_lang", lang, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                HttpOnly = false
            });
            var referer = ctx.Request.Headers.Referer.FirstOrDefault();
            return Results.Redirect(string.IsNullOrEmpty(referer) ? "/" : referer);
        }).AllowAnonymous();

        // Configurable robots.txt
        app.MapGet("/robots.txt", async (IFrontendService frontend, HttpContext ctx) =>
        {
            var fs = await frontend.GetSettingsAsync();
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var body = (fs.RobotsTxt ?? "User-agent: *\nAllow: /\n").TrimEnd() + $"\n\nSitemap: {baseUrl}/sitemap.xml\n";
            return Results.Text(body, "text/plain");
        }).AllowAnonymous();

        // Auto-generated sitemap.xml
        app.MapGet("/sitemap.xml", async (ApplicationDbContext db, HttpContext ctx) =>
        {
            var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = true, Async = true };
            await using var xw = XmlWriter.Create(sb, settings);
            await xw.WriteStartDocumentAsync();
            xw.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            void Url(string loc, DateTime? mod = null, string freq = "weekly", string priority = "0.6")
            {
                xw.WriteStartElement("url");
                xw.WriteElementString("loc", baseUrl + loc);
                if (mod.HasValue) xw.WriteElementString("lastmod", mod.Value.ToString("yyyy-MM-dd"));
                xw.WriteElementString("changefreq", freq);
                xw.WriteElementString("priority", priority);
                xw.WriteEndElement();
            }

            foreach (var p in StaticPaths)
                Url(p, priority: p == "/" ? "1.0" : "0.7");

            var posts = await db.BlogPosts.Where(x => x.Status == BlogStatus.Published)
                .Select(x => new { x.Slug, x.PublishedAt }).ToListAsync();
            foreach (var post in posts)
                Url($"/blog/{post.Slug}", post.PublishedAt, "monthly", "0.6");

            var articles = await db.KbArticles.Where(x => x.IsPublished)
                .Include(x => x.Category)
                .Select(x => new { x.Slug, CategorySlug = x.Category!.Slug, x.UpdatedAt }).ToListAsync();
            foreach (var a in articles)
                Url($"/kb/{a.CategorySlug}/{a.Slug}", a.UpdatedAt, "monthly", "0.5");

            await xw.WriteEndElementAsync();
            await xw.WriteEndDocumentAsync();
            await xw.FlushAsync();
            return Results.Text(sb.ToString(), "application/xml");
        }).AllowAnonymous();
    }
}
