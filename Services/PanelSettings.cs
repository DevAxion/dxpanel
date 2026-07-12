namespace SRXPanel.Services;

/// <summary>
/// Panel-wide configuration bound from the "Panel" section of appsettings.json.
/// Editable from /Settings/Index (SuperAdmin) which writes back to appsettings.json.
/// </summary>
public class PanelSettings
{
    public string Hostname { get; set; } = "panel.example.com";
    public string DefaultPhpVersion { get; set; } = "8.3";
    public int MaxUploadSizeMB { get; set; } = 100;
    public string LetsEncryptEmail { get; set; } = "admin@example.com";
    public string SshKeyPath { get; set; } = "/root/.ssh/id_rsa";

    public SmtpSettings Smtp { get; set; } = new();
    public MySqlSettings MySql { get; set; } = new();
    public NginxPaths Nginx { get; set; } = new();
    public BindPaths Bind { get; set; } = new();
    public MailPaths Mail { get; set; } = new();

    public static readonly string[] PhpVersions = { "7.4", "8.0", "8.1", "8.2", "8.3" };
}

public class SmtpSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "no-reply@example.com";
}

public class MySqlSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3306;
    public string RootUser { get; set; } = "root";
    public string RootPassword { get; set; } = string.Empty;

    public string BuildConnectionString(string? database = null)
    {
        var db = string.IsNullOrEmpty(database) ? string.Empty : $"Database={database};";
        return $"Server={Host};Port={Port};{db}Uid={RootUser};Pwd={RootPassword};";
    }
}

public class NginxPaths
{
    public string SitesAvailable { get; set; } = "/etc/nginx/sites-available";
    public string SitesEnabled { get; set; } = "/etc/nginx/sites-enabled";
    public string LogDir { get; set; } = "/var/log/nginx";
    public string PhpFpmSocketPattern { get; set; } = "/run/php/php{version}-fpm.sock";
}

public class BindPaths
{
    public string ZonesDir { get; set; } = "/etc/bind/zones";
    public string NamedConfLocal { get; set; } = "/etc/bind/named.conf.local";
    public string DefaultNs { get; set; } = "ns1.example.com.";
    public string ServerIp { get; set; } = "203.0.113.10";
}

public class MailPaths
{
    public string VmailboxFile { get; set; } = "/etc/postfix/vmailbox";
    public string VirtualFile { get; set; } = "/etc/postfix/virtual";
    public string MaildirRoot { get; set; } = "/var/mail/vhosts";
}

/// <summary>Stripe configuration bound from the "Stripe" config section.</summary>
public class StripeSettings
{
    public string PublishableKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string Currency { get; set; } = "usd";
}

/// <summary>Twilio SMS configuration bound from the "Twilio" config section.</summary>
public class TwilioSettings
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) &&
        !string.IsNullOrWhiteSpace(AuthToken) &&
        !string.IsNullOrWhiteSpace(FromNumber);
}

/// <summary>Off-site backup (S3 / Backblaze B2) configuration bound from the "Backup" config section.</summary>
public class BackupSettings
{
    public string Provider { get; set; } = "S3";       // "S3" or "Backblaze"
    public string BucketName { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string Endpoint { get; set; } = string.Empty; // for Backblaze: s3.us-west-002.backblazeb2.com

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BucketName) &&
        !string.IsNullOrWhiteSpace(AccessKey) &&
        !string.IsNullOrWhiteSpace(SecretKey);
}
