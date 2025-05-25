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

// Varsay�lan dosyalar� sunmak i�in (�rne�in wwwroot/index.html)
app.UseDefaultFiles();

// wwwroot klas�r�ndeki statik dosyalar� sunmak i�in
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// MVC rotas� (opsiyonel)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// SignalR Hub'�n� "/chathub" endpoint'ine e�leyin.
app.MapHub<ChatHub>("/chathub");

app.Run();
