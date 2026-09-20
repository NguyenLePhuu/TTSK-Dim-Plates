using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TTSK_AutoDim_Plates;

internal static class WorkJournalTests
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static WorkSession Session(DateTime day, int start, int end, string number, string name)
    {
        return new WorkSession { Id = Guid.NewGuid().ToString("N"), Opened = day.AddHours(start),
            Closed = end < 0 ? (DateTime?)null : day.AddHours(end), Projects = new List<WorkProject> {
                new WorkProject { Day = day, FirstSeen = day.AddHours(start), Number = number, Name = name, ModelPath = "model/" + number } } };
    }
    static void Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        string store = Path.Combine(root, "test-data-" + Guid.NewGuid().ToString("N"));
        DateTime day = new DateTime(2026, 9, 14);
        const string japanese = "グレープストーン桶川工場　開発棟増築工事";
        var morning = Session(day, 8, 12, "27-006", japanese);
        var afternoon = Session(day, 13, 17, "27-007", "Dự án B");
        var evening = Session(day, 18, 19, "27-006", japanese);
        var nextDay = Session(day.AddDays(1), 8, 18, "27-006", japanese);
        nextDay.Projects[0].ModelPath = "another-copy-of-project-A";
        var thirdDay = Session(day.AddDays(2), 8, 17, "27-007", "Dự án B");
        var items = new List<WorkSession> { evening, morning, afternoon, nextDay, thirdDay };
        string report = WorkJournal.Report(items, day, day.AddDays(2));
        var groups = WorkJournal.GroupProjects(items, day, day.AddDays(2));
        Check(groups.Count == 2, "Repeated projects across dates and model paths must merge.");
        Check(groups[0].Days.Count == 2 && groups[1].Days.Count == 2, "Each project needs its dates only.");
        Check(groups[0].Days[0].Closed == day.AddHours(19), "A -> B -> A must keep the last close for A.");
        Check(groups[1].Days[0].Closed == day.AddHours(17), "B must retain its own closing time.");
        Check(report.Split(new[] { japanese }, StringSplitOptions.None).Length == 2, "The project name must appear just once.");
        Check(report.Contains("14/09/2026    19:00") && report.Contains("15/09/2026    18:00"), "Dates and closing times must remain legible.");
        Check(!report.Contains("12:00") && !report.Contains("08:00"), "Earlier close and opening times must not appear.");
        Check(!report.Contains("TỔNG") && !report.Contains("Phiên") && !report.Contains("quy đổi") && !report.Contains("chưa"), "No extra explanations or totals.");
        foreach (var item in items) WorkJournal.Save(store, item);
        int unreadable;
        var loaded = WorkJournal.Read(store, out unreadable);
        Check(loaded.Count == 5 && unreadable == 0, "Multiple session records coexist.");
        Check(WorkJournal.Report(loaded, day, day.AddDays(2)) == report, "Restart must preserve grouped output.");
        WorkJournal.Save(store, evening);
        Check(WorkJournal.Read(store, out unreadable).Count == 5, "Atomic replacement does not duplicate sessions.");
        File.WriteAllText(Path.Combine(store, "broken.xml"), "<broken>");
        loaded = WorkJournal.Read(store, out unreadable);
        Check(unreadable == 1 && loaded.Count == 5, "Corrupt files reported without changing valid data.");
        var incomplete = Session(day.AddDays(3), 8, -1, "27-008", "Không được xuất");
        items.Add(incomplete);
        Check(WorkJournal.Report(items, day, day.AddDays(3)) == WorkJournal.Report(items.Take(5), day, day.AddDays(3)), "Open sessions must never appear in export.");
        Check(WorkJournal.Report(new[] { incomplete }, day, day.AddDays(4)) == "", "No closing time means blank output.");
        Check(WorkJournal.GroupProjects(items, day.AddDays(1), day.AddDays(1)).Count == 1, "Date filter must apply before grouping.");
        var mixed = Session(day, 8, 17, "27-006", japanese);
        mixed.Projects.Add(new WorkProject { Day = day, FirstSeen = day.AddHours(13), Number = "27-007", Name = "Dự án B", ModelPath = "model/B" });
        var mixedGroups = WorkJournal.GroupProjects(new[] { mixed }, day, day);
        Check(mixedGroups.Count == 2 && mixedGroups.All(g => g.Days[0].Closed.Hour == 17), "Multiple projects in one closed session must all appear.");
        var open = Session(day, 20, -1, "27-006", japanese);
        var rolled = WorkJournal.RollToDay(store, open, day.AddDays(1).AddMinutes(1));
        Check(rolled.Id != open.Id && rolled.Opened.Date == day.AddDays(1) && !rolled.Closed.HasValue, "Midnight starts a distinct daily session.");
        Check(rolled.Projects.Count == 0 && !open.Closed.HasValue, "Rollover cannot invent a close or carry yesterday's projects into today.");
        Check(object.ReferenceEquals(rolled, WorkJournal.RollToDay(store, rolled, day.AddDays(1).AddHours(8))), "Same day must not create duplicate sessions.");
        loaded = WorkJournal.Read(store, out unreadable);
        Check(WorkJournal.GroupProjects(loaded, day, day)[0].Days[0].Closed == evening.Closed, "Rollover preserves yesterday's last real closing time.");
        Check(WorkJournal.GroupProjects(new[] { open, rolled }, day, day.AddDays(1)).Count == 0, "Midnight without any close stays absent from report.");
        var legacy = Session(day, 20, -1, "27-006", japanese);
        legacy.Closed = day.AddDays(1).AddHours(2);
        legacy.Projects.Add(new WorkProject { Day = day.AddDays(1), Number = "27-007", Name = "Dự án B" });
        var legacyGroups = WorkJournal.GroupProjects(new[] { legacy }, day, day.AddDays(1));
        Check(legacyGroups.Count == 1 && legacyGroups[0].Number == "27-007", "Legacy overnight records must not transfer closes to yesterday's projects.");
        Check(WorkJournal.Report(new WorkSession[0], day, day) == "", "Empty history stays blank.");
        File.WriteAllText(Path.Combine(root, "TTSK_GioLam_Mau.txt"), report, new UTF8Encoding(true));
        Check(File.ReadAllText(Path.Combine(root, "TTSK_GioLam_Mau.txt"), Encoding.UTF8).Contains(japanese), "Japanese UTF-8 survives export.");
        Console.WriteLine("PASS: " + checks + " checks. Fictional sample: " + Path.Combine(root, "TTSK_GioLam_Mau.txt"));
    }
}