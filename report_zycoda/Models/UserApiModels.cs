using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace report_zycoda.Models
{
    [Table("User")] // ระบุให้ตรงกับ [dbo].[User] ใน SQL
    public class UserApiModels
    {
        [Key]
        public int Id { get; set; }

        public string? Id_Zy { get; set; }

        public string? Plant { get; set; }

        public string? FirstName { get; set; }

        public string? LastName { get; set; }

        public string? Email { get; set; }

        public string? Username { get; set; }

        public string? Password { get; set; }

        public string? Position { get; set; }

        public string? Section { get; set; }

        public string? PlannerGroup { get; set; }

        public string? SectionOption { get; set; }

        public string? Class { get; set; }

        public string? Rule { get; set; }

        public bool Active { get; set; }

        public string? Smallgroup { get; set; }

        public string? Tel { get; set; }

        public string? Work_center { get; set; }

        public string? UserAD { get; set; }

        public string? SubUsers { get; set; }

        public string? Expire_active { get; set; }

        public string? LaborRateId { get; set; }

        public string? Last_modify { get; set; }

        public string? Workpremit_class { get; set; }

        public string? Plantoption { get; set; }


    }
}
