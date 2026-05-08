using Microsoft.Data.Sqlite;
using SmartScanner.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SmartScanner.Services;

/// <summary>
/// Stores settings in SQLite.
/// Passwords are protected with Windows DPAPI (ProtectedData) — encrypted, not plaintext.
/// DPAPI is user-scoped: only the same Windows account can decrypt.
/// Note: True one-way hashing (bcrypt/SHA256) cannot be used for SMTP passwords
/// because the original password must be recovered for authentication.
/// </summary>
public class DatabaseService : ISettingsService, ISentItemsService
{
    private static readonly string _dbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SmartScanner", "smartscanner.db");

    public DatabaseService()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Settings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS SenderProfiles (
                Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                DisplayName        TEXT NOT NULL DEFAULT '',
                SenderEmail        TEXT NOT NULL DEFAULT '',
                Username           TEXT NOT NULL DEFAULT '',
                PasswordEncrypted  TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS Recipients (
                Id    INTEGER PRIMARY KEY AUTOINCREMENT,
                Name  TEXT NOT NULL DEFAULT '',
                Email TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS SentItems (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                SentAt    TEXT    NOT NULL DEFAULT '',
                FromName  TEXT    NOT NULL DEFAULT '',
                FromEmail TEXT    NOT NULL DEFAULT '',
                ToEmail   TEXT    NOT NULL DEFAULT '',
                Subject   TEXT    NOT NULL DEFAULT '',
                FileName  TEXT    NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS EmailProfiles (
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                Name           TEXT NOT NULL DEFAULT '',
                SenderUsername TEXT NOT NULL DEFAULT '',
                RecipientEmail TEXT NOT NULL DEFAULT '',
                Subject        TEXT NOT NULL DEFAULT '',
                Body           TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            using var conn = OpenConnection();

            // Key-value settings
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Key, Value FROM Settings";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    switch (r.GetString(0))
                    {
                        case "LastScanner":        settings.LastScanner = r.GetString(1); break;
                        case "SelectedDpi":        if (int.TryParse(r.GetString(1), out var d)) settings.SelectedDpi = d; break;
                        case "SelectedColorMode":  settings.SelectedColorMode = r.GetString(1); break;
                        case "SmtpHost":           settings.SmtpHost = r.GetString(1); break;
                        case "SmtpPort":           if (int.TryParse(r.GetString(1), out var p)) settings.SmtpPort = p; break;
                        case "SmtpUseSsl":         settings.SmtpUseSsl = r.GetString(1) == "1"; break;
                        case "LastSenderUsername": settings.LastSenderUsername = r.GetString(1); break;
                        case "PdfArchivePath":     settings.PdfArchivePath = r.GetString(1); break;
                        case "IsDarkMode":         settings.IsDarkMode = r.GetString(1) == "1"; break;
                    }
                }
            }

            // Sender profiles
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT DisplayName, SenderEmail, Username, PasswordEncrypted FROM SenderProfiles ORDER BY Id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    settings.Senders.Add(new SenderProfile
                    {
                        DisplayName = r.GetString(0),
                        SenderEmail = r.GetString(1),
                        Username    = r.GetString(2),
                        Password    = DecryptPassword(r.GetString(3)),
                    });
                }
            }

