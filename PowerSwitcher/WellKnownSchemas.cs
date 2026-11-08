using System;

namespace PowerSwitcher
{
    /// <summary>GUIDs of the power plans that ship with Windows.</summary>
    public static class WellKnownSchemas
    {
        public static readonly Guid PowerSaver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");
        public static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid HighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

        // Hidden by default; `powercfg -duplicatescheme` gives the copy a new GUID, so a copy counts as a custom plan.
        public static readonly Guid UltimatePerformance = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");

        /// <summary>
        /// Whether a plan is Power saver, Balanced or High performance. Copies get a new GUID but keep the built-in
        /// name, and some PCs only have copies, so these are also matched by their (English) name.
        /// </summary>
        public static bool IsStandardPlan(Guid guid, string name)
        {
            if (guid == PowerSaver || guid == Balanced || guid == HighPerformance) { return true; }

            switch (name?.Trim().ToLowerInvariant())
            {
                case "power saver":
                case "balanced":
                case "high performance":
                    return true;
                default:
                    return false;
            }
        }
    }
}
