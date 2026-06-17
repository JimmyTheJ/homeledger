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

        var connectionString = configuration.GetSection(DatabaseSettings.SectionName)
            .Get<DatabaseSettings>()?.ConnectionString ?? "Data Source=data/homeledger.db";

        services.AddDbContext<HomeLedgerDbContext>(options =>
            options.UseSqlite(connectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(HomeLedgerDbContext).Assembly.GetName().Name)));

        services.AddScoped<ICsvImportService, CsvImportService>();
        services.AddScoped<IHomeLedgerExportService, HomeLedgerExportService>();
        services.AddScoped<IPdfStatementImportService, PdfStatementImportService>();
        services.AddScoped<ITransactionCategorizer, TransactionCategorizer>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IReportService, ReportService>();

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
        }
        else
        {
            services.AddSingleton<ILlmClient, NullLlmClient>();
            services.AddSingleton<ILlmStatementExtractor, NullLlmStatementExtractor>();
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
