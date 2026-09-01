using System;
using System.Collections.Generic;
using RDES.App.Models;

namespace RDES.App.Services
{
    public class BarcodeParserService
    {
        private static readonly Dictionary<string, string> DeviceToMeterTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // GE / Aclara I-210+
            { "EH006", "I-210+" },
            { "EH015", "I-210+" },
            { "EH036", "I-210+" },
            { "EH038", "I-210+" },
            { "EA006", "I-210+" },
            { "EH008", "I-210+" },

            // I-210+C / I-210+Cn
            { "N3008", "I-210+C" },
            { "N3015", "I-210+C" },
            { "NM008", "I-210+C" },
            { "NM011", "I-210+C" },
            { "NM021", "I-210+C" },
            { "NM038", "I-210+C" },
            { "NMD06", "I-210+C" },
            { "NMD15", "I-210+C" },
            { "NMD36", "I-210+Cn" },
            { "NP008", "I-210+C" },
            { "NUD06", "I-210+C" },
            { "NUD36", "I-210+Cn" },
            { "NVD06", "I-210+C" },
            { "NAD06", "I-210+C" },

            // kV2c Family
            { "G3008", "kV2c" },
            { "G30F8", "kV2c" },
            { "G30G1", "kV2c" },
            { "G3NA1", "kV2c" },
            { "G3NA8", "kV2c" },
            { "GMNA1", "kV2c" },
            { "GN0E1", "kV2c" },
            { "GNNA1", "kV2c" },
            { "GNNA8", "kV2c" },
            { "H30F8", "kV2c" },
            { "HNNA1", "kV2c" },
            { "HS008", "kV2c" },
            { "HSNA1", "kV2c" },
            { "PM008", "kV2c" },
            { "PM0F1", "kV2c" },
            { "PM0F8", "kV2c" },
            { "PM0G1", "kV2c" },
            { "PMNA1", "kV2c" },
            { "PMNA8", "kV2c" },
            { "PS008", "kV2c" },
            { "PS0F8", "kV2c" },
            { "PSNA1", "kV2c" },
            { "PSNA8", "kV2c" }
        };

        public BarcodeParseResult Parse(string? input)
        {
            var result = new BarcodeParseResult();
            if (string.IsNullOrWhiteSpace(input)) return result;

            string cleaned = input.Trim().ToUpperInvariant();
            result.RawBarcode = cleaned;

            // Standard 17-char composite barcode: [2 Prefix] [1 Manu] [9 Serial] [5 Device]
            // Example: 1ND988181154NVD06, 00D662559856EH006, 3KD924781912NMD08
            if (cleaned.Length == 17 && char.IsDigit(cleaned[3]))
            {
                result.IsCompositeBarcode = true;
                result.LookupPrefix = cleaned.Substring(0, 2);
                result.ManufacturerCode = cleaned.Substring(2, 1);
                result.SerialNumber = cleaned.Substring(3, 9);
                result.DeviceCode = cleaned.Substring(12, 5);
                result.MeterType = LookupMeterType(result.DeviceCode);
                return result;
            }

            // Standard raw serial (digits only or alphanumeric without composite prefix)
            result.IsCompositeBarcode = false;
            result.SerialNumber = cleaned;
            return result;
        }

        public string LookupMeterType(string deviceCode)
        {
            if (string.IsNullOrWhiteSpace(deviceCode)) return string.Empty;
            if (DeviceToMeterTypeMap.TryGetValue(deviceCode.Trim(), out string? meterType))
            {
                return meterType;
            }
            return string.Empty;
        }
    }
}
