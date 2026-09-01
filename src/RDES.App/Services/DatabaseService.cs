using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class DatabaseService
    {
        private readonly ConfigService _configService;
        private string _dbPath;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _isInitialized = false;

        public string DatabasePath => _dbPath;

        public DatabaseService(ConfigService configService)
        {
            _configService = configService;
            _dbPath = _configService.CurrentConfig.DatabasePath;
        }

        public void UpdateDatabasePath(string newPath)
        {
            _dbPath = newPath;
            _isInitialized = false;
        }

        public string GetConnectionString()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                DefaultTimeout = 15
            };
            return builder.ToString();
        }

        public SqliteConnection CreateConnection()
        {
            var conn = new SqliteConnection(GetConnectionString());
            conn.Open();

            // Set SQLite performance & concurrency PRAGMAs for multi-user shared drive access
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 10000;
                PRAGMA synchronous = NORMAL;
                PRAGMA foreign_keys = ON;
            ";
            cmd.ExecuteNonQuery();

            return conn;
        }

        public async Task<SqliteConnection> CreateConnectionAsync()
        {
            var conn = new SqliteConnection(GetConnectionString());
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA busy_timeout = 10000;
                PRAGMA synchronous = NORMAL;
                PRAGMA foreign_keys = ON;
            ";
            await cmd.ExecuteNonQueryAsync();

            return conn;
        }

        public async Task EnsureDatabaseInitializedAsync()
        {
            if (_isInitialized) return;

            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized) return;

                // Ensure directory exists
                string? dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var conn = await CreateConnectionAsync();

                string createTablesSql = @"
                    CREATE TABLE IF NOT EXISTS DeviceRecords (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SerialNumber TEXT NOT NULL,
                        ModuleNumber TEXT,
                        Defect TEXT,
                        DeviceCode TEXT,
                        ManufacturerCode TEXT,
                        MfgDate TEXT,
                        ModType TEXT,
                        ModNumber TEXT,
                        Problem TEXT,
                        OtherProblem TEXT,
                        RecordType TEXT,
                        Catalog TEXT,
                        FileNumber TEXT,
                        Status TEXT NOT NULL DEFAULT 'Pending',
                        OpCo TEXT NOT NULL DEFAULT 'OH - RMA',
                        AclaraSerialStart TEXT,
                        AclaraSerialEnd TEXT,
                        CustomerSerialNumber TEXT,
                        MaterialGroup TEXT,
                        FailureLocation TEXT,
                        CustomerIssue TEXT,
                        CustomerInput TEXT,
                        Quantity INTEGER NOT NULL DEFAULT 1,
                        Notes TEXT,
                        CreatedBy TEXT NOT NULL,
                        CreatedAt DATETIME NOT NULL,
                        UpdatedBy TEXT NOT NULL,
                        UpdatedAt DATETIME NOT NULL,
                        MachineName TEXT
                    );

                    CREATE INDEX IF NOT EXISTS idx_records_serial ON DeviceRecords (SerialNumber);
                    CREATE INDEX IF NOT EXISTS idx_records_status ON DeviceRecords (Status);
                    CREATE INDEX IF NOT EXISTS idx_records_opco ON DeviceRecords (OpCo);
                    CREATE INDEX IF NOT EXISTS idx_records_created ON DeviceRecords (CreatedAt);
                    CREATE INDEX IF NOT EXISTS idx_records_defect ON DeviceRecords (Defect);

                    CREATE TABLE IF NOT EXISTS DefectOptions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Category TEXT NOT NULL DEFAULT 'General',
                        Name TEXT NOT NULL UNIQUE,
                        Description TEXT,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        IsActive INTEGER NOT NULL DEFAULT 1
                    );

                    CREATE TABLE IF NOT EXISTS OpCoOptions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        IsActive INTEGER NOT NULL DEFAULT 1
                    );

                    CREATE TABLE IF NOT EXISTS AuditLogs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RecordId INTEGER,
                        Action TEXT NOT NULL,
                        Details TEXT,
                        UserName TEXT NOT NULL,
                        MachineName TEXT,
                        Timestamp DATETIME NOT NULL
                    );
                ";

                await conn.ExecuteAsync(createTablesSql);

                // Migration: Ensure OpCo column exists in older database files
                try
                {
                    await conn.ExecuteAsync("ALTER TABLE DeviceRecords ADD COLUMN OpCo TEXT DEFAULT 'OH - RMA';");
                }
                catch
                {
                    // Column already exists
                }

                // Seed default defect options if table is empty
                int count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM DefectOptions;");
                if (count == 0)
                {
                    await SeedDefectOptionsAsync(conn);
                }

                // Seed OpCo options if table is empty
                int opcoCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM OpCoOptions;");
                if (opcoCount == 0)
                {
                    await SeedOpCoOptionsAsync(conn);
                }

                _isInitialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task SeedDefectOptionsAsync(SqliteConnection conn)
        {
            var initialDefects = new List<string>
            {
                "Accuracy Issue",
                "AMI function issue",
                "Assembly/Installation failure",
                "Bad interval data",
                "Battery failure",
                "Display issue/No display",
                "Disconnect not working",
                "Error code 200",
                "Fast blink on NIC",
                "Label incorrect",
                "Meter - Broken base",
                "Meter - Blades recessed into base",
                "Meter - Blades split",
                "Meter - Blade Misaligned/Bent",
                "Meter - Damaged",
                "Meter - Damaged during install",
                "Meter - Dead",
                "Meter - Displays ERROR",
                "Meter - UIQ Event",
                "No Communication",
                "Not Programmed correctly",
                "Other*",
                "Program error",
                "Rebadge",
                "Registers not accumulating",
                "Repeat issue",
                "Serial number incorrect",
                "Shipping damage",
                "T-seals missing",
                "Wrong firmware",
                "Won't power up",
                "MTU - Out of Box (Install) - Cannot program on initial installation",
                "MTU - Out of Box (Install) - No Readings After Installation",
                "MTU - In Service - No or Sporadic Readings After Normal Operation",
                "MTU - In Service - Zero Consumption (MTU transmits, but readings are 0)"
            };

            string insertSql = "INSERT OR IGNORE INTO DefectOptions (Category, Name, SortOrder, IsActive) VALUES (@Category, @Name, @SortOrder, 1);";
            int order = 1;
            foreach (var defect in initialDefects)
            {
                await conn.ExecuteAsync(insertSql, new { Category = "General", Name = defect, SortOrder = order++ });
            }
        }

        private async Task SeedOpCoOptionsAsync(SqliteConnection conn)
        {
            var initialOpCos = new List<string>
            {
                "OH - RMA",
                "I&M - RMA",
                "OH - Special",
                "I&M - Special"
            };

            string insertSql = "INSERT OR IGNORE INTO OpCoOptions (Name, SortOrder, IsActive) VALUES (@Name, @SortOrder, 1);";
            int order = 1;
            foreach (var opco in initialOpCos)
            {
                await conn.ExecuteAsync(insertSql, new { Name = opco, SortOrder = order++ });
            }
        }

        public async Task<(bool Success, string Message)> TestConnectionAsync(string? targetPath = null)
        {
            string pathToTest = targetPath ?? _dbPath;
            try
            {
                string? dir = Path.GetDirectoryName(pathToTest);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = pathToTest,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    DefaultTimeout = 5
                };

                using var conn = new SqliteConnection(builder.ToString());
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1;";
                var result = await cmd.ExecuteScalarAsync();

                return (true, "Successfully connected to the database!");
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> BackupDatabaseAsync(string destinationFolder)
        {
            try
            {
                if (!File.Exists(_dbPath))
                {
                    return (false, "Database file does not exist yet.");
                }

                if (!Directory.Exists(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFileName = $"RDES_Backup_{timestamp}.db";
                string destPath = Path.Combine(destinationFolder, backupFileName);

                using var srcConn = await CreateConnectionAsync();
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = destPath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                };
                using var destConn = new SqliteConnection(builder.ToString());
                await destConn.OpenAsync();

                srcConn.BackupDatabase(destConn);

                return (true, $"Backup created successfully at: {destPath}");
            }
            catch (Exception ex)
            {
                return (false, $"Backup failed: {ex.Message}");
            }
        }
    }
}
