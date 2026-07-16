using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using report_zycoda.Data;
using report_zycoda.Models;
using report_zycoda.Services;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// ==================================================
// 1. CULTURE (ป้องกันปัญหาวันที่เพี้ยนระหว่าง พ.ศ. / ค.ศ.)
// ==================================================
var culture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// ==================================================
// 2. DATABASE CONFIGURATION
// ==================================================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<myDbContext>(options =>
    options.UseSqlServer(connectionString));

// ==================================================
// 3. MVC CONTROLLERS & VIEWS
// ==================================================
builder.Services.AddControllersWithViews();

// ==================================================
// 4. AUTHENTICATION (Cookie)
// ==================================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Login";
        options.LogoutPath = "/Home/Logout";
        options.AccessDeniedPath = "/Home/AccessDenied";

        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;

        options.Cookie.Name = "ZycodaAuthCookie";

        // 🔥 ป้องกัน Login หลุดแบบสุ่ม สิทธิ์ปลอดภัยระดับ Enterprise
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

// ==================================================
// 5. SESSION CONFIGURATION
// ==================================================
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "ZycodaSession";
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// ==================================================
// 6. SERVICES & NAMED HTTPCLIENT
// ==================================================
builder.Services.AddHttpContextAccessor();

// 🚀 ลงทะเบียน Named HttpClient สำหรับ MaintenanceController เพื่อใช้ในการทำ Streaming JSON
builder.Services.AddHttpClient("ZycodaApiClient", client =>
{
    client.BaseAddress = new Uri("https://farmhouse.zycoda.com/"); // 👈 มั่นใจว่าลงท้ายด้วย /
    client.Timeout = TimeSpan.FromSeconds(20);                     // ⏱️ Timeout 20 วินาทีป้องกัน API ค้างยาว
    client.DefaultRequestHeaders.Add("accept", "application/json");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // เปิด Decompression รองรับการบีบอัดข้อมูลก้อนใหญ่จากส่วนกลาง
    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
});

// HttpClient แบบลงทะเบียนทั่วไปในระบบ
builder.Services.AddHttpClient();

// ลงทะเบียน Service ภายในแอปพลิเคชัน
builder.Services.AddScoped<ApiService>();
builder.Services.AddSingleton<LatestSyncStore>();

// [Optional] หากต้องการเปิดใช้งาน Background Worker ในอนาคตสามารถปลดคอมเมนต์ได้เลยครับ
// builder.Services.AddHostedService<MaintenanceWorker>();

var app = builder.Build();

// ==================================================
// 7. DATABASE CONNECTION CHECK (DEBUG)
// ==================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<myDbContext>();
        if (context.Database.CanConnect())
        {
            Console.WriteLine("🔹 [Database] CONNECTED SUCCESS");
        }
        else
        {
            Console.WriteLine("❌ [Database] NOT CONNECTED");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"💥 [Database Error] Exception: {ex.Message}");
    }
}

// ==================================================
// 8. MIDDLEWARE PIPELINE (ปรับลำดับให้ถูกต้องเรียบร้อย)
// ==================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

//app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// ⚡ ย้าย UseSession มาไว้ตรงนี้ (ก่อน Auth) เพื่อให้สิทธิ์ Cookie เรียกอ่านข้อมูล Session ได้ถูกต้อง
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.Run();