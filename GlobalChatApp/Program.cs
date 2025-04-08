using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Gerekli servislerin eklenmesi.
builder.Services.AddSignalR();
builder.Services.AddControllersWithViews();  // MVC kullanmak isterseniz

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Varsayýlan dosyalarý sunmak için (örneðin wwwroot/index.html)
app.UseDefaultFiles();

// wwwroot klasöründeki statik dosyalarý sunmak için
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// MVC rotasý (opsiyonel)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// SignalR Hub'ýný "/chathub" endpoint'ine eþleyin.
app.MapHub<ChatHub>("/chathub");

app.Run();
