namespace Dashboard;

internal static class ShutdownResourceDisposer
{
    public static void DisposeAll(params (string Name, Action Dispose)[] resources)
    {
        foreach (var resource in resources)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                HostOperationLogger.Error("shutdown", $"{resource.Name} disposal failed.", ex);
            }
        }
    }
}
