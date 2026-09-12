using System;

namespace TTSK_AutoDim_Plates.Updater
{
    /// <summary>
    /// Đại diện cho phiên bản cập nhật dạng Major.Minor.Patch (ví dụ: 1.0.15 hoặc v1.0.15).
    /// Hỗ trợ so sánh số học chính xác và chống hạ cấp (downgrade).
    /// </summary>
    public sealed class UpdateVersion : IComparable<UpdateVersion>, IEquatable<UpdateVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public UpdateVersion(int major, int minor, int patch)
        {
            if (major < 0 || minor < 0 || patch < 0)
            {
                throw new ArgumentOutOfRangeException("Các thành phần phiên bản không được là số âm.");
            }

            Major = major;
            Minor = minor;
            Patch = patch;
        }

        /// <summary>
        /// Thử phân tích chuỗi phiên bản dạng 1.0.15 hoặc v1.0.15.
        /// Bắt buộc phải có đúng 3 thành phần số học không âm.
        /// </summary>
        public static bool TryParse(string input, out UpdateVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string trimmed = input.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(1).Trim();
            }

            string[] parts = trimmed.Split('.');
            if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z")) return false;
            if (parts.Length != 3)
            {
                return false;
            }

            int major, minor, patch;
            if (!int.TryParse(parts[0], out major) || major < 0 ||
                !int.TryParse(parts[1], out minor) || minor < 0 ||
                !int.TryParse(parts[2], out patch) || patch < 0)
            {
                return false;
            }

            // Chặn các trường hợp có ký tự lạ hoặc khoảng trắng bên trong số
            if (parts[0].Trim() != parts[0] || parts[1].Trim() != parts[1] || parts[2].Trim() != parts[2])
            {
                return false;
            }

            version = new UpdateVersion(major, minor, patch);
            return true;
        }

        public static UpdateVersion Parse(string input)
        {
            if (TryParse(input, out UpdateVersion version))
            {
                return version;
            }

            throw new FormatException(string.Format("Định dạng phiên bản không hợp lệ: '{0}'. Yêu cầu dạng X.Y.Z hoặc vX.Y.Z.", input));
        }

        public int CompareTo(UpdateVersion other)
        {
            if (ReferenceEquals(other, null)) return 1;
            if (ReferenceEquals(this, other)) return 0;

            int cmp = Major.CompareTo(other.Major);
            if (cmp != 0) return cmp;

            cmp = Minor.CompareTo(other.Minor);
            if (cmp != 0) return cmp;

            return Patch.CompareTo(other.Patch);
        }

        public bool Equals(UpdateVersion other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as UpdateVersion);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Major.GetHashCode();
                hash = hash * 31 + Minor.GetHashCode();
                hash = hash * 31 + Patch.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format("{0}.{1}.{2}", Major, Minor, Patch);
        }

        public string ToDisplayString()
        {
            return string.Format("v{0}.{1}.{2}", Major, Minor, Patch);
        }

        public static bool operator ==(UpdateVersion left, UpdateVersion right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null);
            return left.Equals(right);
        }

        public static bool operator !=(UpdateVersion left, UpdateVersion right)
        {
            return !(left == right);
        }

        public static bool operator <(UpdateVersion left, UpdateVersion right)
        {
            if (ReferenceEquals(left, null)) return !ReferenceEquals(right, null);
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(UpdateVersion left, UpdateVersion right)
        {
            if (ReferenceEquals(left, null)) return true;
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(UpdateVersion left, UpdateVersion right)
        {
            if (ReferenceEquals(left, null)) return false;
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(UpdateVersion left, UpdateVersion right)
        {
            if (ReferenceEquals(left, null)) return ReferenceEquals(right, null);
            return left.CompareTo(right) >= 0;
        }
    }
}
