using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore; // 🚩 เพิ่มตัวนี้
using report_zycoda.Data;           // 🚩 เปลี่ยนเป็น Namespace ของ AppDbContext ฝ้าย
using report_zycoda.Models;

var builder = WebApplication.CreateBuilder(args);

// ----------------------
// Services
// ----------------------

// 1. เชื่อมต่อ Database (ใช้ ConnectionString เดียวกับ API)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddControllersWithViews();

// 2. Authentication Service
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Login";
        options.AccessDeniedPath = "/Home/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

// 3. Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// 4. HttpContext & Api Service
builder.Services.AddHttpContextAccessor();

// 🚩 ปรับการลงทะเบียน ApiService ให้รองรับการฉีด AppDbContext
builder.Services.AddHttpClient<ApiService>();
builder.Services.AddScoped<ApiService>();

var app = builder.Build();

// ----------------------
// Middleware (ลำดับสำคัญมาก!)
// ----------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// ลำดับ: Session -> Authentication -> Authorization
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// ----------------------
// Routing
// ----------------------
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.Run();