using report_zycoda.Data;   // 🚩 เช็ค Namespace ให้ตรงกับ AppDbContext
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using report_zycoda.Models; // สำหรับ MaintenanceApiModels

public class ApiService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly AppDbContext _context;
    private readonly string _externalApiUrl;

    public ApiService(IConfiguration config, HttpClient httpClient, AppDbContext context)
    {
        _config = config;
        _httpClient = httpClient;
        _context = context;
        _externalApiUrl = _config["ApiSettings:BaseUrl"]?.TrimEnd('/') ?? "";
    }

    // ✅ Login ตรงจากตาราง [User]
    public async Task<UserApiModels?> LoginAsync(string username, string password)
    {
        try
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return null;

            // 🚩 ใช้ _context.Users ซึ่งแมพไปที่ตาราง [User] เรียบร้อยแล้ว
            return await _context.Users.FirstOrDefaultAsync(u =>
                u.Username == username &&
                u.Password == password &&
                u.Active == true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"🚩 DB Login Error: {ex.Message}");
            return null;
        }
    }

    // ✅ ดึงรายชื่อพนักงานทั้งหมด
    public async Task<List<UserApiModels>> GetUsersFromApiAsync()
    {
        try
        {
            // 🔍 ลองดึงมาตรงๆ ไม่ผ่านเงื่อนไขใดๆ
            var users = await _context.Users.AsNoTracking().ToListAsync();

            System.Diagnostics.Debug.WriteLine($"✅ Count directly from Context: {users.Count}");
            return users;
        }
        catch (Exception ex)
        {
            // 🚩 ถ้าเข้าตรงนี้ ฝ้ายจะเห็น Error ที่แท้จริง เช่น "Invalid object name 'dbo.User'"
            System.Diagnostics.Debug.WriteLine($"🚩 DB Error: {ex.Message}");
            return new List<UserApiModels>();
        }
    }

    // ✅ ดึงข้อมูลแผนกจากตาราง [Section]
    public async Task<List<SectionApiModels>> GetSectionsFromApiAsync()
    {
        try { return await _context.Sections.ToListAsync(); }
        catch { return new(); }
    }

    // ✅ 2. ดึงงานซ่อม: ยังต้องใช้ HttpClient เพราะเป็น API ภายนอกบริษัท
    public async Task<List<MaintenanceApiModels>> GetJobs(string jobtype, string start, string end, string? user = null, string? pwd = null)
    {
        try
        {
            var apiUsername = user ?? _config["ApiSettings:Username"];
            var apiPassword = pwd ?? _config["ApiSettings:Password"];

            jobtype = string.IsNullOrEmpty(jobtype) ? "EM,CM" : jobtype;
            start = string.IsNullOrEmpty(start) ? DateTime.Today.ToString("yyyy-MM-dd") : start;
            end = string.IsNullOrEmpty(end) ? DateTime.Today.ToString("yyyy-MM-dd") : end;

            string url = $"{_externalApiUrl}/get_job_order?plant=FARMHOUSE&jobtype={jobtype}&start={start}&end={end}";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("username", apiUsername);
            _httpClient.DefaultRequestHeaders.Add("password", apiPassword);

            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new();
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"🚩 External API Error: {ex.Message}"); }
        return new();
    }
}