            // Recipients
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Name, Email FROM Recipients ORDER BY Id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    settings.Recipients.Add(new RecipientProfile
                    {
                        Name  = r.GetString(0),
                        Email = r.GetString(1),
                    });
                }
            }

            // Email profiles
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Name, SenderUsername, RecipientEmail, Subject, Body FROM EmailProfiles ORDER BY Id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    settings.EmailProfiles.Add(new EmailProfile
                    {
                        Name           = r.GetString(0),
                        SenderUsername = r.GetString(1),
                        RecipientEmail = r.GetString(2),
                        Subject        = r.GetString(3),
                        Body           = r.GetString(4),
                    });
                }
            }
        }
        catch { }
        return settings;
    }

    public void Save(AppSettings settings)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            Upsert(conn, tx, "LastScanner",        settings.LastScanner);
            Upsert(conn, tx, "SelectedDpi",        settings.SelectedDpi.ToString());
            Upsert(conn, tx, "SelectedColorMode",  settings.SelectedColorMode);
            Upsert(conn, tx, "SmtpHost",           settings.SmtpHost);
            Upsert(conn, tx, "SmtpPort",           settings.SmtpPort.ToString());
            Upsert(conn, tx, "SmtpUseSsl",         settings.SmtpUseSsl ? "1" : "0");
            Upsert(conn, tx, "LastSenderUsername", settings.LastSenderUsername);
            Upsert(conn, tx, "PdfArchivePath",     settings.PdfArchivePath);
            Upsert(conn, tx, "IsDarkMode",         settings.IsDarkMode ? "1" : "0");

            Execute(conn, tx, "DELETE FROM SenderProfiles");

            foreach (var s in settings.Senders)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO SenderProfiles (DisplayName, SenderEmail, Username, PasswordEncrypted)
                    VALUES ($dn, $se, $un, $pw)
                    """;
                cmd.Parameters.AddWithValue("$dn", s.DisplayName);
                cmd.Parameters.AddWithValue("$se", s.SenderEmail);
                cmd.Parameters.AddWithValue("$un", s.Username);
                cmd.Parameters.AddWithValue("$pw", EncryptPassword(s.Password));
                cmd.ExecuteNonQuery();
            }

            Execute(conn, tx, "DELETE FROM Recipients");

            foreach (var rc in settings.Recipients)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO Recipients (Name, Email) VALUES ($n, $e)";
                cmd.Parameters.AddWithValue("$n", rc.Name);
                cmd.Parameters.AddWithValue("$e", rc.Email);
                cmd.ExecuteNonQuery();
            }

            Execute(conn, tx, "DELETE FROM EmailProfiles");

            foreach (var ep in settings.EmailProfiles)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO EmailProfiles (Name, SenderUsername, RecipientEmail, Subject, Body)
                    VALUES ($n, $su, $re, $s, $b)
                    """;
                cmd.Parameters.AddWithValue("$n",  ep.Name);
                cmd.Parameters.AddWithValue("$su", ep.SenderUsername);
                cmd.Parameters.AddWithValue("$re", ep.RecipientEmail);
                cmd.Parameters.AddWithValue("$s",  ep.Subject);
                cmd.Parameters.AddWithValue("$b",  ep.Body);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ── ISentItemsService ─────────────────────────────────────────────────────

    public void Add(SentItem item)
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SentItems (SentAt, FromName, FromEmail, ToEmail, Subject, FileName)
            VALUES ($at, $fn, $fe, $te, $sub, $file)
            """;
        cmd.Parameters.AddWithValue("$at",   item.SentAt.ToString("o"));
        cmd.Parameters.AddWithValue("$fn",   item.FromName);
        cmd.Parameters.AddWithValue("$fe",   item.FromEmail);
        cmd.Parameters.AddWithValue("$te",   item.ToEmail);
        cmd.Parameters.AddWithValue("$sub",  item.Subject);
        cmd.Parameters.AddWithValue("$file", item.FileName);
        cmd.ExecuteNonQuery();
    }

    public List<SentItem> GetAll()
    {
        var list = new List<SentItem>();
        try
        {
            using var conn = OpenConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, SentAt, FromName, FromEmail, ToEmail, Subject, FileName FROM SentItems ORDER BY Id DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SentItem
                {
                    Id        = r.GetInt32(0),
                    SentAt    = DateTime.TryParse(r.GetString(1), out var dt) ? dt : DateTime.MinValue,
                    FromName  = r.GetString(2),
                    FromEmail = r.GetString(3),
                    ToEmail   = r.GetString(4),
                    Subject   = r.GetString(5),
                    FileName  = r.GetString(6),
                });
            }
        }
        catch { }
        return list;
    }

    public void Delete(int id)
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SentItems WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void ClearAll()
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM SentItems";
        cmd.ExecuteNonQuery();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void Upsert(SqliteConnection conn, SqliteTransaction tx, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO Settings (Key, Value) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    // Windows DPAPI — encrypted per Windows user account, not readable by other users
    private static string EncryptPassword(string password)
    {
        if (string.IsNullOrEmpty(password)) return string.Empty;
        try
        {
            var cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(password),
                null,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipher);
        }
        catch { return string.Empty; }
    }

    private static string DecryptPassword(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return string.Empty;
        try
        {
            var plain = ProtectedData.Unprotect(
                Convert.FromBase64String(encrypted),
                null,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return string.Empty; }
    }
}
