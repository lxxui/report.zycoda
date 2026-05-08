using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using report_zycoda.Data;
using report_zycoda.Models;
using report_zycoda.Services;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// ===============================
// 1. CULTURE (กันวันที่เพี้ยน)
// ===============================
var culture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// ===============================
// 2. DB
// ===============================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<myDbContext>(options =>
    options.UseSqlServer(connectionString));

// ===============================
// 3. MVC
// ===============================
builder.Services.AddControllersWithViews();

// ===============================
// 4. AUTH
// ===============================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Login";
        options.LogoutPath = "/Home/Logout";
        options.AccessDeniedPath = "/Home/AccessDenied";

        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;

        options.Cookie.Name = "ZycodaAuthCookie";

        // 🔥 เพิ่มอันนี้ (กัน login หลุด random)
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

// ===============================
// 5. SESSION
// ===============================
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "ZycodaSession";

    // 🔥 เพิ่ม safety
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// ===============================
// 6. SERVICES
// ===============================
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ApiService>();

var app = builder.Build();

// ===============================
// 7. DATABASE CHECK (DEBUG ONLY)
// ===============================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    try
    {
        var context = services.GetRequiredService<myDbContext>();

        if (context.Database.CanConnect())
        {
            Console.WriteLine("DATABASE CONNECTED SUCCESS");
        }
        else
        {
            Console.WriteLine("DATABASE NOT CONNECTED");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DATABASE ERROR: {ex.Message}");
    }
}

// ===============================
// 8. PIPELINE
// ===============================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.Run();