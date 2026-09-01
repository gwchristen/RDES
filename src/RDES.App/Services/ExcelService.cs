using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class ExcelService
    {
        public List<string> GetSheetNames(string filePath)
        {
            var sheetNames = new List<string>();
            try
            {
                using var workbook = new XLWorkbook(filePath);
                foreach (var sheet in workbook.Worksheets)
                {
                    sheetNames.Add(sheet.Name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading sheets: {ex.Message}");
            }
            return sheetNames;
        }

        public List<DeviceRecord> ImportFromSpreadsheet(string filePath, string? targetSheetName = null)
        {
            var records = new List<DeviceRecord>();
            using var workbook = new XLWorkbook(filePath);

            IXLWorksheet worksheet;
            if (!string.IsNullOrWhiteSpace(targetSheetName) && workbook.Worksheets.Contains(targetSheetName))
            {
                worksheet = workbook.Worksheet(targetSheetName);
            }
            else
            {
                worksheet = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidOperationException("No worksheet found in workbook.");
            }

            var range = worksheet.RangeUsed();
            if (range == null) return records;

            int firstRow = range.FirstRow().RowNumber();
            int lastRow = range.LastRow().RowNumber();
            int firstCol = range.FirstColumn().ColumnNumber();
            int lastCol = range.LastColumn().ColumnNumber();

            // Find header row (usually first non-empty row)
            int headerRowIndex = firstRow;
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int col = firstCol; col <= lastCol; col++)
            {
                string headerVal = worksheet.Cell(headerRowIndex, col).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(headerVal) && !headerMap.ContainsKey(headerVal))
                {
                    headerMap[headerVal] = col;
                }
            }

            // If header row has no identifiable columns, scan row 1 to 5 to find headers
            if (!headerMap.Keys.Any(k => k.Contains("Serial", StringComparison.OrdinalIgnoreCase) || k.Contains("Defect", StringComparison.OrdinalIgnoreCase) || k.Contains("DEV CD", StringComparison.OrdinalIgnoreCase)))
            {
                for (int r = firstRow; r <= Math.Min(firstRow + 5, lastRow); r++)
                {
                    var tempMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int col = firstCol; col <= lastCol; col++)
                    {
                        string headerVal = worksheet.Cell(r, col).GetString().Trim();
                        if (!string.IsNullOrWhiteSpace(headerVal) && !tempMap.ContainsKey(headerVal))
                        {
                            tempMap[headerVal] = col;
                        }
                    }
                    if (tempMap.Keys.Any(k => k.Contains("Serial", StringComparison.OrdinalIgnoreCase) || k.Contains("Defect", StringComparison.OrdinalIgnoreCase)))
                    {
                        headerMap = tempMap;
                        headerRowIndex = r;
                        break;
                    }
                }
            }

            for (int r = headerRowIndex + 1; r <= lastRow; r++)
            {
                var record = new DeviceRecord();

                // Serial Number
                string serial = GetVal(worksheet, r, headerMap, "Serial #", "SERIAL #", "Serial", "Customer Serial Number", "Aclara Serial Number(s) / Range - Start ");
                if (string.IsNullOrWhiteSpace(serial))
                {
                    // If no explicit serial header matched, check column named 'Full Barcode' or try first column
                    serial = GetVal(worksheet, r, headerMap, "Full Barcode");
                    if (string.IsNullOrWhiteSpace(serial)) continue;
                }
                record.SerialNumber = serial;

                // Module Number
                record.ModuleNumber = GetVal(worksheet, r, headerMap, "Module #", "MOD #", "Module Number", "MOD TYPE");
                
                // Defect & Problem
                record.Defect = GetVal(worksheet, r, headerMap, "Defect", "PROBLEM", "Customer Issue", "Failure Location");
                record.Problem = GetVal(worksheet, r, headerMap, "PROBLEM", "Defect");
                record.OtherProblem = GetVal(worksheet, r, headerMap, "OTHER PROBLEM");

                // Additional metadata
                record.DeviceCode = GetVal(worksheet, r, headerMap, "DEV CD", "Device");
                record.ManufacturerCode = GetVal(worksheet, r, headerMap, "MFR CD", "Manu");
                record.MfgDate = GetVal(worksheet, r, headerMap, "MFG DATE");
                record.ModType = GetVal(worksheet, r, headerMap, "MOD TYPE");
                record.ModNumber = GetVal(worksheet, r, headerMap, "MOD #");
                record.RecordType = GetVal(worksheet, r, headerMap, "TYPE");
                record.Catalog = GetVal(worksheet, r, headerMap, "CATALOG", "Catalog/Part Number");
                record.FileNumber = GetVal(worksheet, r, headerMap, "FILE");
                record.Status = GetVal(worksheet, r, headerMap, "STATUS", "Status");
                if (string.IsNullOrWhiteSpace(record.Status)) record.Status = "Pending";

                record.OpCo = GetVal(worksheet, r, headerMap, "OpCo", "OPCO", "Operating Company", "Op Co");
                if (string.IsNullOrWhiteSpace(record.OpCo)) record.OpCo = "OH - RMA";

                record.AclaraSerialStart = GetVal(worksheet, r, headerMap, "Aclara Serial Number(s) / Range - Start ");
                record.AclaraSerialEnd = GetVal(worksheet, r, headerMap, "Aclara Serial Number(s) / Range - End");
                record.CustomerSerialNumber = GetVal(worksheet, r, headerMap, "Customer Serial Number");
                record.MaterialGroup = GetVal(worksheet, r, headerMap, "Material Group");
                record.FailureLocation = GetVal(worksheet, r, headerMap, "Failure Location");
                record.CustomerIssue = GetVal(worksheet, r, headerMap, "Customer Issue");
                record.CustomerInput = GetVal(worksheet, r, headerMap, "Customer Input (30 Characters)");

                string qtyStr = GetVal(worksheet, r, headerMap, "Qty ", "Qty", "Quantity");
                if (int.TryParse(qtyStr, out int q) && q > 0)
                {
                    record.Quantity = q;
                }

                records.Add(record);
            }

            return records;
        }

        private string GetVal(IXLWorksheet ws, int row, Dictionary<string, int> headerMap, params string[] possibleHeaders)
        {
            foreach (var h in possibleHeaders)
            {
                if (headerMap.TryGetValue(h, out int colIndex))
                {
                    var cell = ws.Cell(row, colIndex);
                    if (!cell.IsEmpty())
                    {
                        return cell.GetString().Trim();
                    }
                }
            }
            return string.Empty;
        }

        public void ExportToExcel(IEnumerable<DeviceRecord> records, string filePath)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Device Records");

            var headers = new[]
            {
                "ID", "Serial #", "Module #", "OpCo", "Defect", "Status",
                "Device Code", "MFR Code", "Catalog", "Problem",
                "Customer Input", "Notes", "Created By", "Created Date",
                "Updated By", "Updated Date", "Machine"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F6CBD");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = 2;
            foreach (var r in records)
            {
                worksheet.Cell(row, 1).Value = r.Id;
                worksheet.Cell(row, 2).Value = r.SerialNumber;
                worksheet.Cell(row, 3).Value = r.ModuleNumber;
                worksheet.Cell(row, 4).Value = r.OpCo;
                worksheet.Cell(row, 5).Value = r.Defect;
                worksheet.Cell(row, 6).Value = r.Status;
                worksheet.Cell(row, 7).Value = r.DeviceCode;
                worksheet.Cell(row, 8).Value = r.ManufacturerCode;
                worksheet.Cell(row, 9).Value = r.Catalog;
                worksheet.Cell(row, 10).Value = r.Problem;
                worksheet.Cell(row, 11).Value = r.CustomerInput;
                worksheet.Cell(row, 12).Value = r.Notes;
                worksheet.Cell(row, 13).Value = r.CreatedBy;
                worksheet.Cell(row, 14).Value = r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
                worksheet.Cell(row, 15).Value = r.UpdatedBy;
                worksheet.Cell(row, 16).Value = r.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss");
                worksheet.Cell(row, 17).Value = r.MachineName;

                // Alternate row background (zebra striping)
                if (row % 2 == 0)
                {
                    worksheet.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8F9FA");
                }

                row++;
            }

            worksheet.Columns().AdjustToContents();
            worksheet.RangeUsed()?.SetAutoFilter();

            workbook.SaveAs(filePath);
        }

        public void ExportToCsv(IEnumerable<DeviceRecord> records, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ID,Serial #,Module #,OpCo,Defect,Status,Device Code,MFR Code,Catalog,Problem,Customer Input,Notes,Created By,Created Date,Updated By,Updated Date,Machine");

            foreach (var r in records)
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsv(r.Id.ToString()),
                    EscapeCsv(r.SerialNumber),
                    EscapeCsv(r.ModuleNumber),
                    EscapeCsv(r.OpCo),
                    EscapeCsv(r.Defect),
                    EscapeCsv(r.Status),
                    EscapeCsv(r.DeviceCode),
                    EscapeCsv(r.ManufacturerCode),
                    EscapeCsv(r.Catalog),
                    EscapeCsv(r.Problem),
                    EscapeCsv(r.CustomerInput),
                    EscapeCsv(r.Notes),
                    EscapeCsv(r.CreatedBy),
                    EscapeCsv(r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                    EscapeCsv(r.UpdatedBy),
                    EscapeCsv(r.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                    EscapeCsv(r.MachineName)
                ));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public void ExportToAepFormat(IEnumerable<DeviceRecord> records, string filePath)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("AEP");

            var headers = new[]
            {
                "DATE", "DEV CD", "MFR CD", "SERIAL #", "MFG DATE",
                "MOD TYPE", "MOD #", "PROBLEM", "OTHER PROBLEM", "TYPE",
                "CATALOG", "FILE", "STATUS"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#002D62");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = 2;
            foreach (var r in records)
            {
                worksheet.Cell(row, 1).Value = r.CreatedAt.ToString("MM/dd/yyyy");
                worksheet.Cell(row, 2).Value = r.DeviceCode;
                worksheet.Cell(row, 3).Value = r.ManufacturerCode;
                worksheet.Cell(row, 4).Value = r.SerialNumber;
                worksheet.Cell(row, 5).Value = r.MfgDate;
                worksheet.Cell(row, 6).Value = r.ModType;
                worksheet.Cell(row, 7).Value = r.ModuleNumber;
                worksheet.Cell(row, 8).Value = !string.IsNullOrEmpty(r.Problem) ? r.Problem : r.Defect;
                worksheet.Cell(row, 9).Value = r.OtherProblem;
                worksheet.Cell(row, 10).Value = r.RecordType;
                worksheet.Cell(row, 11).Value = r.Catalog;
                worksheet.Cell(row, 12).Value = r.FileNumber;
                worksheet.Cell(row, 13).Value = r.Status;

                if (row % 2 == 0)
                {
                    worksheet.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F6F9");
                }
                row++;
            }

            worksheet.Columns().AdjustToContents();
            worksheet.RangeUsed()?.SetAutoFilter();
            workbook.SaveAs(filePath);
        }

        public void ExportToAclaraFormat(IEnumerable<DeviceRecord> records, string filePath)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Aclara");

            var headers = new[]
            {
                "Qty ", "Aclara Serial Number(s) / Range - Start ", "Aclara Serial Number(s) / Range - End",
                "Customer Serial Number", "Catalog/Part Number", "Material Group",
                "Failure Location", "Customer Issue", "Customer Input (30 Characters)"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = 2;
            foreach (var r in records)
            {
                worksheet.Cell(row, 1).Value = r.Quantity > 0 ? r.Quantity : 1;
                worksheet.Cell(row, 2).Value = !string.IsNullOrEmpty(r.AclaraSerialStart) ? r.AclaraSerialStart : r.SerialNumber;
                worksheet.Cell(row, 3).Value = r.AclaraSerialEnd;
                worksheet.Cell(row, 4).Value = !string.IsNullOrEmpty(r.CustomerSerialNumber) ? r.CustomerSerialNumber : r.SerialNumber;
                worksheet.Cell(row, 5).Value = r.Catalog;
                worksheet.Cell(row, 6).Value = r.MaterialGroup;
                worksheet.Cell(row, 7).Value = r.FailureLocation;
                worksheet.Cell(row, 8).Value = !string.IsNullOrEmpty(r.CustomerIssue) ? r.CustomerIssue : r.Defect;
                worksheet.Cell(row, 9).Value = r.CustomerInput;

                if (row % 2 == 0)
                {
                    worksheet.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
                }
                row++;
            }

            worksheet.Columns().AdjustToContents();
            worksheet.RangeUsed()?.SetAutoFilter();
            workbook.SaveAs(filePath);
        }

        public List<string> ExportAllOpCosIndividually(IEnumerable<DeviceRecord> records, string folderPath, string format = "Excel")
        {
            var generatedFiles = new List<string>();
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            var groups = records.GroupBy(r => !string.IsNullOrWhiteSpace(r.OpCo) ? r.OpCo.Trim() : "Unassigned");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            foreach (var group in groups)
            {
                string safeOpCo = string.Join("_", group.Key.Split(Path.GetInvalidFileNameChars()));
                string fileName = $"{safeOpCo}_{format}_{timestamp}.xlsx";
                string fullPath = Path.Combine(folderPath, fileName);

                switch (format.ToUpperInvariant())
                {
                    case "AEP":
                        ExportToAepFormat(group, fullPath);
                        break;
                    case "ACLARA":
                        ExportToAclaraFormat(group, fullPath);
                        break;
                    case "CSV":
                        fullPath = Path.Combine(folderPath, $"{safeOpCo}_{timestamp}.csv");
                        ExportToCsv(group, fullPath);
                        break;
                    default:
                        ExportToExcel(group, fullPath);
                        break;
                }

                generatedFiles.Add(fullPath);
            }

            return generatedFiles;
        }

        private static string EscapeCsv(string? field)
        {
            if (string.IsNullOrEmpty(field)) return "\"\"";
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return $"\"{field}\"";
        }
    }
}
