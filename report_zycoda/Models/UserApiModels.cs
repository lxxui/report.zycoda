using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace report_zycoda.Models
{
    public class UserApiModels
    {
        [JsonPropertyName("username")] // ระบุให้ชัดเจน
        public string? username { get; set; }

        [JsonPropertyName("password")]
        public string? password { get; set; }

        [JsonPropertyName("firstname")]
        public string? firstname { get; set; }

        [JsonPropertyName("lastname")]
        public string? lastname { get; set; }

        [JsonPropertyName("section")]
        public string? section { get; set; }

        [JsonPropertyName("rule")]
        public string? rule { get; set; }
        public string? sectionoption { get; set; }
        public string? smallgroup { get; set; }
        public string? active { get; set; } // รับค่า "on"
        public string? tel { get; set; }
        public string? work_center { get; set; }
        public string? userAD { get; set; }
        public object? subUsers { get; set; } // ใช้ object เพราะเป็น List หรือ null
        public string? expire_active { get; set; }
    }
}