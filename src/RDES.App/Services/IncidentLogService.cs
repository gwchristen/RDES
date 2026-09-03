using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class IncidentLogService
    {
        private readonly DatabaseService _databaseService;

        public IncidentLogService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task LogIncidentAsync(
            string severity,
            string eventType,
            string message,
            string portName = "",
            Exception? ex = null,
            string metadataJson = "",
            string sessionId = "")
        {
            try
            {
                await _databaseService.EnsureDatabaseInitializedAsync();
                using var conn = await _databaseService.CreateConnectionAsync();

                var log = new ModemIncidentLog
                {
                    SessionId = sessionId,
                    Timestamp = DateTime.Now,
                    Severity = severity,
                    EventType = eventType,
                    Message = message,
                    PortName = portName,
                    ExceptionDetails = ex != null ? ex.ToString() : string.Empty,
                    MetadataJson = metadataJson
                };

                string sql = @"
                    INSERT INTO ModemIncidentLogs (SessionId, Timestamp, Severity, EventType, Message, PortName, ExceptionDetails, MetadataJson)
                    VALUES (@SessionId, @Timestamp, @Severity, @EventType, @Message, @PortName, @ExceptionDetails, @MetadataJson);
                ";

                await conn.ExecuteAsync(sql, log);
            }
            catch (Exception dbEx)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write incident log: {dbEx.Message}");
            }
        }

        public async Task<List<ModemIncidentLog>> GetIncidentLogsAsync(
            int limit = 100,
            string? severity = null,
            string? eventType = null)
        {
            await _databaseService.EnsureDatabaseInitializedAsync();
            using var conn = await _databaseService.CreateConnectionAsync();

            string sql = @"
                SELECT Id, SessionId, Timestamp, Severity, EventType, Message, PortName, ExceptionDetails, MetadataJson
                FROM ModemIncidentLogs
                WHERE (@Severity IS NULL OR Severity = @Severity)
                  AND (@EventType IS NULL OR EventType = @EventType)
                ORDER BY Timestamp DESC
                LIMIT @Limit;
            ";

            var results = await conn.QueryAsync<ModemIncidentLog>(sql, new { Limit = limit, Severity = severity, EventType = eventType });
            return results.ToList();
        }

        public async Task<bool> ExportDiagnosticsAsync(
            string filePath,
            string format,
            ModemHealthMetrics? metrics = null,
            SoakTestStatus? soakStatus = null)
        {
            try
            {
                var logs = await GetIncidentLogsAsync(500);

                var diagnosticsPackage = new
                {
                    ExportTimestamp = DateTime.Now,
                    SystemInformation = new
                    {
                        MachineName = Environment.MachineName,
                        UserName = Environment.UserName,
                        OSVersion = Environment.OSVersion.ToString(),
                        DotNetVersion = Environment.Version.ToString()
                    },
                    HealthMetrics = metrics ?? new ModemHealthMetrics(),
                    SoakTestStatus = soakStatus ?? new SoakTestStatus(),
                    RecentIncidentLogs = logs
                };

                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (format.Equals("json", StringComparison.OrdinalIgnoreCase) || ext == ".json")
                {
                    string json = JsonSerializer.Serialize(diagnosticsPackage, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(filePath, json);
                }
                else if (format.Equals("csv", StringComparison.OrdinalIgnoreCase) || ext == ".csv")
                {
                    using var writer = new StreamWriter(filePath);
                    await writer.WriteLineAsync("Id,Timestamp,Severity,EventType,PortName,Message,ExceptionDetails");
                    foreach (var log in logs)
                    {
                        string line = $"{log.Id},\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{log.Severity}\",\"{log.EventType}\",\"{log.PortName}\",\"{log.Message.Replace("\"", "\"\"")}\",\"{log.ExceptionDetails.Replace("\"", "\"\"")}\"";
                        await writer.WriteLineAsync(line);
                    }
                }
                else
                {
                    // Plain text / Log format
                    using var writer = new StreamWriter(filePath);
                    await writer.WriteLineAsync("==================================================================");
                    await writer.WriteLineAsync($"RDES MODEM RUN DIAGNOSTICS - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    await writer.WriteLineAsync("==================================================================");
                    await writer.WriteLineAsync($"Modem State: {diagnosticsPackage.HealthMetrics.State}");
                    await writer.WriteLineAsync($"Total Disconnects: {diagnosticsPackage.HealthMetrics.TotalDisconnects}");
                    await writer.WriteLineAsync($"Total Recoveries: {diagnosticsPackage.HealthMetrics.TotalRecoveries}");
                    await writer.WriteLineAsync($"Failures/Hour: {diagnosticsPackage.HealthMetrics.FailuresPerHour:F2}");
                    await writer.WriteLineAsync($"Total Retries: {diagnosticsPackage.HealthMetrics.TotalRetries}");
                    await writer.WriteLineAsync($"Uptime: {diagnosticsPackage.HealthMetrics.UptimePercentage:F1}%");
                    await writer.WriteLineAsync($"Soak Test Active: {diagnosticsPackage.SoakTestStatus.IsRunning}");
                    await writer.WriteLineAsync("------------------------------------------------------------------");
                    await writer.WriteLineAsync("RECENT INCIDENT LOGS:");
                    foreach (var log in logs)
                    {
                        await writer.WriteLineAsync($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Severity}] [{log.EventType}] {log.Message}");
                        if (!string.IsNullOrWhiteSpace(log.ExceptionDetails))
                        {
                            await writer.WriteLineAsync($"   Exception: {log.ExceptionDetails}");
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to export diagnostics: {ex.Message}");
                return false;
            }
        }
    }
}
