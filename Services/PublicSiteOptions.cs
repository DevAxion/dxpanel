namespace SRXPanel.Services;

/// <summary>
/// Deployment-level switches for the public site, bound from the "PanelSettings"
/// section of appsettings.json. These control whether the underlying project is
/// credited/exposed on the public site. All default to <c>false</c> so a fresh
/// install is fully white-labelled.
/// </summary>
public class PublicSiteOptions
{
    /// <summary>Show a "Powered by" credit in the public footer. Off by default.</summary>
    public bool ShowPoweredBy { get; set; }

    /// <summary>Show a link to the project's GitHub repository. Off by default.</summary>
    public bool ShowGithubLink { get; set; }

    /// <summary>Expose the developer documentation (/docs) to the public. Off by default (SuperAdmin only).</summary>
    public bool DeveloperDocsPublic { get; set; }

    /// <summary>The project name used only when a "Powered by" credit is enabled.</summary>
    public string ProjectName { get; set; } = "SRXPanel";

    /// <summary>The project URL used only when the GitHub link / powered-by credit is enabled.</summary>
    public string ProjectUrl { get; set; } = "https://github.com/srxpanel/srxpanel";
}
