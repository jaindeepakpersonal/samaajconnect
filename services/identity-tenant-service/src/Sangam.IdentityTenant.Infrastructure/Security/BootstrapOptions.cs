namespace Sangam.IdentityTenant.Infrastructure.Security;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    /// <summary>
    /// Identifier for the first Super Admin. Leave empty to skip bootstrapping
    /// entirely, which is what a deployment that already has one should do.
    /// </summary>
    public string SuperAdminIdentifier { get; set; } = string.Empty;

    public string SuperAdminPassword { get; set; } = string.Empty;

    public string SuperAdminName { get; set; } = "Platform Super Admin";
}
