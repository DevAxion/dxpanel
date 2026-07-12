namespace SRXPanel.Models;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Reseller = "Reseller";
    public const string Client = "Client";

    public static readonly string[] All = { SuperAdmin, Reseller, Client };
}
