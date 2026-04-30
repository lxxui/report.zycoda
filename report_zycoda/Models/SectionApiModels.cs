using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace report_zycoda.Models
{
    [Table("Section")] // <--- ระบุชื่อตารางให้ตรงกับใน SQL Server
    public class SectionApiModels
    {
        [Key] // ระบุว่าเป็น Primary Key
        public int Id { get; set; }  // 👈 DB generate auto
        public string? Id_Zy { get; set; }
        public string? Sections { get; set; }
        public string? Plant { get; set; }
        public string? Name { get; set; }
        public string? PermitRequest { get; set; }
        public string? WorkCenter { get; set; }

        public DateTime? LastUpdateDate { get; set; }
        public string? LastUpdateBy { get; set; }

    }
}
