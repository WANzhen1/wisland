using System.Runtime.InteropServices;
using System.Text;

namespace WislandSharp.Native;

/// <summary>
/// 直接调用 Windows 自带的 winsqlite3.dll，避免为了读通知数据库额外带一套 SQLite 运行时。
/// 只实现只读查询所需的最小 API。
/// </summary>
internal static class Sqlite3
{
    private const int SQLITE_OK = 0;
    private const int SQLITE_ROW = 100;
    private const int SQLITE_DONE = 101;
    private const int SQLITE_BUSY = 5;
    private const int SQLITE_LOCKED = 6;
    private const int SQLITE_OPEN_READONLY = 0x00000001;

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close_v2(IntPtr db);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(
        IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int nByte, out IntPtr stmt, IntPtr tail);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr stmt);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_reset(IntPtr stmt);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr stmt, int index);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern double sqlite3_column_double(IntPtr stmt, int index);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int index);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr stmt, int index);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_type(IntPtr stmt, int index);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_count(IntPtr stmt);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);

    internal sealed class Row
    {
        private readonly object?[] _values;
        public Row(object?[] values) => _values = values;

        public long GetInt64(int index)
            => _values[index] switch
            {
                long l => l,
                int i => i,
                double d => (long)d,
                _ => 0
            };

        public string GetString(int index)
            => _values[index] as string ?? "";

        public byte[] GetBytes(int index)
            => _values[index] switch
            {
                byte[] b => b,
                string s => Encoding.UTF8.GetBytes(s),
                _ => Array.Empty<byte>()
            };

        public object? Raw(int index) => _values[index];
    }

    public static List<Row> Query(string path, string sql)
    {
        if (!File.Exists(path)) return new List<Row>();
        int rc = sqlite3_open_v2(path, out IntPtr db, SQLITE_OPEN_READONLY, IntPtr.Zero);
        if (rc != SQLITE_OK)
        {
            if (db != IntPtr.Zero) sqlite3_close_v2(db);
            throw new InvalidOperationException($"无法打开通知数据库（sqlite rc={rc}）");
        }

        IntPtr stmt = IntPtr.Zero;
        try
        {
            sqlite3_busy_timeout(db, 1500);
            rc = sqlite3_prepare_v2(db, sql, -1, out stmt, IntPtr.Zero);
            if (rc != SQLITE_OK)
                throw new InvalidOperationException("通知数据库查询准备失败：" + ErrorMessage(db));

            var rows = new List<Row>();
            int retries = 0;
            while (true)
            {
                rc = sqlite3_step(stmt);
                if (rc == SQLITE_ROW)
                {
                    int count = sqlite3_column_count(stmt);
                    var values = new object?[count];
                    for (int i = 0; i < count; i++) values[i] = ReadColumn(stmt, i);
                    rows.Add(new Row(values));
                    continue;
                }
                if (rc == SQLITE_DONE) break;
                if ((rc == SQLITE_BUSY || rc == SQLITE_LOCKED) && retries++ < 5)
                {
                    sqlite3_reset(stmt);
                    Thread.Sleep(120);
                    continue;
                }
                throw new InvalidOperationException("读取通知数据库失败：" + ErrorMessage(db));
            }
            return rows;
        }
        finally
        {
            if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
            sqlite3_close_v2(db);
        }
    }

    private static object? ReadColumn(IntPtr stmt, int index)
    {
        int type = sqlite3_column_type(stmt, index);
        return type switch
        {
            1 => sqlite3_column_int64(stmt, index),
            2 => sqlite3_column_double(stmt, index),
            3 => Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, index)) ?? "",
            4 => CopyBlob(stmt, index),
            _ => null
        };
    }

    private static byte[] CopyBlob(IntPtr stmt, int index)
    {
        IntPtr ptr = sqlite3_column_blob(stmt, index);
        int length = sqlite3_column_bytes(stmt, index);
        if (ptr == IntPtr.Zero || length <= 0) return Array.Empty<byte>();
        byte[] bytes = new byte[length];
        Marshal.Copy(ptr, bytes, 0, length);
        return bytes;
    }

    private static string ErrorMessage(IntPtr db)
    {
        try { return Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) ?? "unknown"; }
        catch { return "unknown"; }
    }
}
