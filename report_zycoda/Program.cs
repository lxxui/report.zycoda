var builder = WebApplication.CreateBuilder(args);

// ----------------------
// Services
// ----------------------

builder.Services.AddControllersWithViews();

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
// Middleware
// ----------------------

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Session ต้องอยู่ตรงนี้
app.UseSession();

app.UseAuthorization();

// ----------------------
// Routing
// ----------------------

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.Run();