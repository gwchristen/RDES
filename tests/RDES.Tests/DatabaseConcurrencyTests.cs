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
    public class DatabaseConcurrencyTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly ConfigService _configService;
        private readonly DatabaseService _databaseService;
        private readonly DeviceRepository _repository;

        public DatabaseConcurrencyTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"RDES_Test_{Guid.NewGuid():N}.db");
            _configService = new ConfigService();
            _configService.CurrentConfig.DatabasePath = _testDbPath;
            _databaseService = new DatabaseService(_configService);
            _repository = new DeviceRepository(_databaseService);
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
                // Ignore cleanup errors
            }
        }

        [Fact]
        public async Task InsertRecord_AutomaticallySetsAuditFields()
        {
            var record = new DeviceRecord
            {
                SerialNumber = "TEST-SN-1001",
                ModuleNumber = "MOD-999",
                Defect = "Accuracy Issue",
                DeviceCode = "NVD06",
                Status = "Pending"
            };

            long id = await _repository.InsertRecordAsync(record);
            Assert.True(id > 0);

            var retrieved = await _repository.GetByIdAsync(id);
            Assert.NotNull(retrieved);
            Assert.Equal("TEST-SN-1001", retrieved.SerialNumber);
            Assert.Equal(Environment.UserName, retrieved.CreatedBy);
            Assert.Equal(Environment.UserName, retrieved.UpdatedBy);
            Assert.Equal(Environment.MachineName, retrieved.MachineName);
            Assert.True(retrieved.CreatedAt <= DateTime.Now);
        }

        [Fact]
        public async Task ConcurrentInserts_ExecuteSuccessfullyWithoutLockErrors()
        {
            const int threadCount = 20;
            var tasks = new List<Task<long>>();

            for (int i = 0; i < threadCount; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    var record = new DeviceRecord
                    {
                        SerialNumber = $"CONCUR-SN-{index:D4}",
                        ModuleNumber = $"MOD-{index}",
                        Defect = "Accuracy Issue",
                        DeviceCode = "DEV-01",
                        Notes = $"Thread insert {index}"
                    };
                    return await _repository.InsertRecordAsync(record);
                }));
            }

            var ids = await Task.WhenAll(tasks);

            Assert.Equal(threadCount, ids.Length);
            Assert.All(ids, id => Assert.True(id > 0));

            var stats = await _repository.GetStatisticsAsync();
            Assert.True(stats.TotalCount >= threadCount);
        }

        [Fact]
        public async Task SearchRecords_FiltersByQueryAndDefect()
        {
            await _repository.InsertRecordAsync(new DeviceRecord
            {
                SerialNumber = "SEARCH-ALPHA-01",
                ModuleNumber = "MOD-AAA",
                Defect = "Fast blink on NIC",
                Status = "Pending"
            });

            await _repository.InsertRecordAsync(new DeviceRecord
            {
                SerialNumber = "SEARCH-BETA-02",
                ModuleNumber = "MOD-BBB",
                Defect = "Meter - Broken base",
                Status = "Closed"
            });

            // Search by serial partial
            var search1 = await _repository.SearchRecordsAsync("ALPHA");
            Assert.Single(search1);
            Assert.Equal("SEARCH-ALPHA-01", search1[0].SerialNumber);

            // Search by Defect filter
            var search2 = await _repository.SearchRecordsAsync(defect: "Fast blink on NIC");
            Assert.Contains(search2, r => r.SerialNumber == "SEARCH-ALPHA-01");
            Assert.DoesNotContain(search2, r => r.SerialNumber == "SEARCH-BETA-02");
        }

        [Fact]
        public async Task BulkInsert_HandlesDuplicatesAndUpdatesCorrectly()
        {
            var initial = new List<DeviceRecord>
            {
                new() { SerialNumber = "BULK-01", Defect = "Accuracy Issue", Status = "Pending" },
                new() { SerialNumber = "BULK-02", Defect = "Battery failure", Status = "Pending" }
            };

            var res1 = await _repository.BulkInsertAsync(initial, overwriteDuplicates: false);
            Assert.Equal(2, res1.InsertedCount);
            Assert.Equal(0, res1.SkippedDuplicates);

            // Second pass without overwrite
            var second = new List<DeviceRecord>
            {
                new() { SerialNumber = "BULK-01", Defect = "Modified Issue", Status = "Closed" },
                new() { SerialNumber = "BULK-03", Defect = "Error code 200", Status = "Pending" }
            };

            var res2 = await _repository.BulkInsertAsync(second, overwriteDuplicates: false);
            Assert.Equal(1, res2.InsertedCount);
            Assert.Equal(1, res2.SkippedDuplicates);

            // Third pass with overwrite
            var res3 = await _repository.BulkInsertAsync(second, overwriteDuplicates: true);
            Assert.Equal(0, res3.InsertedCount);
            Assert.Equal(2, res3.UpdatedCount);

            var updated = await _repository.GetBySerialNumberAsync("BULK-01");
            Assert.NotNull(updated);
            Assert.Equal("Modified Issue", updated.Defect);
        }

        [Fact]
        public async Task OpCo_InitialSeed_ContainsDefaultFourOpCos()
        {
            var opcos = await _repository.GetOpCoOptionsAsync();
            Assert.NotNull(opcos);
            Assert.True(opcos.Count >= 4);

            var names = opcos.Select(o => o.Name).ToList();
            Assert.Contains("OH - RMA", names);
            Assert.Contains("I&M - RMA", names);
            Assert.Contains("OH - Special", names);
            Assert.Contains("I&M - Special", names);
        }

        [Fact]
        public async Task OpCo_AddAndDeactivate_ModifiesListCorrectly()
        {
            var newOpCo = new OpCoOption
            {
                Name = "NEW - Custom OpCo",
                SortOrder = 99,
                IsActive = true
            };

            bool added = await _repository.AddOpCoOptionAsync(newOpCo);
            Assert.True(added);

            var listAfterAdd = await _repository.GetOpCoOptionsAsync();
            var addedItem = listAfterAdd.FirstOrDefault(o => o.Name == "NEW - Custom OpCo");
            Assert.NotNull(addedItem);

            bool deleted = await _repository.DeleteOpCoOptionAsync(addedItem.Id);
            Assert.True(deleted);

            var listAfterDelete = await _repository.GetOpCoOptionsAsync();
            Assert.DoesNotContain(listAfterDelete, o => o.Name == "NEW - Custom OpCo");
        }

        [Fact]
        public async Task SearchRecords_FiltersByOpCo()
        {
            await _repository.InsertRecordAsync(new DeviceRecord
            {
                SerialNumber = "OPCO-OH-01",
                OpCo = "OH - RMA",
                Defect = "Accuracy Issue"
            });

            await _repository.InsertRecordAsync(new DeviceRecord
            {
                SerialNumber = "OPCO-IM-02",
                OpCo = "I&M - Special",
                Defect = "Fast blink on NIC"
            });

            var searchOH = await _repository.SearchRecordsAsync(opCo: "OH - RMA");
            Assert.Contains(searchOH, r => r.SerialNumber == "OPCO-OH-01");
            Assert.DoesNotContain(searchOH, r => r.SerialNumber == "OPCO-IM-02");

            var searchIM = await _repository.SearchRecordsAsync(opCo: "I&M - Special");
            Assert.Contains(searchIM, r => r.SerialNumber == "OPCO-IM-02");
            Assert.DoesNotContain(searchIM, r => r.SerialNumber == "OPCO-OH-01");
        }

        [Fact]
        public async Task UpdateStatusBatch_UpdatesMultipleRecordsToSubmitted()
        {
            long id1 = await _repository.InsertRecordAsync(new DeviceRecord
            {
                SerialNumber = "STATUS-01",
                Status = "Pending"
            });

            long id2 = await _repository.InsertRecordAsync(new DeviceRecord
            {
                SerialNumber = "STATUS-02",
                Status = "Pending"
            });

            int updated = await _repository.UpdateStatusBatchAsync(new[] { id1, id2 }, "Submitted");
            Assert.Equal(2, updated);

            var rec1 = await _repository.GetByIdAsync(id1);
            var rec2 = await _repository.GetByIdAsync(id2);

            Assert.NotNull(rec1);
            Assert.NotNull(rec2);
            Assert.Equal("Submitted", rec1.Status);
            Assert.Equal("Submitted", rec2.Status);
        }

        [Fact]
        public async Task StatisticsQueries_ReturnsAccurateMetricsAndGroupings()
        {
            await _repository.InsertRecordAsync(new DeviceRecord
            {
                SerialNumber = "STAT-01",
                OpCo = "OH - RMA",
                DeviceCode = "EH006",
                Defect = "Accuracy Issue",
                Status = "Pending"
            });

            await _repository.InsertRecordAsync(new DeviceRecord
            {
                SerialNumber = "STAT-02",
                OpCo = "OH - RMA",
                DeviceCode = "EH006",
                Defect = "Accuracy Issue",
                Status = "Submitted"
            });

            await _repository.InsertRecordAsync(new DeviceRecord
            {
                SerialNumber = "STAT-03",
                OpCo = "I&M - Special",
                DeviceCode = "NVD06",
                Defect = "Broken Terminal",
                Status = "Pending"
            });

            var summary = await _repository.GetFilteredSummaryMetricsAsync(null, null, "All", "All", "All", "All", "All");
            Assert.True(summary.TotalCount >= 3);
            Assert.True(summary.PendingCount >= 2);
            Assert.True(summary.SubmittedCount >= 1);

            var opcoGroup = await _repository.GetGroupedStatisticsAsync(null, null, "All", "All", "All", "All", "All", "OpCo");
            Assert.Contains(opcoGroup, g => g.Key == "OH - RMA" && g.Count >= 2);
            Assert.Contains(opcoGroup, g => g.Key == "I&M - Special" && g.Count >= 1);

            var devDefectGroup = await _repository.GetGroupedStatisticsAsync(null, null, "All", "All", "All", "All", "All", "DeviceCode", "Defect");
            Assert.Contains(devDefectGroup, g => g.Key == "EH006" && g.SubKey == "Accuracy Issue" && g.Count >= 2);
        }
    }
}
