using SRXPanel.Models;

namespace SRXPanel.Services.AppHosting;

public record AppTemplate(string Key, string Name, AppRuntimeType Type, string EntryPoint,
    string StartCommand, string Packages, string Description, string InstallHint);

/// <summary>Framework quickstart presets shown on the create page (code is shown, never auto-created).</summary>
public static class QuickstartTemplates
{
    public static readonly IReadOnlyList<AppTemplate> All = new[]
    {
        // Node.js
        new AppTemplate("express", "Express.js", AppRuntimeType.NodeJs, "app.js",
            "node app.js", "express", "Minimal, unopinionated Node web framework.", "npm install express"),
        new AppTemplate("nextjs", "Next.js", AppRuntimeType.NodeJs, "node_modules/.bin/next",
            "npm run start", "next react react-dom", "React framework with SSR (run build first).", "npm install && npm run build"),
        new AppTemplate("nestjs", "NestJS", AppRuntimeType.NodeJs, "dist/main.js",
            "node dist/main.js", "@nestjs/core @nestjs/common", "Progressive Node framework for scalable apps.", "npm install && npm run build"),
        new AppTemplate("fastify", "Fastify", AppRuntimeType.NodeJs, "server.js",
            "node server.js", "fastify", "Fast and low-overhead Node web framework.", "npm install fastify"),

        // Python
        new AppTemplate("flask", "Flask", AppRuntimeType.Python, "app.py",
            "gunicorn app:app", "flask gunicorn", "Lightweight WSGI web application framework.", "pip install flask gunicorn"),
        new AppTemplate("django", "Django", AppRuntimeType.Python, "project/wsgi.py",
            "gunicorn project.wsgi:application", "django gunicorn", "Batteries-included Python web framework.", "pip install django gunicorn"),
        new AppTemplate("fastapi", "FastAPI", AppRuntimeType.Python, "main.py",
            "uvicorn main:app --host 127.0.0.1", "fastapi uvicorn", "Modern, fast ASGI framework with type hints.", "pip install fastapi uvicorn")
    };

    public static AppTemplate? Get(string key) => All.FirstOrDefault(t => t.Key == key);
    public static IEnumerable<AppTemplate> ForType(AppRuntimeType type) => All.Where(t => t.Type == type);
}
