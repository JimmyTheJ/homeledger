using HomeLedger.Core.Configuration;
using HomeLedger.Infrastructure;
using HomeLedger.Infrastructure.Data;
using HomeLedger.Infrastructure.Llm;
using HomeLedger.Web.ModelBinding;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

var dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDir);
builder.Configuration.AddJsonFile(
    Path.Combine(dataDir, LlmSettingsOverlayStore.FileName),
    optional: true,
    reloadOnChange: true);

const long maxUploadBytes =
    (long)ReceiptInboxSettings.DefaultMaxFileSizeBytes
    * ReceiptInboxSettings.DefaultMaxFilesPerUpload;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBytes;
});
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = maxUploadBytes;
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
});

builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(0, new HomeLedgerDateModelBinderProvider());
});
builder.Services.AddLedgerInfrastructure(builder.Configuration);

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "receipts-inbox"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HomeLedgerDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
    await DatabaseInitializer.UpgradeLegacyBaselineAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
