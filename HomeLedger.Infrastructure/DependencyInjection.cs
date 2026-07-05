using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure.Data;
using HomeLedger.Infrastructure.Export;
using HomeLedger.Infrastructure.Import;
using HomeLedger.Infrastructure.Llm;
using HomeLedger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeLedger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLedgerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName));
        services.Configure<LlmSettings>(configuration.GetSection(LlmSettings.SectionName));

        var databaseSettings = configuration.GetSection(DatabaseSettings.SectionName)
            .Get<DatabaseSettings>() ?? new DatabaseSettings();

        services.AddDbContext<HomeLedgerDbContext>(options =>
            HomeLedgerDbContextOptions.Configure(
                options,
                databaseSettings.ResolvedProvider,
                databaseSettings.ResolvedConnectionString));

        services.AddScoped<ICsvImportService, CsvImportService>();
        services.AddScoped<IHomeLedgerExportService, HomeLedgerExportService>();
        services.AddScoped<IPdfStatementImportService, PdfStatementImportService>();
        services.AddScoped<IReceiptImageImportService, ReceiptImageImportService>();
        services.AddScoped<ITransactionCategorizer, TransactionCategorizer>();
        services.AddScoped<IImportProfileService, ImportProfileService>();
        services.AddScoped<IImportSkipRuleMatcher, ImportSkipRuleMatcher>();
        services.AddScoped<ITransferPairMatcher, TransferPairMatcher>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddHttpClient(nameof(LlmHealthService));
        services.AddScoped<ILlmHealthService, LlmHealthService>();

        var llmSettings = configuration.GetSection(LlmSettings.SectionName).Get<LlmSettings>() ?? new LlmSettings();
        if (llmSettings.Enabled)
        {
            services.AddHttpClient<ILlmClient, LlmClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmSettings>>().Value;
                client.BaseAddress = new Uri(ResolveBaseUrl(settings).TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromMinutes(2);
            });

            services.AddHttpClient<ILlmStatementExtractor, LlmStatementExtractor>((sp, client) =>
            {
                var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmSettings>>().Value;
                client.BaseAddress = new Uri(ResolveBaseUrl(settings).TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromMinutes(5);
            });

            services.AddHttpClient<ILlmReceiptExtractor, LlmReceiptExtractor>((sp, client) =>
            {
                var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmSettings>>().Value;
                client.BaseAddress = new Uri(ResolveBaseUrl(settings).TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromMinutes(5);
            });

            services.AddHttpClient<IImportRowClassifier, ImportRowClassifier>((sp, client) =>
            {
                var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmSettings>>().Value;
                client.BaseAddress = new Uri(ResolveBaseUrl(settings).TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromMinutes(2);
            });
        }
        else
        {
            services.AddSingleton<ILlmClient, NullLlmClient>();
            services.AddSingleton<ILlmStatementExtractor, NullLlmStatementExtractor>();
            services.AddSingleton<ILlmReceiptExtractor, NullLlmReceiptExtractor>();
            services.AddSingleton<IImportRowClassifier, NullImportRowClassifier>();
        }

        return services;
    }

    private static string ResolveBaseUrl(LlmSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            return settings.BaseUrl;

        return LlmProviderDefaults.BaseUrl(settings.ResolvedProvider);
    }
}
