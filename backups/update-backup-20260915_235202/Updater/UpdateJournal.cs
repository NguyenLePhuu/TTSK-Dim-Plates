using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TTSK_AutoDim_Plates.Updater
{
    public enum UpdateTransactionState
    {
        None = 0,
        Prepared = 1,
        BackingUp = 2,
        BackedUp = 3,
        Replacing = 4,
        Verifying = 5,
        Committed = 6,
        RollingBack = 7,
        RolledBack = 8,
        Failed = 9
    }

    public enum UpdateFileActionType
    {
        Replace = 1,
        Add = 2,
        Delete = 3
    }

    public sealed class UpdateFileRecord
    {
        public string RelativePath { get; set; }
        public UpdateFileActionType ActionType { get; set; }
        public bool ExistedBefore { get; set; }
        public string BackupHash { get; set; }
        public string TargetNewHash { get; set; }
        public bool MutationStarted { get; set; }
        public string TemporaryPath { get; set; }
    }

    /// <summary>
    /// Nhật ký giao dịch cập nhật (durable journal) lưu trữ trên đĩa để hỗ trợ phục hồi khi gặp sự cố hoặc khôi phục (rollback).
    /// </summary>
    public sealed class UpdateJournal
    {
        public string SessionGuid { get; set; }
        public string TargetDirectory { get; set; }
        public string StagingDirectory { get; set; }
        public string BackupDirectory { get; set; }
        public string TargetVersion { get; set; }
        public UpdateTransactionState State { get; set; }
        public string CreatedUtc { get; set; }
        public List<UpdateFileRecord> FileActions { get; set; } = new List<UpdateFileRecord>();

        public const string JournalFileName = "journal.json";

        public static string GetJournalPath(string targetDirectory)
        {
            string canonical = Path.GetFullPath(targetDirectory).TrimEnd('\\', '/');
            string pathHash = UpdateSecurity.GetCanonicalPathHash(canonical);
            string stateDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TTSK Dim Plates",
                "Updater",
                pathHash
            );

            if (!Directory.Exists(stateDir))
            {
                Directory.CreateDirectory(stateDir);
            }

            return Path.Combine(stateDir, JournalFileName);
        }

        /// <summary>
        /// Lưu nhật ký atomic ra đĩa qua file tạm rồi đổi tên để tránh hỏng dữ liệu khi mất nguồn.
        /// </summary>
        public void SaveAtomic(string journalPath)
        {
            var serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(this);

            string dir = Path.GetDirectoryName(journalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            UpdateSecurity.AtomicWrite(journalPath, json);
        }

        public static UpdateJournal LoadFromFile(string journalPath)
        {
            if (!File.Exists(journalPath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(journalPath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                return serializer.Deserialize<UpdateJournal>(json);
            }
            catch (Exception ex) { throw new InvalidDataException("Cannot read recovery journal; keep backup and recover manually: " + journalPath, ex); }
        }

        public static void DeleteJournal(string journalPath)
        {
            try
            {
                if (File.Exists(journalPath))
                {
                    File.Delete(journalPath);
                }
            }
            catch { }
        }
    }
}
