using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using report_zycoda.Data;
using report_zycoda.Models;
using report_zycoda.Services;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// 1. Database Connection
// ------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<myDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddControllersWithViews();

// ------------------------------------------------------------------
// 2. Authentication (สำคัญ: ตรวจสอบการตั้งค่า Cookie)
// ------------------------------------------------------------------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Login";          // หน้าสำหรับ Login
        options.LogoutPath = "/Home/Logout";        // หน้าสำหรับ Logout
        options.AccessDeniedPath = "/Home/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60); // ปรับให้เท่ากับ Session
        options.SlidingExpiration = true;           // ต่ออายุอัตโนมัติเมื่อมีการใช้งาน
        options.Cookie.Name = "ZycodaAuthCookie";    // ตั้งชื่อ Cookie ให้ชัดเจน
    });

// ------------------------------------------------------------------
// 3. Session Configuration
// ------------------------------------------------------------------
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "ZycodaSession";         // ตั้งชื่อแยกกับ Auth Cookie
});

// แก้ตรงส่วน DI Services ให้เป็นแบบนี้:
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient(); // ใช้ตัวนี้แทน AddHttpClient<ApiService>() ถ้าไม่ได้ตั้งค่าพิเศษ
builder.Services.AddScoped<ApiService>();
//builder.Services.AddHostedService<JobSyncService>();

var app = builder.Build();

// ✅ Database Connection Check (เก็บไว้เหมือนเดิมเพื่อความชัวร์)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<myDbContext>();
        if (context.Database.CanConnect())
        {
            Console.WriteLine("✅ [DATABASE STATUS]: CONNECTION SUCCESS!");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"🚩 [DATABASE ERROR]: {ex.Message}");
    }
}

// ------------------------------------------------------------------
// 5. Middleware Pipeline (ลำดับห้ามสลับ!)
// ------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// 🚩 ลำดับต้องเป็น: Session -> Authentication -> Authorization
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.Run();
