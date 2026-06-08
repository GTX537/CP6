using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Msbb.Domain.Entities
{
    [Table("T_SYS_LOG")]
    public class SysLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // 自动递增 ID
        public long LogSeq { get; set; }

        public DateTime LogDate { get; set; } = DateTime.Now;

        [MaxLength(50)]
        public string? LoginID { get; set; }

        [MaxLength(10)]
        public string? BaseCD { get; set; } // 据点CD

        [MaxLength(20)]
        public string? UserCD { get; set; } // 担当者CD

        [MaxLength(1)]
        public string LogType { get; set; } = "1"; // 1:登录, 2:操作, 9:错误

        [MaxLength(100)]
        public string Title { get; set; } = string.Empty;

        public string? Content { get; set; } // NVARCHAR(MAX)

        [MaxLength(20)]
        public string? Result { get; set; }
    }
}