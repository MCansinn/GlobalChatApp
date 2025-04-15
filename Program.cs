using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// SignalR servisinde maksimum mesaj boyutunu 1 MB'a ��kar�yoruz.
// Bu sayede ses kayd� gibi base64 kodlanm�� veriler, varsay�lan 32 KB limitini a�madan iletilebilir.
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1 MB
});

builder.Services.AddControllersWithViews();

// Cookie tabanl� authentication ve authorization ekleniyor.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

// E�er kullan�c� kimlik do�rulanmam��sa, query string'den gelen bilgileri kullanarak otomatik giri� yap.
app.Use(async (context, next) =>
{
    if (!context.User.Identity.IsAuthenticated)
    {
        var username = context.Request.Query["username"].ToString();
        var gender = context.Request.Query["gender"].ToString();
        var language = context.Request.Query["language"].ToString();

        if (!string.IsNullOrEmpty(username) &&
            !string.IsNullOrEmpty(gender) &&
            !string.IsNullOrEmpty(language))
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim("gender", gender),
                new Claim("language", language)
            };

            var identity = new ClaimsIdentity(claims, "Custom");
            context.User = new ClaimsPrincipal(identity);
        }
    }
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// SignalR Hub'�n� "/chathub" endpoint'ine e�leyin.
app.MapHub<ChatHub>("/chathub");

app.Run();
