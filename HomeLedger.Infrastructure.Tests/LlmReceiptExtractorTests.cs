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

    [Fact]
    public void TryParseResponse_uses_extended_total_when_model_returns_unit_price()
    {
        const string json = """
            {"merchant":"Shoppers Drug Mart","receipt_date":"2026/08/14","line_items":[
              {"description":"KENDAMIL INF F","amount":-56.99,"quantity":5,"unit_price":-56.99,"category":"Groceries"},
              {"description":"BURTS BEES OIN","amount":-13.99,"quantity":1,"category":"Personal Care"}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.LineItems.Count);
        Assert.Equal(-284.95m, parsed.LineItems[0].Amount);
        Assert.Equal("KENDAMIL INF F", parsed.LineItems[0].Description);
        Assert.Equal(5m, parsed.LineItems[0].Quantity);
        Assert.Equal("ea", parsed.LineItems[0].QuantityUnit);
        Assert.Equal(56.99m, parsed.LineItems[0].UnitPrice);
        Assert.Equal(-13.99m, parsed.LineItems[1].Amount);
        Assert.Equal("BURTS BEES OIN", parsed.LineItems[1].Description);
        Assert.Equal(1m, parsed.LineItems[1].Quantity);
        Assert.Equal("ea", parsed.LineItems[1].QuantityUnit);
        Assert.Equal(13.99m, parsed.LineItems[1].UnitPrice);
    }

    [Fact]
    public void TryParseResponse_keeps_extended_total_when_quantity_is_present()
    {
        const string json = """
            {"merchant":"Shoppers Drug Mart","receipt_date":"2026/08/14","line_items":[
              {"description":"5x KENDAMIL INF F","amount":-284.95,"quantity":5,"unit_price":-56.99,"category":"Groceries"}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal(-284.95m, line.Amount);
        Assert.Equal("KENDAMIL INF F", line.Description);
        Assert.Equal(5m, line.Quantity);
        Assert.Equal("ea", line.QuantityUnit);
        Assert.Equal(56.99m, line.UnitPrice);
    }

    [Fact]
    public void TryParseResponse_computes_amount_from_quantity_and_unit_price()
    {
        const string json = """
            {"merchant":"Shoppers Drug Mart","line_items":[
              {"description":"KENDAMIL INF F","quantity":5,"unit_price":-56.99,"category":"Groceries"}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal(-284.95m, line.Amount);
        Assert.Equal("KENDAMIL INF F", line.Description);
        Assert.Equal(5m, line.Quantity);
        Assert.Equal(56.99m, line.UnitPrice);
    }

    [Fact]
    public void TryParseResponse_corrects_unit_price_amount_when_signs_differ()
    {
        const string json = """
            {"merchant":"Shoppers Drug Mart","line_items":[
              {"description":"KENDAMIL INF F","amount":-56.99,"quantity":5,"unit_price":56.99,"category":"Groceries"}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal(-284.95m, line.Amount);
        Assert.Equal(5m, line.Quantity);
        Assert.Equal(56.99m, line.UnitPrice);
    }

    [Fact]
    public void TryParseResponse_reads_weighed_produce_by_kilogram()
    {
        const string json = """
            {"merchant":"Grocery","receipt_date":"2026/08/14","line_items":[
              {"description":"BANANAS","amount":-1.11,"quantity":0.640,"quantity_unit":"kg","unit_price":1.74,"category":"Groceries"}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal("BANANAS", line.Description);
        Assert.Equal(-1.11m, line.Amount);
        Assert.Equal(0.640m, line.Quantity);
        Assert.Equal("kg", line.QuantityUnit);
        Assert.Equal(1.74m, line.UnitPrice);
    }
}
