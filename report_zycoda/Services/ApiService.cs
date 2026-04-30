using report_zycoda.Data;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using report_zycoda.Models;

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
        // ดึง URL จาก appsettings.json ถ้าไม่มีให้ใช้ค่า Default
        _externalApiUrl = _config["ApiSettings:BaseUrl"]?.TrimEnd('/') ?? "https://api.zycoda.com/apimpros";
    }

    // ✅ 1. Login: ตรวจสอบจากตาราง [User] ใน Database ของเราเอง
    public async Task<UserApiModels?> LoginAsync(string username, string password)
    {
        try
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return null;

            // ค้นหาพนักงานที่ Username และ Password ตรงกัน และต้องเป็น Active
            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.Username == username.Trim() &&
                    u.Password == password &&
                    u.Active == true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"🚩 DB Login Error: {ex.Message}");
            return null;
        }
    }

    // ✅ 2. ดึงข้อมูลแผนก: ดึงตรงจากตาราง [Section] (ใช้แทนไฟล์ JSON)
    public async Task<List<SectionApiModels>> GetSectionsFromApiAsync()
    {
        try
        {
            // ดึงข้อมูลแผนกทั้งหมด และเรียงตามชื่อแผนก
            return await _context.Sections
                .AsNoTracking()
                .OrderBy(s => s.Sections)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"🚩 DB Section Error: {ex.Message}");
            return new List<SectionApiModels>();
        }
    }

    public async Task<List<StatusApiModels>> GetStatusesFromApiAsync()
    {
        try
        {
            return await _context.Statuses.AsNoTracking().ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"🚩 DB Status Error: {ex.Message}");
            return new List<StatusApiModels>();
        }
    }


    // ✅ 3. ดึงงานซ่อม (Maintenance): เรียกผ่าน External API 
    public async Task<List<MaintenanceApiModels>> GetJobs(string jobtype, string start, string end, string? user = null, string? pwd = null)
    {
        try
        {
            // ใช้ข้อมูล User/Pass จากที่ส่งมา (ตอน Login) หรือจาก Config
            var apiUsername = user ?? _config["ApiSettings:Username"];
            var apiPassword = pwd ?? _config["ApiSettings:Password"];

            // จัดการค่า Default สำหรับวันและประเภทงาน
            jobtype = string.IsNullOrEmpty(jobtype) ? "EM,CM" : jobtype;
            start = string.IsNullOrEmpty(start) ? DateTime.Today.ToString("yyyy-MM-dd") : start;
            end = string.IsNullOrEmpty(end) ? DateTime.Today.ToString("yyyy-MM-dd") : end;

            // สร้าง URL สำหรับดึงข้อมูล
            string url = $"{_externalApiUrl}/get_job_order?plant=FARMHOUSE&jobtype={jobtype}&start={start}&end={end}&v=all&status=ALL";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("username", apiUsername);
            _httpClient.DefaultRequestHeaders.Add("password", apiPassword);

            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<MaintenanceApiModels>>(json) ?? new();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"🚩 API Error: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"🚩 External API Exception: {ex.Message}");
        }
        return new List<MaintenanceApiModels>();
    }

    // ✅ 4. ดึงรายชื่อพนักงานทั้งหมด (สำหรับหน้า Admin หรือตรวจสอบ)
    public async Task<List<UserApiModels>> GetUsersFromApiAsync()
    {
        try
        {
            return await _context.Users.AsNoTracking().ToListAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"🚩 DB Error: {ex.Message}");
            return new List<UserApiModels>();
        }
    }
}