using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace report_zycoda.Models
{
    [Table("Section")]
    public class SectionApiModels
    {
        [Key]
        public int Id { get; set; }
        public string? Id_Zy { get; set; }
        public string? Sections { get; set; }
        public string? Plant { get; set; }
        public string? Name { get; set; }
        public string? PermitRequest { get; set; }
        public string? WorkCenter { get; set; }

        public DateTime? LastUpdateDate { get; set; }
        public string? LastUpdateBy { get; set; }

        // 🚩 แก้ไขจุดนี้: เติม ? เข้าไปเพื่อรองรับค่า NULL จากฐานข้อมูลค่ะ
        public string? Flag_Report { get; set; }

    }
}