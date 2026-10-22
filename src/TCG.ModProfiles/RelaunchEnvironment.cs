namespace TCG.ModProfiles.Shared
{
    public static class RelaunchEnvironment
    {
        // Doorstop guards belong to one process; the native bootstrap recomputes them.
        public const string Reset = "@($psi.EnvironmentVariables.Keys) | Where-Object { $_ -like 'DOORSTOP_*' } | ForEach-Object { $psi.EnvironmentVariables.Remove($_) }; ";
    }
}
