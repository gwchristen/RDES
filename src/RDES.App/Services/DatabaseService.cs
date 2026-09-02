using Dapper;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Drawing.Charts;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Math;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using RDES.App.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace RDES.App.Services
{
    public class DatabaseService
    {
        private readonly ConfigService _configService;
        private string _dbPath;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _isInitialized = false;

        public string DatabasePath => _dbPath;
        public bool IsClientMode => _configService.IsClientMode;

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
                Mode = IsClientMode ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate,
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

                if (string.IsNullOrWhiteSpace(_dbPath) || !File.Exists(_dbPath))
                {
                    _isInitialized = false;
                    if (IsClientMode)
                    {
                        // In Client / Lite mode, do NOT create new local DB file
                        return;
                    }
                }

                if (IsClientMode)
                {
                    // In Client mode: connect to existing central DB
                    using var clientConn = await CreateConnectionAsync();
                    _isInitialized = true;
                    return;
                }

                // Host Mode: Ensure directory exists & create tables/schema
                string? dir = System.IO.Path.GetDirectoryName(_dbPath);
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

                // Seed default catalog options if empty
                await SeedInitialDataAsync(conn);

                _isInitialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task SeedInitialDataAsync(SqliteConnection conn)
        {
            // Seed default Defects
            var initialDefects = new List<(string Category, string Name)>
            {
                ("Accuracy Issue", "Accuracy Issue"),
                ("AMI function issue", "AMI function issue"),
                ("Assembly/Installation failure", "Assembly/Installation failure"),
                ("Bad interval data", "Bad interval data"),
                ("Battery failure", "Battery failure"),
                ("Communication failure", "Communication failure"),
                ("Configuration issue", "Configuration issue"),
                ("Corrosion", "Corrosion"),
                ("Damaged during shipping", "Damaged during shipping"),
                ("Display issue/No display", "Display issue/No display"),
                ("Disconnect not working", "Disconnect not working"),
                ("Error code 200", "Error code 200"),
                ("Fast blink on NIC", "Fast blink on NIC"),
                ("Label incorrect", "Label incorrect"),
                ("Meter - Broken base", "Meter - Broken base"),
                ("Meter - Blades recessed into base", "Meter - Blades recessed into base"),
                ("Meter - Blades split", "Meter - Blades split"),
                ("Meter - Blade Misaligned/Bent", "Meter - Blade Misaligned/Bent"),
                ("Meter - Damaged", "Meter - Damaged"),
                ("Meter - Damaged during install", "Meter - Damaged during install"),
                ("Meter - Dead", "Meter - Dead"),
                ("Meter - Displays ERROR", "Meter - Displays ERROR"),
                ("Meter - UIQ Event", "Meter - UIQ Event"),
                ("No Communication", "No Communication"),
                ("Not Programmed correctly", "Not Programmed correctly"),
                ("Other*", "Other*"),
                ("Program error", "Program error"),
                ("Rebadge", "Rebadge"),
                ("Registers not accumulating", "Registers not accumulating"),
                ("Repeat issue", "Repeat issue"),
                ("Serial number incorrect", "Serial number incorrect"),
                ("Shipping damage", "Shipping damage"),
                ("T-seals missing", "T-seals missing"),
                ("Wrong firmware", "Wrong firmware"),
                ("Won't power up", "Won't power up"),
                ("MTU - Out of Box (Install) - Cannot program on initial installation (please provide error code from Mobile Programmer) - AclaraConnect Document 000005301", "MTU - Out of Box (Install) - Cannot program on initial installation (please provide error code from Mobile Programmer) - AclaraConnect Document 000005301"),
                ("MTU - Out of Box (Install) - No Readings After Installation - AclaraConnect Document 000005305", "MTU - Out of Box (Install) - No Readings After Installation - AclaraConnect Document 000005305"),
                ("MTU - In Service - No or Sporadic Readings After Normal Operation - AclaraConnect Document 000005304", "MTU - In Service - No or Sporadic Readings After Normal Operation - AclaraConnect Document 000005304"),
                ("MTU - In Service - Zero Consumption (MTU transmits, but readings are 0) - AclaraConnect Document 000005306", "MTU - In Service - Zero Consumption (MTU transmits, but readings are 0) - AclaraConnect Document 000005306")

            };

            string insertDefectSql = @"
                INSERT OR IGNORE INTO DefectOptions (Category, Name, SortOrder, IsActive)
                VALUES (@Category, @Name, @SortOrder, 1);
            ";

            int order = 1;
            foreach (var (cat, name) in initialDefects)
            {
                await conn.ExecuteAsync(insertDefectSql, new { Category = cat, Name = name, SortOrder = order++ });
            }

            // Seed default OpCos
            var initialOpCos = new List<string>
            {
                "OH - RMA",
                "I&M - RMA",
                "OH - Special",
                "I&M - Special"
            };

            string insertOpCoSql = @"
                INSERT OR IGNORE INTO OpCoOptions (Name, SortOrder, IsActive)
                VALUES (@Name, @SortOrder, 1);
            ";

            order = 1;
            foreach (var opco in initialOpCos)
            {
                await conn.ExecuteAsync(insertOpCoSql, new { Name = opco, SortOrder = order++ });
            }
        }

        public async Task<(bool Success, string Message)> TestConnectionAsync(string? targetPath = null)
        {
            string pathToTest = targetPath ?? _dbPath;
            if (string.IsNullOrWhiteSpace(pathToTest))
            {
                return (false, IsClientMode 
                    ? "Central database path not configured. Please select the shared network database in Settings."
                    : "Database path is empty.");
            }

            if (!File.Exists(pathToTest))
            {
                return (false, IsClientMode 
                    ? $"Central database not found at '{pathToTest}'. Please ensure network share is accessible."
                    : $"Database file does not exist at '{pathToTest}'. It will be created on first save.");
            }

            try
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = pathToTest,
                    Mode = SqliteOpenMode.ReadWrite,
                    DefaultTimeout = 5
                };

                using var conn = new SqliteConnection(builder.ToString());
                await conn.OpenAsync();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DeviceRecords';";
                var result = await cmd.ExecuteScalarAsync();
                long count = result != null && result != DBNull.Value ? Convert.ToInt64(result) : 0;

                if (count == 0)
                {
                    return (false, IsClientMode 
                        ? "Connected to file, but central tables (DeviceRecords) were not found."
                        : "Connected, but tables not yet created.");
                }

                cmd.CommandText = "SELECT COUNT(*) FROM DeviceRecords;";
                var recCount = await cmd.ExecuteScalarAsync();
                long records = recCount != null && recCount != DBNull.Value ? Convert.ToInt64(recCount) : 0;

                return (true, $"✅ Successfully connected to shared database! ({records:N0} records found)");
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
                string destPath = System.IO.Path.Combine(destinationFolder, backupFileName);

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
