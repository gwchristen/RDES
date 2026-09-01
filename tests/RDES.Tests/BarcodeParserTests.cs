using RDES.App.Services;
using Xunit;

namespace RDES.Tests
{
    public class BarcodeParserTests
    {
        private readonly BarcodeParserService _parser = new();

        [Fact]
        public void Parse_CompositeBarcode_17Chars_ExtractsAllComponents()
        {
            // Sample from Serial Manipulator Tool: 1ND988181154NVD06
            string rawBarcode = "1ND988181154NVD06";
            var result = _parser.Parse(rawBarcode);

            Assert.True(result.IsCompositeBarcode);
            Assert.Equal("1N", result.LookupPrefix);
            Assert.Equal("D", result.ManufacturerCode);
            Assert.Equal("988181154", result.SerialNumber);
            Assert.Equal("NVD06", result.DeviceCode);
            Assert.Equal("I-210+C", result.MeterType);
        }

        [Fact]
        public void Parse_CompositeBarcode_EH006_ExtractsI210Plus()
        {
            // Sample from Serial Manipulator Tool: 00D662559856EH006
            string rawBarcode = "00D662559856EH006";
            var result = _parser.Parse(rawBarcode);

            Assert.True(result.IsCompositeBarcode);
            Assert.Equal("00", result.LookupPrefix);
            Assert.Equal("D", result.ManufacturerCode);
            Assert.Equal("662559856", result.SerialNumber);
            Assert.Equal("EH006", result.DeviceCode);
            Assert.Equal("I-210+", result.MeterType);
        }

        [Fact]
        public void Parse_SixteenChars_DoesNotPrematurelyTriggerComposite()
        {
            // 16 characters in middle of scan: 1ND988181154NVD0
            string partialBarcode = "1ND988181154NVD0";
            var result = _parser.Parse(partialBarcode);

            Assert.False(result.IsCompositeBarcode);
            Assert.Equal("1ND988181154NVD0", result.SerialNumber);
        }

        [Fact]
        public void Parse_PureSerial_DoesNotTriggerComposite()
        {
            string serial = "921807607";
            var result = _parser.Parse(serial);

            Assert.False(result.IsCompositeBarcode);
            Assert.Equal("921807607", result.SerialNumber);
        }
    }
}
