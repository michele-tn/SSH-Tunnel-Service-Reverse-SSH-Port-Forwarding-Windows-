using System;
using System.Data.SQLite;
using System.IO;

namespace SshTunnelService.Infrastructure
{
    public class DbLoggingService : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _connectionString;
        private readonly object _locker = new object();
        private bool _disposed;

        public DbLoggingService(string dbPath = null)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dbFolder = Path.Combine(baseDir, "Database");
            if (!Directory.Exists(dbFolder))
            {
                Directory.CreateDirectory(dbFolder);
            }

            _dbPath = string.IsNullOrWhiteSpace(dbPath)
                ? Path.Combine(dbFolder, "Logs.db")
                : dbPath;
            _connectionString = string.Format("Data Source={0};Version=3;Pooling=True;Max Pool Size=100;", _dbPath);
            EnsureDatabase();
        }

        private void EnsureDatabase()
        {
            lock (_locker)
            {
                var create = !File.Exists(_dbPath);
                if (create)
                {
                    SQLiteConnection.CreateFile(_dbPath);
                }

                using (var conn = new SQLiteConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Logs (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                                Level TEXT NOT NULL,
                                Source TEXT,
                                Message TEXT,
                                Exception TEXT
                            );";
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_logs_timestamp ON Logs(Timestamp);";
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        public void Info(string source, string message)
        {
            Write("INFO", source, message, null);
        }

        public void Error(string source, string message, Exception ex)
        {
            Write("ERROR", source, message, ex);
        }

        private void Write(string level, string source, string message, Exception ex)
        {
            try
            {
                lock (_locker)
                {
                    using (var conn = new SQLiteConnection(_connectionString))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "INSERT INTO Logs (Level, Source, Message, Exception) VALUES (@level, @source, @message, @exc);";
                            cmd.Parameters.AddWithValue("@level", level);
                            cmd.Parameters.AddWithValue("@source", source ?? string.Empty);
                            cmd.Parameters.AddWithValue("@message", message ?? string.Empty);
                            cmd.Parameters.AddWithValue("@exc", ex != null ? ex.ToString() : string.Empty);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch
            {
                // swallow logging errors
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
