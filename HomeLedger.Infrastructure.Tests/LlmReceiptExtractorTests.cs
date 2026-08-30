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

    [Fact]
    public void TryParseResponse_treats_refund_receipt_printed_negative_as_positive()
    {
        const string json = """
            {"merchant":"Farm Boy","receipt_date":"2026/08/08","is_refund":true,"line_items":[
              {"description":"SAUS.ITAL.MILD 500G","amount":-7.99,"quantity":-1,"unit_price":7.99,"category":"Groceries","is_return":true}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal("SAUS.ITAL.MILD 500G", line.Description);
        Assert.Equal(7.99m, line.Amount);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal("ea", line.QuantityUnit);
        Assert.Equal(7.99m, line.UnitPrice);
    }

    [Fact]
    public void TryParseResponse_treats_unsigned_purchase_as_negative()
    {
        const string json = """
            {"merchant":"Shoppers Drug Mart","receipt_date":"2026/08/11","is_refund":false,"line_items":[
              {"description":"ZINCOFAX CREME","amount":12.75,"quantity":1,"unit_price":12.75,"category":"Personal Care","is_return":false}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal("ZINCOFAX CREME", line.Description);
        Assert.Equal(-12.75m, line.Amount);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal(12.75m, line.UnitPrice);
    }

    [Fact]
    public void TryParseResponse_uses_negative_quantity_as_return_when_flags_omitted()
    {
        const string json = """
            {"merchant":"Farm Boy","line_items":[
              {"description":"SAUS.ITAL.MILD 500G","amount":-7.99,"quantity":-1,"category":"Groceries"}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal(7.99m, line.Amount);
        Assert.Equal(1m, line.Quantity);
    }

    [Fact]
    public void TryParseResponse_keeps_purchase_negative_when_return_flags_omitted()
    {
        const string json = """
            {"merchant":"Shoppers Drug Mart","line_items":[
              {"description":"ZINCOFAX CREME","amount":-12.75,"category":"Personal Care"}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        Assert.Equal(-12.75m, Assert.Single(parsed.LineItems).Amount);
    }

    [Fact]
    public void TryParseResponse_uses_receipt_refund_flag_when_line_flags_omitted()
    {
        const string json = """
            {"merchant":"Farm Boy","is_refund":"true","line_items":[
              {"description":"SAUS.ITAL.MILD 500G","amount":-7.99,"category":"Groceries"}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        Assert.Equal(7.99m, Assert.Single(parsed.LineItems).Amount);
    }

    [Fact]
    public void TryParseResponse_signs_mixed_purchase_and_return_lines()
    {
        const string json = """
            {"merchant":"Grocery","is_refund":false,"line_items":[
              {"description":"Milk","amount":-4.99,"is_return":false,"category":"Groceries"},
              {"description":"Yogurt","amount":-3.49,"is_return":true,"category":"Groceries"}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.LineItems.Count);
        Assert.Equal(-4.99m, parsed.LineItems[0].Amount);
        Assert.Equal(3.49m, parsed.LineItems[1].Amount);
    }

    [Fact]
    public void TryParseResponse_applies_return_sign_to_quantity_extended_total()
    {
        const string json = """
            {"merchant":"Shoppers Drug Mart","is_refund":true,"line_items":[
              {"description":"KENDAMIL INF F","amount":-56.99,"quantity":5,"unit_price":56.99,"category":"Groceries","is_return":true}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        var line = Assert.Single(parsed.LineItems);
        Assert.Equal(284.95m, line.Amount);
        Assert.Equal(5m, line.Quantity);
        Assert.Equal(56.99m, line.UnitPrice);
    }

    [Fact]
    public void TryParseResponse_keeps_repeated_identical_product_rows()
    {
        const string json = """
            {"merchant":"Once Upon A Child","receipt_date":"2026/08/29","subtotal":26.50,"line_items":[
              {"description":"S000652700 Bodysuit","amount":-1.50,"quantity":1,"unit_price":1.50,"category":"Clothing & Shoes"},
              {"description":"S000498048 Bodysuit","amount":-1.50,"quantity":1,"unit_price":1.50,"category":"Clothing & Shoes"},
              {"description":"S000677823 Bodysuit","amount":-1.50,"quantity":1,"unit_price":1.50,"category":"Clothing & Shoes"},
              {"description":"S000158436 Bodysuit","amount":-1.50,"quantity":1,"unit_price":1.50,"category":"Clothing & Shoes"},
              {"description":"S000662866 Bodysuit","amount":-1.50,"quantity":1,"unit_price":1.50,"category":"Clothing & Shoes"},
              {"description":"S000646285 Book","amount":-3.50,"quantity":1,"unit_price":3.50,"category":"Books & Music"},
              {"description":"S000621743 Book","amount":-2.50,"quantity":1,"unit_price":2.50,"category":"Books & Music"},
              {"description":"S000543786 Book","amount":-1.50,"quantity":1,"unit_price":1.50,"category":"Books & Music"},
              {"description":"S000532193 Book","amount":-1.50,"quantity":1,"unit_price":1.50,"category":"Books & Music"},
              {"description":"S000596897 Paperback Book","amount":-1.50,"quantity":1,"unit_price":1.50,"category":"Books & Music"},
              {"description":"S000704881 Guitar","amount":-8.50,"quantity":1,"unit_price":8.50,"category":"Books & Music"}
            ]}
            """;

        var parsed = LlmReceiptExtractor.TryParseResponse(json);

        Assert.NotNull(parsed);
        Assert.Equal(26.50m, parsed.Subtotal);
        Assert.Equal(11, parsed.LineItems.Count);
        Assert.Equal(5, parsed.LineItems.Count(l => l.Description.Contains("Bodysuit", StringComparison.Ordinal)));
        Assert.Equal(-26.50m, parsed.LineItems.Sum(l => l.Amount));
        Assert.False(LlmReceiptExtractor.HasSubtotalGap(parsed, out _));
    }

    [Fact]
    public void HasSubtotalGap_detects_collapsed_once_upon_a_child_lines()
    {
        var collapsed = new ExtractedReceipt(
            "Once Upon A Child",
            new DateOnly(2026, 8, 29),
            "109582",
            [
                new(new DateOnly(2026, 8, 29), -1.50m, "Bodysuit", "Clothing & Shoes"),
                new(new DateOnly(2026, 8, 29), -3.50m, "Book", "Books & Music"),
                new(new DateOnly(2026, 8, 29), -2.50m, "Book", "Books & Music"),
                new(new DateOnly(2026, 8, 29), -8.50m, "Guitar", "Books & Music")
            ],
            Subtotal: 26.50m);

        Assert.True(LlmReceiptExtractor.HasSubtotalGap(collapsed, out var lineSum));
        Assert.Equal(-16.00m, lineSum);
    }

    [Fact]
    public void ExtractionPromptTemplate_requires_one_object_per_printed_row()
    {
        Assert.Contains("one line_items object per printed product/service row", LlmReceiptExtractor.ExtractionPromptTemplate);
        Assert.Contains("five \"Bodysuit 1.50\" rows", LlmReceiptExtractor.ExtractionPromptTemplate);
        Assert.Contains("different SKUs", LlmReceiptExtractor.ExtractionPromptTemplate);
    }
}
