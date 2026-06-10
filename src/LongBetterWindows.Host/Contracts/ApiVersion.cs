namespace LongBetterWindows.Host.Contracts
{
    public readonly struct ApiVersion : IEquatable<ApiVersion>, IComparable<ApiVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public ApiVersion(int major, int minor, int patch = 0)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public static ApiVersion Current => new(1, 0, 0);

        public bool IsCompatibleWith(ApiVersion requested)
            => Major == requested.Major && Minor >= requested.Minor;

        public override string ToString() => $"v{Major}.{Minor}.{Patch}";

        public bool Equals(ApiVersion other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        public override bool Equals(object? obj) => obj is ApiVersion v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);

        public int CompareTo(ApiVersion other)
        {
            int cmp = Major.CompareTo(other.Major);
            if (cmp != 0) return cmp;
            cmp = Minor.CompareTo(other.Minor);
            if (cmp != 0) return cmp;
            return Patch.CompareTo(other.Patch);
        }

        public static bool operator ==(ApiVersion a, ApiVersion b) => a.Equals(b);
        public static bool operator !=(ApiVersion a, ApiVersion b) => !a.Equals(b);
        public static bool operator <(ApiVersion a, ApiVersion b) => a.CompareTo(b) < 0;
        public static bool operator >(ApiVersion a, ApiVersion b) => a.CompareTo(b) > 0;
        public static bool operator <=(ApiVersion a, ApiVersion b) => a.CompareTo(b) <= 0;
        public static bool operator >=(ApiVersion a, ApiVersion b) => a.CompareTo(b) >= 0;
    }
}
