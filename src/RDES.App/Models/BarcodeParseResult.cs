namespace RDES.App.Models
{
    public class BarcodeParseResult
    {
        public bool IsCompositeBarcode { get; set; }
        public string RawBarcode { get; set; } = string.Empty;
        public string LookupPrefix { get; set; } = string.Empty;
        public string ManufacturerCode { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string DeviceCode { get; set; } = string.Empty;
        public string MeterType { get; set; } = string.Empty;
        public string Suffix { get; set; } = string.Empty;
    }
}
