using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class BatchSessionService
    {
        private readonly DatabaseService _databaseService;
        private readonly IncidentLogService _incidentLogService;

        public BatchSessionService(DatabaseService databaseService, IncidentLogService incidentLogService)
        {
            _databaseService = databaseService;
            _incidentLogService = incidentLogService;
        }

        public async Task<BatchSession> CreateBatchSessionAsync(string name, List<string> serialNumbers)
        {
            await _databaseService.EnsureDatabaseInitializedAsync();
            using var conn = await _databaseService.CreateConnectionAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                var session = new BatchSession
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = string.IsNullOrWhiteSpace(name) ? $"Batch_{DateTime.Now:yyyyMMdd_HHmmss}" : name,
                    Status = "Pending",
                    TotalItems = serialNumbers.Count,
                    ProcessedItems = 0,
                    SuccessCount = 0,
                    FailureCount = 0,
                    CurrentIndex = 0,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                string insertSessionSql = @"
                    INSERT INTO BatchSessions (Id, Name, Status, TotalItems, ProcessedItems, SuccessCount, FailureCount, CurrentIndex, CreatedAt, UpdatedAt)
                    VALUES (@Id, @Name, @Status, @TotalItems, @ProcessedItems, @SuccessCount, @FailureCount, @CurrentIndex, @CreatedAt, @UpdatedAt);
                ";
                await conn.ExecuteAsync(insertSessionSql, session, tran);

                string insertItemSql = @"
                    INSERT INTO BatchSessionItems (BatchSessionId, ItemIndex, SerialNumber, PayloadJson, Status, RetryCount)
                    VALUES (@BatchSessionId, @ItemIndex, @SerialNumber, @PayloadJson, 'Pending', 0);
                ";

                int index = 0;
                foreach (var sn in serialNumbers)
                {
                    await conn.ExecuteAsync(insertItemSql, new
                    {
                        BatchSessionId = session.Id,
                        ItemIndex = index++,
                        SerialNumber = sn,
                        PayloadJson = string.Empty
                    }, tran);
                }

                tran.Commit();

                await _incidentLogService.LogIncidentAsync(
                    "Info",
                    "BatchCreated",
                    $"Batch session '{session.Name}' (ID: {session.Id}) created with {session.TotalItems} items.",
                    sessionId: session.Id);

                return session;
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public async Task<BatchSession?> GetBatchSessionAsync(string sessionId)
        {
            await _databaseService.EnsureDatabaseInitializedAsync();
            using var conn = await _databaseService.CreateConnectionAsync();

            string sql = "SELECT * FROM BatchSessions WHERE Id = @Id;";
            return await conn.QueryFirstOrDefaultAsync<BatchSession>(sql, new { Id = sessionId });
        }

        public async Task<List<BatchSessionItem>> GetSessionItemsAsync(string sessionId)
        {
            await _databaseService.EnsureDatabaseInitializedAsync();
            using var conn = await _databaseService.CreateConnectionAsync();

            string sql = "SELECT * FROM BatchSessionItems WHERE BatchSessionId = @BatchSessionId ORDER BY ItemIndex ASC;";
            var items = await conn.QueryAsync<BatchSessionItem>(sql, new { BatchSessionId = sessionId });
            return items.ToList();
        }

        public async Task<List<BatchSession>> GetUnfinishedBatchSessionsAsync()
        {
            await _databaseService.EnsureDatabaseInitializedAsync();
            using var conn = await _databaseService.CreateConnectionAsync();

            string sql = @"
                SELECT * FROM BatchSessions 
                WHERE Status IN ('Pending', 'InProgress', 'Paused') 
                ORDER BY UpdatedAt DESC;
            ";
            var sessions = await conn.QueryAsync<BatchSession>(sql);
            return sessions.ToList();
        }

        public async Task<List<BatchSession>> GetAllBatchSessionsAsync(int limit = 50)
        {
            await _databaseService.EnsureDatabaseInitializedAsync();
            using var conn = await _databaseService.CreateConnectionAsync();

            string sql = "SELECT * FROM BatchSessions ORDER BY CreatedAt DESC LIMIT @Limit;";
            var sessions = await conn.QueryAsync<BatchSession>(sql, new { Limit = limit });
            return sessions.ToList();
        }

        public async Task<bool> ResumeBatchSessionAsync(
            string sessionId,
            Func<BatchSessionItem, Task<bool>> processItemFunc,
            IProgress<(int Processed, int Total, string CurrentSN)>? progress = null,
            CancellationToken ct = default)
        {
            var session = await GetBatchSessionAsync(sessionId);
            if (session == null) return false;

            var items = await GetSessionItemsAsync(sessionId);
            var pendingOrFailedItems = items.Where(i => i.Status != "Success").OrderBy(i => i.ItemIndex).ToList();

            if (pendingOrFailedItems.Count == 0)
            {
                // All items already succeeded
                await UpdateSessionStatusAsync(sessionId, "Completed", DateTime.Now);
                return true;
            }

            await UpdateSessionStatusAsync(sessionId, "InProgress");

            await _incidentLogService.LogIncidentAsync(
                "Info",
                "BatchResumed",
                $"Resuming batch session '{session.Name}' from item index {pendingOrFailedItems[0].ItemIndex}. {pendingOrFailedItems.Count} items remaining.",
                sessionId: sessionId);

            using var conn = await _databaseService.CreateConnectionAsync();

            foreach (var item in pendingOrFailedItems)
            {
                if (ct.IsCancellationRequested)
                {
                    await UpdateSessionStatusAsync(sessionId, "Paused");
                    return false;
                }

                // Update item status to InProgress
                string updateItemStartSql = @"
                    UPDATE BatchSessionItems
                    SET Status = 'InProgress', RetryCount = RetryCount + 1
                    WHERE Id = @Id;
                ";
                await conn.ExecuteAsync(updateItemStartSql, new { item.Id });

                progress?.Report((session.ProcessedItems, session.TotalItems, item.SerialNumber));

                bool success = false;
                string errorMsg = string.Empty;

                try
                {
                    success = await processItemFunc(item);
                }
                catch (Exception ex)
                {
                    success = false;
                    errorMsg = ex.Message;
                }

                // Update item result in SQLite
                string updateItemResultSql = @"
                    UPDATE BatchSessionItems
                    SET Status = @Status, ErrorMessage = @ErrorMessage, ProcessedAt = @ProcessedAt
                    WHERE Id = @Id;
                ";
                await conn.ExecuteAsync(updateItemResultSql, new
                {
                    Id = item.Id,
                    Status = success ? "Success" : "Failed",
                    ErrorMessage = errorMsg,
                    ProcessedAt = DateTime.Now
                });

                // Update session counts in SQLite
                string updateSessionProgressSql = @"
                    UPDATE BatchSessions
                    SET ProcessedItems = (SELECT COUNT(*) FROM BatchSessionItems WHERE BatchSessionId = @SessionId AND Status IN ('Success', 'Failed')),
                        SuccessCount = (SELECT COUNT(*) FROM BatchSessionItems WHERE BatchSessionId = @SessionId AND Status = 'Success'),
                        FailureCount = (SELECT COUNT(*) FROM BatchSessionItems WHERE BatchSessionId = @SessionId AND Status = 'Failed'),
                        CurrentIndex = @CurrentIndex,
                        UpdatedAt = @UpdatedAt
                    WHERE Id = @SessionId;
                ";
                await conn.ExecuteAsync(updateSessionProgressSql, new
                {
                    SessionId = sessionId,
                    CurrentIndex = item.ItemIndex,
                    UpdatedAt = DateTime.Now
                });

                // Refresh session state
                var currentSession = await GetBatchSessionAsync(sessionId);
                if (currentSession != null)
                {
                    session = currentSession;
                }
            }

            // Final status check
            string finalStatus = session.FailureCount == 0 ? "Completed" : (session.SuccessCount > 0 ? "Completed" : "Failed");
            await UpdateSessionStatusAsync(sessionId, finalStatus, DateTime.Now);

            await _incidentLogService.LogIncidentAsync(
                "Info",
                "BatchFinished",
                $"Batch session '{session.Name}' finished with status '{finalStatus}'. Total: {session.TotalItems}, Success: {session.SuccessCount}, Failures: {session.FailureCount}.",
                sessionId: sessionId);

            return true;
        }

        private async Task UpdateSessionStatusAsync(string sessionId, string status, DateTime? completedAt = null)
        {
            using var conn = await _databaseService.CreateConnectionAsync();
            string sql = @"
                UPDATE BatchSessions
                SET Status = @Status, UpdatedAt = @UpdatedAt, CompletedAt = COALESCE(@CompletedAt, CompletedAt)
                WHERE Id = @Id;
            ";
            await conn.ExecuteAsync(sql, new { Id = sessionId, Status = status, UpdatedAt = DateTime.Now, CompletedAt = completedAt });
        }
    }
}
