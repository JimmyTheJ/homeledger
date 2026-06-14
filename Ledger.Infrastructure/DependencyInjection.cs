using Ledger.Core.Configuration;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Import;
using Ledger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLedgerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName));
        services.Configure<LlmSettings>(configuration.GetSection(LlmSettings.SectionName));

        var connectionString = configuration.GetSection(DatabaseSettings.SectionName)
            .Get<DatabaseSettings>()?.ConnectionString ?? "Data Source=data/ledger.db";

        services.AddDbContext<LedgerDbContext>(options =>
            options.UseSqlite(connectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(LedgerDbContext).Assembly.GetName().Name)));

        services.AddScoped<ICsvImportService, CsvImportService>();
        services.AddScoped<ITransactionCategorizer, TransactionCategorizer>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IReportService, ReportService>();

        var llmEnabled = configuration.GetSection(LlmSettings.SectionName).Get<LlmSettings>()?.Enabled ?? false;
        if (llmEnabled)
        {
            services.AddHttpClient<ILlmClient, LlmClient>((sp, client) =>
            {
                var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmSettings>>().Value;
                client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromMinutes(2);
            });
        }
        else
        {
            services.AddSingleton<ILlmClient, NullLlmClient>();
        }

        return services;
    }
}
