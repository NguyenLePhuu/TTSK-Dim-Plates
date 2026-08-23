#pragma warning disable 1633

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

using TSM = Tekla.Structures.Model;

namespace Tekla.Technology.Akit.UserScript
{
    /// <summary>
    /// Single routing point for project-specific column DIM strategies.
    /// Geometry engines consume only the resolved key; a future project adds
    /// one matcher here and its own engine/strategy without changing Inzai
    /// Neighbor or Inzai Grid geometry rules.
    /// </summary>
    public static class PHU_ColumnProjectRouter
    {
        public const string GeneralSystemKey = "GENERAL_SYSTEM";
        public const string InzaiDataCenterKey = "INZAI_DATA_CENTER";

        private sealed class ProjectIdentity
        {
            public string ModelPath;
            public string ModelName;
            public string Normalized;
        }

        private interface IProjectMatcher
        {
            string Key { get; }
            bool Matches(ProjectIdentity identity);
        }

        private sealed class InzaiDataCenterMatcher : IProjectMatcher
        {
            public string Key
            {
                get { return InzaiDataCenterKey; }
            }

            public bool Matches(ProjectIdentity identity)
            {
                if (identity == null || String.IsNullOrEmpty(identity.Normalized))
                    return false;

                // Known production model root: 27-011)ATTOKPJ(Data_Center).
                // Both tokens are required so another project's H/C/L column
                // cannot enter this non-company-standard dimension flow.
                return identity.Normalized.Contains("ATTOKPJ")
                    && identity.Normalized.Contains("DATACENTER");
            }
        }

        private static readonly IProjectMatcher[] Matchers = new IProjectMatcher[]
        {
            new InzaiDataCenterMatcher()
        };

        public static bool TryResolve(TSM.Model model, out string projectKey, out string message)
        {
            projectKey = String.Empty;
            message = String.Empty;
            try
            {
                if (model == null || !model.GetConnectionStatus())
                {
                    message = "Column project routing failed: Model is unavailable.";
                    return false;
                }

                ProjectIdentity identity = ReadIdentity(model);
                List<IProjectMatcher> matches = new List<IProjectMatcher>();
                for (int i = 0; i < Matchers.Length; i++)
                {
                    if (Matchers[i].Matches(identity))
                        matches.Add(Matchers[i]);
                }

                if (matches.Count == 0)
                {
                    projectKey = GeneralSystemKey;
                    message = "Column project resolved to the general system strategy.";
                    return true;
                }
                if (matches.Count != 1)
                {
                    message = "More than one column project matched the active model.";
                    return false;
                }

                projectKey = matches[0].Key;
                message = "Column project resolved: " + projectKey + ".";
                return true;
            }
            catch (Exception ex)
            {
                projectKey = String.Empty;
                message = "Column project routing failed. " + ex.Message;
                return false;
            }
        }

        private static ProjectIdentity ReadIdentity(TSM.Model model)
        {
            TSM.ModelInfo info = model.GetInfo();
            ProjectIdentity identity = new ProjectIdentity();
            identity.ModelPath = info == null ? String.Empty : (info.ModelPath ?? String.Empty);
            identity.ModelName =
                Convert.ToString(GetMember(info, "ModelName"), CultureInfo.InvariantCulture)
                ?? String.Empty;
            identity.Normalized = Normalize(identity.ModelPath + " " + identity.ModelName);
            return identity;
        }

        private static object GetMember(object value, string name)
        {
            if (value == null || String.IsNullOrEmpty(name))
                return null;
            try
            {
                Type type = value.GetType();
                PropertyInfo property = type.GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (property != null)
                    return property.GetValue(value, null);
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                return field == null ? null : field.GetValue(value);
            }
            catch
            {
                return null;
            }
        }

        private static string Normalize(string value)
        {
            StringBuilder result = new StringBuilder();
            string upper = (value ?? String.Empty).ToUpperInvariant();
            for (int i = 0; i < upper.Length; i++)
            {
                char c = upper[i];
                if (Char.IsLetterOrDigit(c))
                    result.Append(c);
            }
            return result.ToString();
        }
    }
}
