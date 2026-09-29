using System.Runtime.InteropServices;

namespace LiveTypeBridge.Core.Diagnostics;

public enum FirewallStatus
{
    Allowed,      // a rule exists for our executable
    NoRule,       // no rule yet: first server start will trigger the Windows prompt
    Unknown,      // COM query failed (non-fatal)
}

/// <summary>
/// Checks (read-only) whether Windows Firewall already has a rule for this executable.
/// We never add firewall rules ourselves — that would need admin rights. Windows shows
/// its own allow/deny prompt the first time the server listens on the LAN.
/// </summary>
public static class FirewallChecker
{
    public static FirewallStatus Query()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return FirewallStatus.Unknown;

            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null) return FirewallStatus.Unknown;
            var fw = Activator.CreateInstance(type);
            if (fw is null) return FirewallStatus.Unknown;

            try
            {
                var rules = ((dynamic)fw).Rules;
                foreach (var rule in rules)
                {
                    try
                    {
                        var appPath = (string?)rule.ApplicationName;
                        if (!string.IsNullOrEmpty(appPath)
                            && string.Equals(appPath, exe, StringComparison.OrdinalIgnoreCase)
                            && (bool)rule.Enabled)
                        {
                            Marshal.ReleaseComObject(rule);
                            return FirewallStatus.Allowed;
                        }
                    }
                    catch { /* some rule types throw on ApplicationName */ }
                    finally
                    {
                        Marshal.ReleaseComObject(rule);
                    }
                }
                return FirewallStatus.NoRule;
            }
            finally
            {
                Marshal.ReleaseComObject(fw);
            }
        }
        catch
        {
            return FirewallStatus.Unknown;
        }
    }
}
