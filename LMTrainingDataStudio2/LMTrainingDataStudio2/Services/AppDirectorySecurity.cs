using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LMTrainingDataStudio2.Services;

/// <summary>
/// Restricts application data directories to the current user on Windows.
/// No-ops on other platforms where the directory follows the default umask.
/// </summary>
public static class AppDirectorySecurity
{
    private static readonly ConcurrentDictionary<string, byte> Restricted = new(
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Removes inherited permissions and grants full control to the current user,
    /// SYSTEM and Administrators only. Best effort; failures are logged.
    /// </summary>
    public static bool TryRestrictToCurrentUser(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !OperatingSystem.IsWindows())
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (Restricted.ContainsKey(fullPath))
        {
            return true;
        }

        try
        {
            RestrictCore(fullPath);
            Restricted.TryAdd(fullPath, 0);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or
                                       PlatformNotSupportedException or InvalidOperationException or
                                       IdentityNotMappedException or SecurityException)
        {
            AppLog.Warning($"Could not restrict permissions on '{fullPath}': {ex.Message}");
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictCore(string fullPath)
    {
        var info = new DirectoryInfo(fullPath);
        info.Create();

        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows identity has no user SID.");

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddFullControl(security, user);
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        info.SetAccessControl(security);
    }

    private static void AddFullControl(
        DirectorySecurity security, IdentityReference identity)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }
}
