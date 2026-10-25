using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RestResponseTests
    {
        [Theory]
        [InlineData("{\"status\":\"SUCCESS\",\"result\":null,\"error\":null}")]
        [InlineData("{\"status\":\"PROCESSING\",\"request_id\":1}")]
        // Real Control Server output is pretty-printed with spaces around the colon.
        [InlineData("{\n   \"status\" : \"PROCESSING\",\n   \"request_id\" : 707,\n   \"result\" : null,\n   \"error\" : null\n}")]
        [InlineData("{\n   \"status\" : \"SUCCESS\"\n}")]
        public void IsAccepted_SuccessOrProcessing_ReturnsTrue(string body)
        {
            Assert.True(RestResponse.IsAccepted(body));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\n   \"status\" : \"ERROR\",\n   \"error\" : { \"reason\" : \"PROTOCOL\" }\n}")]
        [InlineData("{\"result\":{}}")]
        public void IsAccepted_ErrorOrMalformed_ReturnsFalse(string body)
        {
            Assert.False(RestResponse.IsAccepted(body));
        }
    }
}
