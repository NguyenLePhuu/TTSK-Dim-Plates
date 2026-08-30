#pragma warning disable 1633

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// Nishi Azabu topology 2.  The implementation has its own plan builders;
    /// slot 06 continues to use the original topology 1 entry point.
    /// </summary>
    public class PHU_NishiAzabuAutoDimSlot07
    {
        public static bool LastRunSucceeded { get; private set; }
        public static string LastRunMessage { get; private set; }

        public static void Run()
        {
            LastRunSucceeded = false;
            LastRunMessage = string.Empty;
            string message = PHU_NishiAzabuDimensionEngine.RunSlot07();
            if (!string.IsNullOrEmpty(message))
            {
                LastRunSucceeded = true;
                LastRunMessage = message;
            }
        }

        // Read-only entry point for validating a live drawing before mutation.
        public static string AuditPlan()
        {
            return PHU_NishiAzabuDimensionEngine.AuditPlanSlot07();
        }
    }
}
