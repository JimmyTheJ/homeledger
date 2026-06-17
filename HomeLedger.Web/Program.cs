using HomeLedger.Infrastructure;
using HomeLedger.Infrastructure.Data;
using HomeLedger.Web.ModelBinding;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(0, new HomeLedgerDateModelBinderProvider());
});
builder.Services.AddLedgerInfrastructure(builder.Configuration);

var app = builder.Build();

var dataDir = Path.Combine(app.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDir);

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
