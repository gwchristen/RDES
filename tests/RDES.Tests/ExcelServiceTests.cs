using System;
using System.Collections.Generic;
using System.IO;
using RDES.App.Models;
using RDES.App.Services;
using Xunit;

namespace RDES.Tests
{
    public class ExcelServiceTests : IDisposable
    {
        private readonly string _tempExportXlsx;
        private readonly string _tempExportCsv;
        private readonly ExcelService _excelService;

        public ExcelServiceTests()
        {
            _tempExportXlsx = Path.Combine(Path.GetTempPath(), $"RDES_Export_{Guid.NewGuid():N}.xlsx");
            _tempExportCsv = Path.Combine(Path.GetTempPath(), $"RDES_Export_{Guid.NewGuid():N}.csv");
            _excelService = new ExcelService();
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_tempExportXlsx)) File.Delete(_tempExportXlsx);
                if (File.Exists(_tempExportCsv)) File.Delete(_tempExportCsv);
            }
            catch { }
        }

        [Fact]
        public void ExportToExcelAndCsv_GeneratesValidFiles()
        {
            var sampleRecords = new List<DeviceRecord>
            {
                new()
                {
                    Id = 1,
                    SerialNumber = "921807607",
                    ModuleNumber = "00135005FF5E9DA8",
                    Defect = "Accuracy Issue",
                    Status = "Pending",
                    DeviceCode = "NVD06",
                    ManufacturerCode = "D",
                    CreatedBy = "Pirate",
                    CreatedAt = DateTime.Now,
                    UpdatedBy = "Pirate",
                    UpdatedAt = DateTime.Now,
                    MachineName = "TEST-PC"
                }
            };

            _excelService.ExportToExcel(sampleRecords, _tempExportXlsx);
            Assert.True(File.Exists(_tempExportXlsx));
            Assert.True(new FileInfo(_tempExportXlsx).Length > 0);

            _excelService.ExportToCsv(sampleRecords, _tempExportCsv);
            Assert.True(File.Exists(_tempExportCsv));
            string csvContent = File.ReadAllText(_tempExportCsv);
            Assert.Contains("921807607", csvContent);
            Assert.Contains("00135005FF5E9DA8", csvContent);
            Assert.Contains("Accuracy Issue", csvContent);

            // Test AEP format
            string aepPath = Path.Combine(Path.GetTempPath(), $"AEP_Test_{Guid.NewGuid():N}.xlsx");
            try
            {
                _excelService.ExportToAepFormat(sampleRecords, aepPath);
                Assert.True(File.Exists(aepPath));
                Assert.True(new FileInfo(aepPath).Length > 0);
            }
            finally
            {
                if (File.Exists(aepPath)) File.Delete(aepPath);
            }

            // Test Aclara format
            string aclaraPath = Path.Combine(Path.GetTempPath(), $"Aclara_Test_{Guid.NewGuid():N}.xlsx");
            try
            {
                _excelService.ExportToAclaraFormat(sampleRecords, aclaraPath);
                Assert.True(File.Exists(aclaraPath));
                Assert.True(new FileInfo(aclaraPath).Length > 0);
            }
            finally
            {
                if (File.Exists(aclaraPath)) File.Delete(aclaraPath);
            }
        }

        [Fact]
        public void ImportFromSpreadsheet_IfFileExists_ReadsRmaRecords()
        {
            string sampleRmaFile = @"C:\Users\Pirate\Downloads\New RMA Doc PopUp.xlsm";
            if (!File.Exists(sampleRmaFile)) return; // Skip if file is not in user's downloads

            var sheets = _excelService.GetSheetNames(sampleRmaFile);
            Assert.Contains("RMA Entry", sheets);

            var records = _excelService.ImportFromSpreadsheet(sampleRmaFile, "RMA Entry");
            Assert.NotEmpty(records);
            Assert.Contains(records, r => r.SerialNumber == "623868725");
        }

        [Fact]
        public void ExportAllOpCosIndividually_CreatesSeparateFilesPerOpCo()
        {
            var records = new List<DeviceRecord>
            {
                new() { SerialNumber = "SN-01", OpCo = "OH - RMA", Defect = "Accuracy Issue" },
                new() { SerialNumber = "SN-02", OpCo = "OH - RMA", Defect = "Battery failure" },
                new() { SerialNumber = "SN-03", OpCo = "I&M - RMA", Defect = "Error code 200" },
                new() { SerialNumber = "SN-04", OpCo = "OH - Special", Defect = "Repeat issue" },
                new() { SerialNumber = "SN-05", OpCo = "I&M - Special", Defect = "Bad interval data" },
            };

            string exportDir = Path.Combine(Path.GetTempPath(), $"OpCoExport_{Guid.NewGuid():N}");
            try
            {
                var generatedFiles = _excelService.ExportAllOpCosIndividually(records, exportDir, "Excel");
                Assert.Equal(4, generatedFiles.Count);

                foreach (var file in generatedFiles)
                {
                    Assert.True(File.Exists(file));
                    Assert.True(new FileInfo(file).Length > 0);
                }
            }
            finally
            {
                if (Directory.Exists(exportDir)) Directory.Delete(exportDir, true);
            }
        }
    }
}
