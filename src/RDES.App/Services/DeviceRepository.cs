using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class DeviceRepository
    {
        private readonly DatabaseService _databaseService;
        private const int MaxRetries = 5;
        private const int InitialDelayMs = 100;

        public DeviceRepository(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        private async Task<T> ExecuteWithRetryAsync<T>(Func<SqliteConnection, Task<T>> action)
        {
            await _databaseService.EnsureDatabaseInitializedAsync();

            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    using var conn = await _databaseService.CreateConnectionAsync();
                    return await action(conn);
                }
                catch (SqliteException ex) when (IsLockException(ex) && attempt <= MaxRetries)
                {
                    int delay = InitialDelayMs * (int)Math.Pow(2, attempt - 1) + Random.Shared.Next(10, 50);
                    await Task.Delay(delay);
                }
            }
        }

        private async Task ExecuteWithRetryAsync(Func<SqliteConnection, Task> action)
        {
            await _databaseService.EnsureDatabaseInitializedAsync();

            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    using var conn = await _databaseService.CreateConnectionAsync();
                    await action(conn);
                    return;
                }
                catch (SqliteException ex) when (IsLockException(ex) && attempt <= MaxRetries)
                {
                    int delay = InitialDelayMs * (int)Math.Pow(2, attempt - 1) + Random.Shared.Next(10, 50);
                    await Task.Delay(delay);
                }
            }
        }

        private static bool IsLockException(SqliteException ex)
        {
            // SQLite error 5 = SQLITE_BUSY, 6 = SQLITE_LOCKED
            return ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6 ||
                   ex.Message.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<long> InsertRecordAsync(DeviceRecord record)
        {
            record.CreatedBy = Environment.UserName;
            record.CreatedAt = DateTime.Now;
            record.UpdatedBy = Environment.UserName;
            record.UpdatedAt = DateTime.Now;
            record.MachineName = Environment.MachineName;

            return await ExecuteWithRetryAsync(async conn =>
            {
                using var trans = conn.BeginTransaction();

                string sql = @"
                    INSERT INTO DeviceRecords (
                        SerialNumber, ModuleNumber, Defect, DeviceCode, ManufacturerCode,
                        MfgDate, ModType, ModNumber, Problem, OtherProblem,
                        RecordType, Catalog, FileNumber, Status, OpCo, AclaraSerialStart,
                        AclaraSerialEnd, CustomerSerialNumber, MaterialGroup, FailureLocation,
                        CustomerIssue, CustomerInput, Quantity, Notes, CreatedBy,
                        CreatedAt, UpdatedBy, UpdatedAt, MachineName
                    ) VALUES (
                        @SerialNumber, @ModuleNumber, @Defect, @DeviceCode, @ManufacturerCode,
                        @MfgDate, @ModType, @ModNumber, @Problem, @OtherProblem,
                        @RecordType, @Catalog, @FileNumber, @Status, @OpCo, @AclaraSerialStart,
                        @AclaraSerialEnd, @CustomerSerialNumber, @MaterialGroup, @FailureLocation,
                        @CustomerIssue, @CustomerInput, @Quantity, @Notes, @CreatedBy,
                        @CreatedAt, @UpdatedBy, @UpdatedAt, @MachineName
                    );
                    SELECT last_insert_rowid();
                ";

                long newId = await conn.ExecuteScalarAsync<long>(sql, record, trans);
                record.Id = newId;

                // Log audit
                string auditSql = @"
                    INSERT INTO AuditLogs (RecordId, Action, Details, UserName, MachineName, Timestamp)
                    VALUES (@RecordId, 'INSERT', @Details, @UserName, @MachineName, @Timestamp);
                ";
                await conn.ExecuteAsync(auditSql, new
                {
                    RecordId = newId,
                    Details = $"Created record for Serial: {record.SerialNumber}, OpCo: {record.OpCo}, Defect: {record.Defect}",
                    UserName = Environment.UserName,
                    MachineName = Environment.MachineName,
                    Timestamp = DateTime.Now
                }, trans);

                trans.Commit();
                return newId;
            });
        }

        public async Task<bool> UpdateRecordAsync(DeviceRecord record)
        {
            record.UpdatedBy = Environment.UserName;
            record.UpdatedAt = DateTime.Now;
            record.MachineName = Environment.MachineName;

            return await ExecuteWithRetryAsync(async conn =>
            {
                using var trans = conn.BeginTransaction();

                string sql = @"
                    UPDATE DeviceRecords SET
                        SerialNumber = @SerialNumber,
                        ModuleNumber = @ModuleNumber,
                        Defect = @Defect,
                        DeviceCode = @DeviceCode,
                        ManufacturerCode = @ManufacturerCode,
                        MfgDate = @MfgDate,
                        ModType = @ModType,
                        ModNumber = @ModNumber,
                        Problem = @Problem,
                        OtherProblem = @OtherProblem,
                        RecordType = @RecordType,
                        Catalog = @Catalog,
                        FileNumber = @FileNumber,
                        Status = @Status,
                        OpCo = @OpCo,
                        AclaraSerialStart = @AclaraSerialStart,
                        AclaraSerialEnd = @AclaraSerialEnd,
                        CustomerSerialNumber = @CustomerSerialNumber,
                        MaterialGroup = @MaterialGroup,
                        FailureLocation = @FailureLocation,
                        CustomerIssue = @CustomerIssue,
                        CustomerInput = @CustomerInput,
                        Quantity = @Quantity,
                        Notes = @Notes,
                        UpdatedBy = @UpdatedBy,
                        UpdatedAt = @UpdatedAt,
                        MachineName = @MachineName
                    WHERE Id = @Id;
                ";

                int affected = await conn.ExecuteAsync(sql, record, trans);

                if (affected > 0)
                {
                    string auditSql = @"
                        INSERT INTO AuditLogs (RecordId, Action, Details, UserName, MachineName, Timestamp)
                        VALUES (@RecordId, 'UPDATE', @Details, @UserName, @MachineName, @Timestamp);
                    ";
                    await conn.ExecuteAsync(auditSql, new
                    {
                        RecordId = record.Id,
                        Details = $"Updated record for Serial: {record.SerialNumber}, OpCo: {record.OpCo}, Defect: {record.Defect}",
                        UserName = Environment.UserName,
                        MachineName = Environment.MachineName,
                        Timestamp = DateTime.Now
                    }, trans);
                }

                trans.Commit();
                return affected > 0;
            });
        }

        public async Task<bool> DeleteRecordAsync(long id)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                using var trans = conn.BeginTransaction();

                string deleteSql = "DELETE FROM DeviceRecords WHERE Id = @Id;";
                int affected = await conn.ExecuteAsync(deleteSql, new { Id = id }, trans);

                if (affected > 0)
                {
                    string auditSql = @"
                        INSERT INTO AuditLogs (RecordId, Action, Details, UserName, MachineName, Timestamp)
                        VALUES (@RecordId, 'DELETE', @Details, @UserName, @MachineName, @Timestamp);
                    ";
                    await conn.ExecuteAsync(auditSql, new
                    {
                        RecordId = id,
                        Details = $"Deleted record Id: {id}",
                        UserName = Environment.UserName,
                        MachineName = Environment.MachineName,
                        Timestamp = DateTime.Now
                    }, trans);
                }

                trans.Commit();
                return affected > 0;
            });
        }

        public async Task<int> UpdateStatusBatchAsync(IEnumerable<long> ids, string newStatus)
        {
            var idList = ids.ToList();
            if (idList.Count == 0) return 0;

            return await ExecuteWithRetryAsync(async conn =>
            {
                using var trans = conn.BeginTransaction();
                string currentUser = Environment.UserName;
                string currentMachine = Environment.MachineName;
                DateTime now = DateTime.Now;

                string sql = @"
                    UPDATE DeviceRecords SET
                        Status = @Status,
                        UpdatedBy = @UpdatedBy,
                        UpdatedAt = @UpdatedAt,
                        MachineName = @MachineName
                    WHERE Id IN @Ids;
                ";

                int affected = await conn.ExecuteAsync(sql, new
                {
                    Status = newStatus,
                    UpdatedBy = currentUser,
                    UpdatedAt = now,
                    MachineName = currentMachine,
                    Ids = idList
                }, trans);

                if (affected > 0)
                {
                    string auditSql = @"
                        INSERT INTO AuditLogs (RecordId, Action, Details, UserName, MachineName, Timestamp)
                        VALUES (NULL, 'STATUS_UPDATE', @Details, @UserName, @MachineName, @Timestamp);
                    ";
                    await conn.ExecuteAsync(auditSql, new
                    {
                        Details = $"Marked {affected} record(s) as '{newStatus}'",
                        UserName = currentUser,
                        MachineName = currentMachine,
                        Timestamp = now
                    }, trans);
                }

                trans.Commit();
                return affected;
            });
        }

        public async Task<DeviceRecord?> GetByIdAsync(long id)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                string sql = "SELECT * FROM DeviceRecords WHERE Id = @Id LIMIT 1;";
                return await conn.QueryFirstOrDefaultAsync<DeviceRecord>(sql, new { Id = id });
            });
        }

        public async Task<DeviceRecord?> GetBySerialNumberAsync(string serialNumber)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                string sql = "SELECT * FROM DeviceRecords WHERE SerialNumber = @SerialNumber ORDER BY Id DESC LIMIT 1;";
                return await conn.QueryFirstOrDefaultAsync<DeviceRecord>(sql, new { SerialNumber = serialNumber.Trim() });
            });
        }

        public async Task<List<DeviceRecord>> SearchRecordsAsync(
            string? query = null,
            string? status = null,
            string? defect = null,
            string? opCo = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            int limit = 500)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                var builder = new System.Text.StringBuilder("SELECT * FROM DeviceRecords WHERE 1=1");
                var parameters = new DynamicParameters();

                if (!string.IsNullOrWhiteSpace(query))
                {
                    builder.Append(@" AND (
                        SerialNumber LIKE @Query
                        OR ModuleNumber LIKE @Query
                        OR Defect LIKE @Query
                        OR Problem LIKE @Query
                        OR OtherProblem LIKE @Query
                        OR Catalog LIKE @Query
                        OR CustomerInput LIKE @Query
                        OR Notes LIKE @Query
                        OR OpCo LIKE @Query
                        OR CreatedBy LIKE @Query
                        OR UpdatedBy LIKE @Query
                    )");
                    parameters.Add("Query", $"%{query.Trim()}%");
                }

                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                {
                    builder.Append(" AND Status = @Status");
                    parameters.Add("Status", status);
                }

                if (!string.IsNullOrWhiteSpace(defect) && defect != "All")
                {
                    builder.Append(" AND Defect = @Defect");
                    parameters.Add("Defect", defect);
                }

                if (!string.IsNullOrWhiteSpace(opCo) && opCo != "All")
                {
                    builder.Append(" AND OpCo = @OpCo");
                    parameters.Add("OpCo", opCo);
                }

                if (fromDate.HasValue)
                {
                    builder.Append(" AND CreatedAt >= @FromDate");
                    parameters.Add("FromDate", fromDate.Value.Date);
                }

                if (toDate.HasValue)
                {
                    builder.Append(" AND CreatedAt <= @ToDate");
                    parameters.Add("ToDate", toDate.Value.Date.AddDays(1).AddTicks(-1));
                }

                builder.Append(" ORDER BY Id DESC LIMIT @Limit");
                parameters.Add("Limit", limit);

                var results = await conn.QueryAsync<DeviceRecord>(builder.ToString(), parameters);
                return results.ToList();
            });
        }

        public async Task<List<DeviceRecord>> GetRecentRecordsAsync(int count = 10)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                string sql = "SELECT * FROM DeviceRecords ORDER BY Id DESC LIMIT @Count;";
                var results = await conn.QueryAsync<DeviceRecord>(sql, new { Count = count });
                return results.ToList();
            });
        }

        public async Task<List<DefectOption>> GetDefectOptionsAsync()
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                string sql = "SELECT * FROM DefectOptions WHERE IsActive = 1 ORDER BY SortOrder ASC, Name ASC;";
                var results = await conn.QueryAsync<DefectOption>(sql);
                return results.ToList();
            });
        }

        public async Task<bool> AddDefectOptionAsync(DefectOption defect)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                string sql = @"
                    INSERT OR IGNORE INTO DefectOptions (Category, Name, Description, SortOrder, IsActive)
                    VALUES (@Category, @Name, @Description, @SortOrder, 1);
                ";
                int affected = await conn.ExecuteAsync(sql, defect);
                return affected > 0;
            });
        }

        public async Task<bool> DeleteDefectOptionAsync(long id)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                string sql = "UPDATE DefectOptions SET IsActive = 0 WHERE Id = @Id;";
                int affected = await conn.ExecuteAsync(sql, new { Id = id });
                return affected > 0;
            });
        }

        public async Task<List<OpCoOption>> GetOpCoOptionsAsync()
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                string sql = "SELECT * FROM OpCoOptions WHERE IsActive = 1 ORDER BY SortOrder ASC, Name ASC;";
                var results = await conn.QueryAsync<OpCoOption>(sql);
                return results.ToList();
            });
        }

        public async Task<bool> AddOpCoOptionAsync(OpCoOption opco)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                string sql = @"
                    INSERT OR IGNORE INTO OpCoOptions (Name, SortOrder, IsActive)
                    VALUES (@Name, @SortOrder, 1);
                ";
                int affected = await conn.ExecuteAsync(sql, opco);
                return affected > 0;
            });
        }

        public async Task<bool> DeleteOpCoOptionAsync(long id)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                string sql = "UPDATE OpCoOptions SET IsActive = 0 WHERE Id = @Id;";
                int affected = await conn.ExecuteAsync(sql, new { Id = id });
                return affected > 0;
            });
        }

        public async Task<ImportResult> BulkInsertAsync(IEnumerable<DeviceRecord> records, bool overwriteDuplicates = false)
        {
            var result = new ImportResult();
            var recordList = records.ToList();
            result.TotalRead = recordList.Count;

            if (recordList.Count == 0) return result;

            await ExecuteWithRetryAsync(async conn =>
            {
                using var trans = conn.BeginTransaction();
                string currentUser = Environment.UserName;
                string currentMachine = Environment.MachineName;
                DateTime now = DateTime.Now;

                foreach (var record in recordList)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(record.SerialNumber))
                        {
                            result.Errors.Add("Skipped record with empty Serial Number.");
                            continue;
                        }

                        var existing = await conn.QueryFirstOrDefaultAsync<DeviceRecord>(
                            "SELECT Id FROM DeviceRecords WHERE SerialNumber = @SerialNumber LIMIT 1;",
                            new { record.SerialNumber }, trans);

                        if (existing != null)
                        {
                            if (overwriteDuplicates)
                            {
                                record.Id = existing.Id;
                                record.UpdatedBy = currentUser;
                                record.UpdatedAt = now;
                                record.MachineName = currentMachine;

                                string updateSql = @"
                                    UPDATE DeviceRecords SET
                                        ModuleNumber = @ModuleNumber,
                                        Defect = @Defect,
                                        DeviceCode = @DeviceCode,
                                        ManufacturerCode = @ManufacturerCode,
                                        MfgDate = @MfgDate,
                                        ModType = @ModType,
                                        ModNumber = @ModNumber,
                                        Problem = @Problem,
                                        OtherProblem = @OtherProblem,
                                        RecordType = @RecordType,
                                        Catalog = @Catalog,
                                        FileNumber = @FileNumber,
                                        Status = @Status,
                                        OpCo = @OpCo,
                                        AclaraSerialStart = @AclaraSerialStart,
                                        AclaraSerialEnd = @AclaraSerialEnd,
                                        CustomerSerialNumber = @CustomerSerialNumber,
                                        MaterialGroup = @MaterialGroup,
                                        FailureLocation = @FailureLocation,
                                        CustomerIssue = @CustomerIssue,
                                        CustomerInput = @CustomerInput,
                                        Quantity = @Quantity,
                                        Notes = @Notes,
                                        UpdatedBy = @UpdatedBy,
                                        UpdatedAt = @UpdatedAt,
                                        MachineName = @MachineName
                                    WHERE Id = @Id;
                                ";
                                await conn.ExecuteAsync(updateSql, record, trans);
                                result.UpdatedCount++;
                            }
                            else
                            {
                                result.SkippedDuplicates++;
                            }
                        }
                        else
                        {
                            record.CreatedBy = currentUser;
                            record.CreatedAt = now;
                            record.UpdatedBy = currentUser;
                            record.UpdatedAt = now;
                            record.MachineName = currentMachine;

                            string insertSql = @"
                                INSERT INTO DeviceRecords (
                                    SerialNumber, ModuleNumber, Defect, DeviceCode, ManufacturerCode,
                                    MfgDate, ModType, ModNumber, Problem, OtherProblem,
                                    RecordType, Catalog, FileNumber, Status, OpCo, AclaraSerialStart,
                                    AclaraSerialEnd, CustomerSerialNumber, MaterialGroup, FailureLocation,
                                    CustomerIssue, CustomerInput, Quantity, Notes, CreatedBy,
                                    CreatedAt, UpdatedBy, UpdatedAt, MachineName
                                ) VALUES (
                                    @SerialNumber, @ModuleNumber, @Defect, @DeviceCode, @ManufacturerCode,
                                    @MfgDate, @ModType, @ModNumber, @Problem, @OtherProblem,
                                    @RecordType, @Catalog, @FileNumber, @Status, @OpCo, @AclaraSerialStart,
                                    @AclaraSerialEnd, @CustomerSerialNumber, @MaterialGroup, @FailureLocation,
                                    @CustomerIssue, @CustomerInput, @Quantity, @Notes, @CreatedBy,
                                    @CreatedAt, @UpdatedBy, @UpdatedAt, @MachineName
                                );
                            ";
                            await conn.ExecuteAsync(insertSql, record, trans);
                            result.InsertedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Error on serial '{record.SerialNumber}': {ex.Message}");
                    }
                }

                // Log bulk audit
                string bulkAuditSql = @"
                    INSERT INTO AuditLogs (RecordId, Action, Details, UserName, MachineName, Timestamp)
                    VALUES (NULL, 'BULK_IMPORT', @Details, @UserName, @MachineName, @Timestamp);
                ";
                await conn.ExecuteAsync(bulkAuditSql, new
                {
                    Details = $"Bulk imported: {result.InsertedCount} inserted, {result.UpdatedCount} updated, {result.SkippedDuplicates} skipped duplicates.",
                    UserName = currentUser,
                    MachineName = currentMachine,
                    Timestamp = now
                }, trans);

                trans.Commit();
            });

            return result;
        }

        public async Task<(int TotalCount, int TodayCount, int PendingCount)> GetStatisticsAsync()
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                int total = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM DeviceRecords;");
                DateTime today = DateTime.Today;
                int todayCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM DeviceRecords WHERE CreatedAt >= @Today;", new { Today = today });
                int pendingCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM DeviceRecords WHERE Status = 'Pending';");

                return (total, todayCount, pendingCount);
            });
        }

        public async Task<List<string>> GetDistinctUsersAsync()
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                var users = await conn.QueryAsync<string>("SELECT DISTINCT CreatedBy FROM DeviceRecords WHERE CreatedBy IS NOT NULL AND CreatedBy != '' ORDER BY CreatedBy;");
                return users.ToList();
            });
        }

        public async Task<List<string>> GetDistinctDeviceCodesAsync()
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                var codes = await conn.QueryAsync<string>("SELECT DISTINCT DeviceCode FROM DeviceRecords WHERE DeviceCode IS NOT NULL AND DeviceCode != '' ORDER BY DeviceCode;");
                return codes.ToList();
            });
        }

        public async Task<StatisticsSummary> GetFilteredSummaryMetricsAsync(
            DateTime? fromDate,
            DateTime? toDate,
            string? opCo,
            string? user,
            string? defect,
            string? deviceCode,
            string? status)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                var (whereClause, parameters) = BuildFilterClause(fromDate, toDate, opCo, user, defect, deviceCode, status);

                string sql = $@"
                    SELECT 
                        COUNT(*) AS TotalCount,
                        COALESCE(SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END), 0) AS PendingCount,
                        COALESCE(SUM(CASE WHEN Status = 'Submitted' THEN 1 ELSE 0 END), 0) AS SubmittedCount,
                        COUNT(DISTINCT SerialNumber) AS UniqueSerialsCount,
                        COUNT(DISTINCT CreatedBy) AS UniqueUsersCount,
                        COUNT(DISTINCT OpCo) AS UniqueOpCosCount
                    FROM DeviceRecords
                    {whereClause};
                ";

                var summary = await conn.QueryFirstOrDefaultAsync<StatisticsSummary>(sql, parameters) ?? new StatisticsSummary();

                // Top defect
                string topDefectSql = $@"
                    SELECT COALESCE(Defect, 'Unspecified') 
                    FROM DeviceRecords 
                    {whereClause} 
                    AND Defect IS NOT NULL AND Defect != ''
                    GROUP BY Defect 
                    ORDER BY COUNT(*) DESC 
                    LIMIT 1;
                ";
                summary.TopDefect = await conn.QueryFirstOrDefaultAsync<string>(topDefectSql, parameters) ?? "N/A";

                // Top device code
                string topDevSql = $@"
                    SELECT COALESCE(DeviceCode, 'Unspecified') 
                    FROM DeviceRecords 
                    {whereClause} 
                    AND DeviceCode IS NOT NULL AND DeviceCode != ''
                    GROUP BY DeviceCode 
                    ORDER BY COUNT(*) DESC 
                    LIMIT 1;
                ";
                summary.TopDeviceCode = await conn.QueryFirstOrDefaultAsync<string>(topDevSql, parameters) ?? "N/A";

                // Top OpCo
                string topOpCoSql = $@"
                    SELECT COALESCE(OpCo, 'Unspecified') 
                    FROM DeviceRecords 
                    {whereClause} 
                    AND OpCo IS NOT NULL AND OpCo != ''
                    GROUP BY OpCo 
                    ORDER BY COUNT(*) DESC 
                    LIMIT 1;
                ";
                summary.TopOpCo = await conn.QueryFirstOrDefaultAsync<string>(topOpCoSql, parameters) ?? "N/A";

                return summary;
            });
        }

        public async Task<List<StatisticItem>> GetGroupedStatisticsAsync(
            DateTime? fromDate,
            DateTime? toDate,
            string? opCo,
            string? user,
            string? defect,
            string? deviceCode,
            string? status,
            string primaryColumn,
            string? secondaryColumn = null)
        {
            return await ExecuteWithRetryAsync(async conn =>
            {
                var (whereClause, parameters) = BuildFilterClause(fromDate, toDate, opCo, user, defect, deviceCode, status);

                string col1 = SanitizeColumn(primaryColumn);
                string col2 = !string.IsNullOrEmpty(secondaryColumn) ? SanitizeColumn(secondaryColumn) : "''";

                string sql = $@"
                    SELECT 
                        COALESCE(NULLIF({col1}, ''), 'Unspecified') AS Key,
                        COALESCE(NULLIF({col2}, ''), '') AS SubKey,
                        COUNT(*) AS Count
                    FROM DeviceRecords
                    {whereClause}
                    GROUP BY {col1}{(string.IsNullOrEmpty(secondaryColumn) ? "" : $", {col2}")}
                    ORDER BY Count DESC;
                ";

                var list = (await conn.QueryAsync<StatisticItem>(sql, parameters)).ToList();
                int total = list.Sum(x => x.Count);
                if (total > 0)
                {
                    foreach (var item in list)
                    {
                        item.Percentage = (double)item.Count / total * 100.0;
                    }
                }
                return list;
            });
        }

        private static (string whereClause, DynamicParameters parameters) BuildFilterClause(
            DateTime? fromDate,
            DateTime? toDate,
            string? opCo,
            string? user,
            string? defect,
            string? deviceCode,
            string? status)
        {
            var builder = new System.Text.StringBuilder("WHERE 1=1");
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(opCo) && opCo != "All")
            {
                builder.Append(" AND OpCo = @OpCo");
                parameters.Add("OpCo", opCo);
            }

            if (!string.IsNullOrWhiteSpace(user) && user != "All")
            {
                builder.Append(" AND (CreatedBy = @User OR UpdatedBy = @User)");
                parameters.Add("User", user);
            }

            if (!string.IsNullOrWhiteSpace(defect) && defect != "All")
            {
                builder.Append(" AND Defect = @Defect");
                parameters.Add("Defect", defect);
            }

            if (!string.IsNullOrWhiteSpace(deviceCode) && deviceCode != "All")
            {
                builder.Append(" AND DeviceCode = @DeviceCode");
                parameters.Add("DeviceCode", deviceCode);
            }

            if (!string.IsNullOrWhiteSpace(status) && status != "All")
            {
                builder.Append(" AND Status = @Status");
                parameters.Add("Status", status);
            }

            if (fromDate.HasValue)
            {
                builder.Append(" AND CreatedAt >= @FromDate");
                parameters.Add("FromDate", fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                builder.Append(" AND CreatedAt <= @ToDate");
                parameters.Add("ToDate", toDate.Value.Date.AddDays(1).AddTicks(-1));
            }

            return (builder.ToString(), parameters);
        }

        private static string SanitizeColumn(string col)
        {
            return col.Trim().ToLowerInvariant() switch
            {
                "devicecode" or "device_code" or "dev cd" or "device code" => "DeviceCode",
                "opco" or "operating company" => "OpCo",
                "defect" or "issue" or "problem" => "Defect",
                "createdby" or "user" or "entered by" or "operator" => "CreatedBy",
                "status" => "Status",
                "date" or "day" or "daily" or "createdat" => "strftime('%Y-%m-%d', CreatedAt)",
                "manufacturercode" or "mfr cd" or "mfr" => "ManufacturerCode",
                "modtype" or "mod type" or "module type" => "ModType",
                _ => "OpCo"
            };
        }
    }
}
