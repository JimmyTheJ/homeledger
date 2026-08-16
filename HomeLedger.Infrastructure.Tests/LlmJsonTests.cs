using System.Text.Json;
using HomeLedger.Infrastructure.Llm;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class LlmJsonTests
{
    [Theory]
    [InlineData("line_items", "LineItems")]
    [InlineData("lineItems", "LineItems")]
    [InlineData("LineItems", "LineItems")]
    [InlineData("receipt_date", "ReceiptDate")]
    [InlineData("external-id", "ExternalId")]
    public void ToPascalCase_normalizes_llm_property_names(string input, string expected)
    {
        Assert.Equal(expected, LlmJson.ToPascalCase(input));
    }

    [Fact]
    public void Deserialize_maps_snake_case_line_items_from_local_models()
    {
        const string json = """
            {"merchant":"Walmart","receipt_date":"2026/08/15","external_id":"123","line_items":[{"description":"Milk","amount":-4.99,"category":"Groceries"}]}
            """;

        var parsed = LlmJson.Deserialize<SampleReceipt>(json);

        Assert.NotNull(parsed);
        Assert.Equal("Walmart", parsed.Merchant);
        Assert.Equal("2026/08/15", parsed.ReceiptDate);
        Assert.Equal("123", parsed.ExternalId);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal("Milk", line.Description);
        Assert.Equal(-4.99m, line.Amount);
        Assert.Equal("Groceries", line.Category);
    }

    [Fact]
    public void Deserialize_maps_camel_case_and_string_amounts()
    {
        const string json = """
            {"merchant":"Costco","receiptDate":"2026-08-15","lineItems":[{"description":"Water","amount":"-$12.50","category":"Groceries"}]}
            """;

        var parsed = LlmJson.Deserialize<SampleReceipt>(json);

        Assert.NotNull(parsed);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal(-12.50m, line.Amount);
        Assert.Equal("2026-08-15", parsed.ReceiptDate);
    }

    [Fact]
    public void Deserialize_reads_fenced_json_with_trailing_comma()
    {
        const string json = """
            Sure, here you go:
            ```json
            {"merchant":"Shell","line_items":[{"description":"Fuel","amount":-40.00,}],}
            ```
            """;

        var parsed = LlmJson.Deserialize<SampleReceipt>(json);

        Assert.NotNull(parsed);
        Assert.Equal("Shell", parsed.Merchant);
        Assert.Equal(-40.00m, Assert.Single(parsed.LineItems).Amount);
    }

    [Fact]
    public void ReadOpenAiMessageContent_supports_array_content_parts()
    {
        const string payload = """
            {"choices":[{"message":{"content":[{"type":"text","text":"{\"merchant\":\"A\"}"}]}}]}
            """;
        using var doc = JsonDocument.Parse(payload);

        var content = LlmVisionHelper.ReadOpenAiMessageContent(doc.RootElement);

        Assert.Equal("""{"merchant":"A"}""", content);
    }

    private sealed class SampleReceipt
    {
        public string? Merchant { get; set; }
        public string? ReceiptDate { get; set; }
        public string? ExternalId { get; set; }
        public List<SampleLine> LineItems { get; set; } = [];
    }

    private sealed class SampleLine
    {
        public decimal? Amount { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
    }
}
