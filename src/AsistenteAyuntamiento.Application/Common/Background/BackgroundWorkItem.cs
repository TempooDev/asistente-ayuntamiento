namespace AsistenteAyuntamiento.Application.Common.Background;

/// <summary>
/// A unit of work executed outside the request, in its own DI scope. Tenant-scoped work carries its tenant
/// explicitly (there is no HttpContext in the background); platform-wide work must say so explicitly.
/// </summary>
public sealed record BackgroundWorkItem
{
    private BackgroundWorkItem(string name, string? tenantId, Func<IServiceProvider, CancellationToken, Task> work)
    {
        Name = name;
        TenantId = tenantId;
        Work = work;
    }

    public string Name { get; }

    /// <summary>Tenant to run under, or <c>null</c> for platform-wide work.</summary>
    public string? TenantId { get; }

    /// <summary>The work, given the scoped service provider and the host shutdown token.</summary>
    public Func<IServiceProvider, CancellationToken, Task> Work { get; }

    public static BackgroundWorkItem ForTenant(string name, string tenantId, Func<IServiceProvider, CancellationToken, Task> work)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return new BackgroundWorkItem(name, tenantId, work);
    }

    public static BackgroundWorkItem PlatformWide(string name, Func<IServiceProvider, CancellationToken, Task> work) =>
        new(name, null, work);
}
