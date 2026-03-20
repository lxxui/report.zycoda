using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ----------------------
// Services
// ----------------------
builder.Services.AddControllersWithViews();

// 1. เพิ่ม Authentication Service (จำเป็นมากเพื่อให้ User.IsInRole ทำงานได้)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Login";
        options.AccessDeniedPath = "/Home/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);

        // 🚩 เพิ่มบรรทัดนี้ ถ้ายังไม่ได้รัน HTTPS จริงจัง
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// HttpContext
builder.Services.AddHttpContextAccessor();

// API Service
builder.Services.AddHttpClient<ApiService>();

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

// ลำดับที่ถูกต้อง: Session -> Authentication -> Authorization
app.UseSession();

app.UseAuthentication(); // 2. ต้องมีบรรทัดนี้เพื่อระบุตัวตน (Who are you?)
app.UseAuthorization();  // 3. ตรวจสอบสิทธิ์ (What can you do?)

// ----------------------
// Routing
// ----------------------
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.Run();