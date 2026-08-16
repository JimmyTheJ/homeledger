using HomeLedger.Infrastructure.Llm;
using Xunit;

namespace HomeLedger.Infrastructure.Tests;

public class LlmReceiptExtractorTests
{
    [Fact]
    public void TryParseResponse_reads_prompt_shaped_snake_case_json()
    {
        const string json = """
            {"merchant":"Walmart","receipt_date":"2026/08/15","external_id":null,"line_items":[{"description":"Bananas","amount":-2.47,"category":"Groceries"}]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        Assert.Equal("Walmart", parsed.Merchant);
        Assert.Equal(new DateOnly(2026, 8, 15), parsed.ReceiptDate);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal("Bananas", line.Description);
        Assert.Equal(-2.47m, line.Amount);
        Assert.Equal("Groceries", line.SuggestedCategoryName);
        Assert.Equal(new DateOnly(2026, 8, 15), line.Date);
    }

    [Fact]
    public void TryParseResponse_keeps_line_items_when_date_is_missing()
    {
        const string json = """
            {"merchant":"Cafe","line_items":[{"description":"Coffee","amount":-4.25,"category":"Dining"}]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), line.Date);
        Assert.Equal("Cafe", parsed.Merchant);
    }

    [Fact]
    public void TryParseResponse_returns_null_for_empty_line_items()
    {
        const string json = """{"merchant":"Walmart","line_items":[]}""";

        Assert.Null(LlmReceiptExtractor.TryParseResponse(json));
    }
}
