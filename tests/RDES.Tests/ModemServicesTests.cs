using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RDES.App.Models;
using RDES.App.Services;
using Xunit;

namespace RDES.Tests
{
    public class ModemServicesTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly IncidentLogService _incidentLogService;

        public ModemServicesTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"RDES_ModemTest_{Guid.NewGuid():N}.db");
            _configService = new ConfigService();
            _configService.CurrentConfig.DatabasePath = _testDbPath;
            _databaseService = new DatabaseService(_configService);
            _incidentLogService = new IncidentLogService(_databaseService);
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
                string wal = $"{_testDbPath}-wal";
                string shm = $"{_testDbPath}-shm";
                if (File.Exists(wal)) File.Delete(wal);
                if (File.Exists(shm)) File.Delete(shm);
            }
            catch
            {
                // Ignore cleanup
            }
        }

        [Fact]
        public void ExponentialBackoffPolicy_CalculatesDelayWithExponentialIncreaseAndJitter()
        {
            var config = new RetryPolicyConfig
            {
                BaseDelayMs = 100,
                MaxDelayMs = 1000,
                JitterFactor = 0.1,
                MaxRetries = 3
            };
            var policy = new ExponentialBackoffPolicy(config);

            int delay1 = policy.CalculateDelay(1); // ~100ms
            int delay2 = policy.CalculateDelay(2); // ~200ms
            int delay3 = policy.CalculateDelay(3); // ~400ms

            Assert.InRange(delay1, 80, 120);
            Assert.InRange(delay2, 170, 230);
            Assert.InRange(delay3, 340, 460);
        }

        [Fact]
        public async Task ExponentialBackoffPolicy_RetriesOnFailureAndSucceeds()
        {
            var config = new RetryPolicyConfig
            {
                BaseDelayMs = 10,
                MaxDelayMs = 50,
                MaxRetries = 3,
                CommandTimeoutMs = 1000
            };
            var policy = new ExponentialBackoffPolicy(config);

            int attempts = 0;
            int retriesLogged = 0;

            string result = await policy.ExecuteWithRetryAsync<string>(
                async ct =>
                {
                    attempts++;
                    if (attempts < 3)
                    {
                        throw new InvalidOperationException("Transient serial timeout");
                    }
                    return await Task.FromResult("SUCCESS");
                },
                onRetry: (ex, att) =>
                {
                    retriesLogged++;
                    return Task.CompletedTask;
                });

            Assert.Equal("SUCCESS", result);
            Assert.Equal(3, attempts);
            Assert.Equal(2, retriesLogged);
        }

        [Fact]
        public async Task ExponentialBackoffPolicy_ThrowsWhenMaxRetriesExceeded()
        {
            var config = new RetryPolicyConfig
            {
                BaseDelayMs = 10,
                MaxDelayMs = 50,
                MaxRetries = 2,
                CommandTimeoutMs = 500
            };
            var policy = new ExponentialBackoffPolicy(config);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await policy.ExecuteWithRetryAsync<bool>(async ct =>
                {
                    throw new InvalidOperationException("Permanent failure");
                });
            });
        }

        [Fact]
        public async Task ModemRecoveryService_AutoRecoversAfterUsbDisconnect()
        {
            var communicator = new ModemCommunicator();
            await communicator.ConnectAsync("COM3");

            var config = new RetryPolicyConfig { BaseDelayMs = 10, MaxDelayMs = 50, MaxRetries = 3 };
            var policy = new ExponentialBackoffPolicy(config);
            var recoveryService = new ModemRecoveryService(communicator, policy, _incidentLogService);

            Assert.True(communicator.IsConnected);
            Assert.Equal(ModemState.Connected, communicator.CurrentState);

            // Simulate USB Disconnect
            communicator.SimulateUsbDisconnect();

            // Verify auto-recovery was automatically triggered and succeeded
            Assert.True(communicator.IsConnected);
            Assert.Equal(ModemState.Connected, communicator.CurrentState);
            Assert.Equal(1, recoveryService.TotalRecoveries);
            Assert.Equal(1, recoveryService.TotalDisconnects);
        }

        [Fact]
        public async Task ModemWatchdogService_CalculatesMetricsAndFailuresPerHour()
        {
            var communicator = new ModemCommunicator();
            await communicator.ConnectAsync("COM3");

            var policy = new ExponentialBackoffPolicy(new RetryPolicyConfig { BaseDelayMs = 10 });
            var recoveryService = new ModemRecoveryService(communicator, policy, _incidentLogService);
            var watchdogService = new ModemWatchdogService(communicator, recoveryService, _incidentLogService);

            watchdogService.RecordRetry();
            watchdogService.RecordRetry();
            watchdogService.RecordFailure();
            watchdogService.RecordCommandExecution(true);
            watchdogService.RecordCommandExecution(false);

            var metrics = watchdogService.UpdateMetrics();

            Assert.Equal(2, metrics.TotalRetries);
            Assert.Equal(2.0, metrics.FailuresPerHour);
            Assert.Equal(2, metrics.TotalCommandsExecuted);
            Assert.Equal(1, metrics.TotalCommandFailures);
            Assert.True(metrics.UptimePercentage > 0);
        }

        [Fact]
        public async Task ModemSoakTestService_ExecutesPeriodicHealthChecks()
        {
            var communicator = new ModemCommunicator();
            await communicator.ConnectAsync("COM3");

            var policy = new ExponentialBackoffPolicy(new RetryPolicyConfig { BaseDelayMs = 5 });
            var recoveryService = new ModemRecoveryService(communicator, policy, _incidentLogService);
            var watchdogService = new ModemWatchdogService(communicator, recoveryService, _incidentLogService);
            var soakService = new ModemSoakTestService(communicator, policy, recoveryService, watchdogService, _incidentLogService);

            var soakConfig = new SoakTestConfig
            {
                TargetDurationHours = 0.001, // ~3.6 seconds for test
                HealthCheckIntervalSeconds = 1,
                StopOnFailure = false
            };

            bool started = await soakService.StartSoakTestAsync(soakConfig);
            Assert.True(started);
            Assert.True(soakService.Status.IsRunning);

            // Wait for test to execute health check cycles
            await Task.Delay(2500);
            soakService.StopSoakTest();

            Assert.False(soakService.Status.IsRunning);
            Assert.True(soakService.Status.TotalChecks >= 1);
            Assert.True(soakService.Status.PassedChecks >= 1);
        }

        [Fact]
        public async Task BatchSessionService_IsCrashSafeAndResumableFromSQLite()
        {
            var batchService = new BatchSessionService(_databaseService, _incidentLogService);
            var serials = new List<string> { "SN-001", "SN-002", "SN-003", "SN-004", "SN-005" };

            var session = await batchService.CreateBatchSessionAsync("TestBatch", serials);
            Assert.NotNull(session);
            Assert.Equal(5, session.TotalItems);
            Assert.Equal("Pending", session.Status);

            // Process first 2 items, then simulate crash/cancellation
            int processedInFirstRun = 0;
            using var cts = new CancellationTokenSource();

            _ = batchService.ResumeBatchSessionAsync(
                session.Id,
                async item =>
                {
                    processedInFirstRun++;
                    if (processedInFirstRun == 2)
                    {
                        cts.Cancel(); // Simulate crash/interruption during item 2
                    }
                    return await Task.FromResult(true);
                },
                ct: cts.Token);

            await Task.Delay(200);

            // Check SQLite state after crash
            var sessionAfterCrash = await batchService.GetBatchSessionAsync(session.Id);
            Assert.NotNull(sessionAfterCrash);
            Assert.True(sessionAfterCrash.ProcessedItems >= 1 && sessionAfterCrash.ProcessedItems < 5);

            // Resume execution from SQLite
            var processedInSecondRun = new List<string>();
            bool completed = await batchService.ResumeBatchSessionAsync(
                session.Id,
                async item =>
                {
                    processedInSecondRun.Add(item.SerialNumber);
                    return await Task.FromResult(true);
                });

            Assert.True(completed);

            var finalSession = await batchService.GetBatchSessionAsync(session.Id);
            Assert.NotNull(finalSession);
            Assert.Equal("Completed", finalSession.Status);
            Assert.Equal(5, finalSession.ProcessedItems);
            Assert.Equal(5, finalSession.SuccessCount);

            // Verify that already succeeded items were NOT re-processed!
            Assert.DoesNotContain("SN-001", processedInSecondRun);
        }

        [Fact]
        public async Task IncidentLogService_LogsAndExportsDiagnostics()
        {
            await _incidentLogService.LogIncidentAsync("Warning", "Disconnect", "Test disconnect message", "COM3");
            await _incidentLogService.LogIncidentAsync("Error", "CommandTimeout", "Test timeout message", "COM3");

            var logs = await _incidentLogService.GetIncidentLogsAsync(50);
            Assert.True(logs.Count >= 2);
            Assert.Contains(logs, l => l.EventType == "Disconnect");
            Assert.Contains(logs, l => l.EventType == "CommandTimeout");

            string tempFile = Path.Combine(Path.GetTempPath(), $"Diagnostics_{Guid.NewGuid():N}.json");
            bool exported = await _incidentLogService.ExportDiagnosticsAsync(tempFile, "json");
            Assert.True(exported);
            Assert.True(File.Exists(tempFile));

            string fileContent = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("Test disconnect message", fileContent);

            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
