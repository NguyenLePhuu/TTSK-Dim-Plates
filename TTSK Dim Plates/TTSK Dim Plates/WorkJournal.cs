using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace TTSK_AutoDim_Plates
{
    public sealed class WorkProject
    {
        public DateTime Day { get; set; }
        public DateTime FirstSeen { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public string ModelPath { get; set; }
    }

    public sealed class WorkSession
    {
        public string Id { get; set; }
        public DateTime Opened { get; set; }
        public DateTime? Closed { get; set; }
        public List<WorkProject> Projects { get; set; } = new List<WorkProject>();
    }

    internal static class WorkJournal
    {
        internal static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TTSK Dim Plates", "WorkJournal");

        internal static void Save(string folder, WorkSession session)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, session.Id + ".xml");
            string temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    new XmlSerializer(typeof(WorkSession)).Serialize(stream, session);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static List<WorkSession> Read(string folder, out int unreadable)
        {
            unreadable = 0;
            var sessions = new List<WorkSession>();
            if (!Directory.Exists(folder)) return sessions;
            foreach (string file in Directory.GetFiles(folder, "*.xml"))
            {
                try
                {
                    using (var stream = File.OpenRead(file))
                    {
                        var session = (WorkSession)new XmlSerializer(typeof(WorkSession)).Deserialize(stream);
                        if (session == null || session.Projects == null || session.Projects.Any(p => p == null)
                            || string.IsNullOrWhiteSpace(session.Id) || session.Opened == default(DateTime)
                            || (session.Closed.HasValue && session.Closed.Value < session.Opened))
                            throw new InvalidDataException();
                        sessions.Add(session);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException)
                { unreadable++; }
            }
            return sessions;
        }

        internal static WorkSession StartDay(DateTime now)
        {
            return new WorkSession { Id = Guid.NewGuid().ToString("N"), Opened = now };
        }

        // The old day's record stays unclosed; only an actual window close supplies Closed.
        internal static WorkSession RollToDay(string folder, WorkSession session, DateTime now)
        {
            if (session.Opened.Date == now.Date) return session;
            Save(folder, session);
            var next = StartDay(now);
            Save(folder, next);
            return next;
        }

        private static string Clean(string text)
        {
            return (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        }

        internal static List<WorkProjectReport> GroupProjects(IEnumerable<WorkSession> source, DateTime from, DateTime to)
        {
            // Aggregate completed sessions only. An open session must not alter a previous closing time.
            return source.GroupBy(s => s.Id).Select(g => g.Last())
                .Where(s => s.Closed.HasValue && s.Closed.Value.Date >= from.Date && s.Closed.Value.Date <= to.Date)
                .SelectMany(s => s.Projects.Where(p => p.Day.Date == s.Closed.Value.Date)
                    .Select(p => new { Number = Clean(p.Number), Name = Clean(p.Name), Closed = s.Closed.Value }))
                .GroupBy(p => new { p.Number, p.Name })
                .Select(g => new WorkProjectReport {
                    Number = g.Key.Number, Name = g.Key.Name,
                    Days = g.GroupBy(p => p.Closed.Date).OrderBy(d => d.Key)
                        .Select(d => new WorkCloseDay { Day = d.Key, Closed = d.Max(p => p.Closed) }).ToList()
                })
                .OrderBy(p => p.Days[0].Day).ThenBy(p => p.Number, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name)
                .ToList();
        }

        internal static string Report(IEnumerable<WorkSession> source, DateTime from, DateTime to)
        {
            var text = new StringBuilder();
            var groups = GroupProjects(source, from, to);
            if (groups.Count == 0) return "";
            text.AppendLine(from.ToString("dd/MM/yyyy") + " – " + to.ToString("dd/MM/yyyy"));
            foreach (var project in groups)
            {
                text.AppendLine();
                text.AppendLine("Project number: " + project.Number);
                text.AppendLine("Tên: " + project.Name);
                text.AppendLine("Ngày          Giờ đóng");
                foreach (var day in project.Days)
                    text.AppendLine(day.Day.ToString("dd/MM/yyyy") + "    " + day.Closed.ToString("HH:mm"));
            }
            return text.ToString();
        }
    }

    internal sealed class WorkProjectReport
    {
        internal string Number;
        internal string Name;
        internal List<WorkCloseDay> Days;
    }

    internal sealed class WorkCloseDay
    {
        internal DateTime Day;
        internal DateTime Closed;
    }
}
