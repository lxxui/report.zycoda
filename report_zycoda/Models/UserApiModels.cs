using Microsoft.AspNetCore.Mvc;

namespace report_zycoda.Models
{
    public class UserApiModels
    {
        public int? @class { get; set; } // ใช้ @ นำหน้าเพราะ class เป็นคำสงวนใน C#
        public string? email { get; set; }
        public string? firstname { get; set; }
        public string? id { get; set; }
        public string? password { get; set; }
        public string? lastname { get; set; }
        public string? planner_group { get; set; }
        public string? plant { get; set; }
        public string? position { get; set; }
        public string? rule { get; set; }
        public string? section { get; set; }
        public string? sectionoption { get; set; }
        public string? smallgroup { get; set; }
        public string? username { get; set; }
        public string? active { get; set; } // รับค่า "on"
        public string? tel { get; set; }
        public string? work_center { get; set; }
        public string? userAD { get; set; }
        public object? subUsers { get; set; } // ใช้ object เพราะเป็น List หรือ null
        public string? expire_active { get; set; }
    }
}