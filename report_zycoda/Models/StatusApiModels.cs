using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace report_zycoda.Models
{
    [Table("Status")] // ระบุชื่อตารางให้ตรงกับใน DB
    public class StatusApiModels
    {
        [Key]
        public int? Id { get; set; }

        [Required]
        public string StatusId { get; set; } = string.Empty;

        [Required]
        public string StatusName { get; set; } = string.Empty;
    }
}